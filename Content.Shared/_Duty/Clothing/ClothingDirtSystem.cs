// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx. Урезано: у нас нет классических частей тела
// (BodyPartComponent/DirtExposures/DirtCoverageLayers — GetBodyChildren и вся линейка body-part
// методов не на чем строить), поэтому TryDirtyBody/TryWashBody/TryDirtyWornSplash/PuddleStep/
// PuddleCrawl и AddDirtTargets не перенесены. Осталось пачканье конкретных предметов одежды
// (TryDirtyClothing) и по слотам инвентаря (TryDirtyWorn) — этого достаточно для стирки/душа.

using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Content.Shared._Duty.Clothing;

public sealed partial class ClothingDirtSystem : EntitySystem
{
    public const string DefaultSolutionName = "dirt";

    /// <summary>Что пачкает кровотечение: кровь проступает на нижний слой одежды.</summary>
    public static readonly SlotFlags BleedSlots = SlotFlags.INNERCLOTHING;

    /// <summary>
    /// Зоны тела и слои одежды, которые их закрывают (от внешнего к внутреннему). В Onyx это
    /// поля частей тела (DirtExposures/DirtCoverageLayers); частей тела у нас нет, поэтому та же
    /// таблица зашита сюда. Грязь попадает на САМЫЙ ВНЕШНИЙ надетый слой каждой задетой зоны.
    /// Парные зоны (руки, ноги, кисти, ступни) перечислены дважды — как две части тела в Onyx,
    /// чтобы доля грязи на предмет делилась так же.
    /// </summary>
    private static readonly (DirtExposure Exposure, SlotFlags[] Layers)[] BodyZones =
    [
        // Торс
        (DirtExposure.Splash | DirtExposure.Crawl | DirtExposure.FullBody,
            [SlotFlags.OUTERCLOTHING, SlotFlags.INNERCLOTHING, SlotFlags.NECK]),
        // Голова
        (DirtExposure.Splash | DirtExposure.Crawl | DirtExposure.Face | DirtExposure.FullBody,
            [SlotFlags.HEAD, SlotFlags.MASK]),
        // Руки
        (DirtExposure.Splash | DirtExposure.Crawl | DirtExposure.FullBody,
            [SlotFlags.OUTERCLOTHING, SlotFlags.INNERCLOTHING]),
        (DirtExposure.Splash | DirtExposure.Crawl | DirtExposure.FullBody,
            [SlotFlags.OUTERCLOTHING, SlotFlags.INNERCLOTHING]),
        // Кисти
        (DirtExposure.Splash | DirtExposure.Crawl | DirtExposure.Hands | DirtExposure.FullBody,
            [SlotFlags.GLOVES]),
        (DirtExposure.Splash | DirtExposure.Crawl | DirtExposure.Hands | DirtExposure.FullBody,
            [SlotFlags.GLOVES]),
        // Ноги
        (DirtExposure.Splash | DirtExposure.Crawl | DirtExposure.FullBody,
            [SlotFlags.OUTERCLOTHING, SlotFlags.INNERCLOTHING]),
        (DirtExposure.Splash | DirtExposure.Crawl | DirtExposure.FullBody,
            [SlotFlags.OUTERCLOTHING, SlotFlags.INNERCLOTHING]),
        // Ступни
        (DirtExposure.Splash | DirtExposure.Ground | DirtExposure.Crawl | DirtExposure.FullBody,
            [SlotFlags.FEET, SlotFlags.SOCKS]),
        (DirtExposure.Splash | DirtExposure.Ground | DirtExposure.Crawl | DirtExposure.FullBody,
            [SlotFlags.FEET, SlotFlags.SOCKS]),
    ];

    private readonly HashSet<EntityUid> _dirtTargets = new();

    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    private readonly HashSet<EntityUid> _drying = new();
    private readonly List<EntityUid> _dryingBuffer = new();
    private float _dryUpdateAccumulator;

    /// <summary>Кэш <see cref="GetCleanMultiplier"/> по id реагента — иначе на каждый реагент
    /// каждой операции заново перебирались бы все ReactiveEffects прототипа.</summary>
    private readonly Dictionary<string, float> _cleanMultipliers = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClothingDirtableComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ClothingDirtableComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ClothingDirtableComponent, ExaminedEvent>(OnExamined);
        // SolutionChangedEvent поднимается на сущности самого раствора, а не на одежде — до
        // владельца доходит только релей SolutionContainerChangedEvent.
        SubscribeLocalEvent<ClothingDirtableComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<ReagentPrototype>())
            _cleanMultipliers.Clear();
    }

    private void OnShutdown(Entity<ClothingDirtableComponent> ent, ref ComponentShutdown args)
        => _drying.Remove(ent.Owner);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer || (_dryUpdateAccumulator += frameTime) < 5f)
            return;

        var elapsed = _dryUpdateAccumulator;
        _dryUpdateAccumulator = 0f;
        _dryingBuffer.Clear();
        _dryingBuffer.AddRange(_drying);
        foreach (var uid in _dryingBuffer)
        {
            if (!TryComp(uid, out ClothingDirtableComponent? dirtable))
            {
                _drying.Remove(uid);
                continue;
            }

            dirtable.DryAccumulator += elapsed;
            if (dirtable.DryAccumulator < dirtable.DryInterval)
                continue;
            dirtable.DryAccumulator %= dirtable.DryInterval;
            DryClothing((uid, dirtable));
        }
    }

    private void OnMapInit(Entity<ClothingDirtableComponent> ent, ref MapInitEvent args)
    {
        if (_solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out var solutionEnt, out var solution))
        {
            if (solution.MaxVolume != ent.Comp.Capacity)
                _solutions.SetCapacity(solutionEnt.Value, ent.Comp.Capacity);
            Refresh(ent, solution);
        }
    }

    private void OnSolutionChanged(Entity<ClothingDirtableComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (!_net.IsServer || args.SolutionId != ent.Comp.Solution)
            return;
        Refresh(ent, args.Solution);
    }

    private void OnExamined(Entity<ClothingDirtableComponent> ent, ref ExaminedEvent args)
    {
        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out _, out var solution) || solution.Volume <= 0 ||
            solution.GetPrimaryReagentId() is not { } primaryId ||
            !_prototype.Resolve<ReagentPrototype>(primaryId.Prototype, out var primary))
            return;

        args.PushMarkup(Loc.GetString("clothing-dirtable-examine",
            ("color", solution.GetColor(_prototype).ToHexNoAlpha()),
            ("desc", primary.LocalizedPhysicalDescription),
            ("chemCount", solution.Contents.Count)));
    }

    public bool TryDirtyClothing(EntityUid clothing, Solution source, FixedPoint2 amount,
        ClothingDirtableComponent? component = null)
    {
        if (!_net.IsServer || amount <= 0 || source.Volume <= 0 ||
            !Resolve(clothing, ref component, false) ||
            !TryEnsureDirtSolution(clothing, component, out var solutionEnt, out var dirt))
            return false;

        var target = FixedPoint2.Min(amount, source.Volume, dirt.AvailableVolume);
        if (target <= 0)
            return false;

        var sample = new Solution();
        var sourceVolume = source.Volume;
        foreach (var reagent in source.Contents)
        {
            var accepted = FixedPoint2.Min(
                reagent.Quantity / sourceVolume * target,
                component.MaxReagentAmount - dirt.GetReagentQuantity(reagent.Reagent),
                target - sample.Volume);
            if (accepted > 0)
                sample.AddReagent(reagent.Reagent, accepted);
        }

        // TryAddSolution/UpdateChemicals поднимают SolutionContainerChangedEvent → Refresh.
        if (sample.Volume <= 0 || !_solutions.TryAddSolution(solutionEnt.Value, sample))
            return false;
        if (ProcessCleaners(dirt))
            _solutions.UpdateChemicals(solutionEnt.Value);
        return true;
    }

    public bool TryAddCleanerToClothing(EntityUid clothing, ReagentId cleaner, FixedPoint2 amount,
        ClothingDirtableComponent? component = null)
    {
        if (!_net.IsServer || amount <= 0 || !Resolve(clothing, ref component, false) ||
            !_prototype.Resolve<ReagentPrototype>(cleaner.Prototype, out var prototype) ||
            GetCleanMultiplier(prototype) <= 0 ||
            !_solutions.TryGetSolution(clothing, component.Solution, out var solutionEnt, out var dirt))
            return false;

        var add = FixedPoint2.Min(amount,
            component.MaxReagentAmount - dirt.GetReagentQuantity(cleaner));
        if (add <= 0)
            return false;

        var spaceNeeded = FixedPoint2.Max(FixedPoint2.Zero, add - dirt.AvailableVolume);
        if (spaceNeeded > 0)
            RemoveWashableDirt(dirt, spaceNeeded);

        add = FixedPoint2.Min(add, dirt.AvailableVolume);
        if (add <= 0)
            return false;

        var solution = new Solution();
        solution.AddReagent(cleaner, add);
        if (!_solutions.TryAddSolution(solutionEnt.Value, solution))
            return false;

        if (ProcessCleaners(dirt))
            _solutions.UpdateChemicals(solutionEnt.Value);
        return true;
    }

    public bool TryWashClothing(EntityUid clothing, ReagentId cleaner, FixedPoint2 amount,
        ClothingDirtableComponent? component = null)
    {
        if (!_net.IsServer || amount <= 0 || !Resolve(clothing, ref component, false) ||
            !_prototype.Resolve<ReagentPrototype>(cleaner.Prototype, out var prototype) ||
            !_solutions.TryGetSolution(clothing, component.Solution, out var solutionEnt, out var dirt))
            return false;

        var multiplier = GetCleanMultiplier(prototype);
        if (multiplier <= 0)
            return false;

        var washable = GetWashableVolume(dirt);
        var remaining = FixedPoint2.Min(amount * multiplier, washable);
        if (remaining <= 0)
            return true;

        var original = remaining;
        foreach (var reagent in dirt.Contents.ToArray())
        {
            if (remaining <= 0 || IsCleaner(reagent.Reagent))
                continue;
            remaining -= dirt.RemoveReagent(reagent.Reagent, FixedPoint2.Min(reagent.Quantity / washable * original, remaining));
        }

        if (remaining > 0)
        {
            foreach (var reagent in dirt.Contents.ToArray())
            {
                if (remaining <= 0 || IsCleaner(reagent.Reagent))
                    continue;
                remaining -= dirt.RemoveReagent(reagent.Reagent, FixedPoint2.Min(reagent.Quantity, remaining));
            }
        }

        _solutions.UpdateChemicals(solutionEnt.Value);
        return original > remaining;
    }

    public bool TryCleanDirt(EntityUid dirtable, float amount, ClothingDirtableComponent? component = null)
    {
        if (!_net.IsServer || amount <= 0 || !Resolve(dirtable, ref component, false) ||
            !_solutions.TryGetSolution(dirtable, component.Solution, out var solutionEnt, out var dirt))
            return false;

        var removed = RemoveWashableDirt(dirt, FixedPoint2.New(amount));
        if (removed <= 0)
            return false;
        _solutions.UpdateChemicals(solutionEnt.Value);
        return true;
    }

    /// <summary>Облили или забрызгали — пачкается всё, что снаружи.</summary>
    public bool TryDirtyWornSplash(EntityUid wearer, Solution source, FixedPoint2 amount)
        => TryDirtyBody(wearer, source, amount, DirtExposure.Splash);

    /// <summary>Прошёл по луже — пачкается обувь (или носки, если босиком).</summary>
    public bool TryDirtyWornPuddleStep(EntityUid wearer, Solution source, FixedPoint2 amount)
        => TryDirtyBody(wearer, source, amount, DirtExposure.Ground);

    /// <summary>Ползёт по луже — пачкается всё тело, каждая зона получает полную порцию.</summary>
    public bool TryDirtyWornPuddleCrawl(EntityUid wearer, Solution source, FixedPoint2 amount)
        => TryDirtyBody(wearer, source, amount, DirtExposure.Crawl, splitAmount: false);

    /// <summary>
    /// Пачкает внешний слой одежды на всех зонах тела, подверженных <paramref name="exposure"/>.
    /// Голые зоны (одежды нет) тоже учитываются в делителе: их долю грязи «забирает кожа», как в
    /// Onyx, — иначе один носок впитывал бы всю лужу.
    /// </summary>
    public bool TryDirtyBody(EntityUid body, Solution source, FixedPoint2 amount, DirtExposure exposure,
        bool splitAmount = true)
    {
        if (!_net.IsServer || amount <= 0 || source.Volume <= 0 || !HasComp<InventoryComponent>(body))
            return false;

        _dirtTargets.Clear();
        var bareZones = 0;
        foreach (var (zoneExposure, layers) in BodyZones)
        {
            if ((zoneExposure & exposure) == 0)
                continue;
            if (!AddOuterLayer(body, layers))
                bareZones++;
        }

        if (_dirtTargets.Count == 0)
            return false;

        var amountPerTarget = splitAmount ? amount / (_dirtTargets.Count + bareZones) : amount;
        var changed = false;
        foreach (var target in _dirtTargets)
            changed |= TryDirtyClothing(target, source, amountPerTarget);
        return changed;
    }

    /// <summary>Добавляет в цели предметы первого из <paramref name="layers"/>, в котором что-то надето.</summary>
    private bool AddOuterLayer(EntityUid body, SlotFlags[] layers)
    {
        foreach (var layer in layers)
        {
            if (!_inventory.TryGetContainerSlotEnumerator(body, out var enumerator, layer))
                continue;

            var found = false;
            while (enumerator.NextItem(out var item))
            {
                if (!HasComp<ClothingDirtableComponent>(item))
                    continue;
                _dirtTargets.Add(item);
                found = true;
            }

            if (found)
                return true;
        }
        return false;
    }

    /// <summary>Пачкает всю одежду в указанных слотах инвентаря (например, при контакте с лужей).</summary>
    public bool TryDirtyWorn(EntityUid wearer, Solution source, FixedPoint2 amount, SlotFlags slots)
    {
        if (!_inventory.TryGetContainerSlotEnumerator(wearer, out var enumerator, slots))
            return false;

        var changed = false;
        while (enumerator.NextItem(out var item))
        {
            if (!TryComp(item, out ClothingDirtableComponent? dirtable))
                continue;
            changed |= TryDirtyClothing(item, source, amount, dirtable);
        }
        return changed;
    }

    private void DryClothing(Entity<ClothingDirtableComponent> ent)
    {
        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out var solutionEnt, out var dirt))
        {
            _drying.Remove(ent.Owner);
            return;
        }

        var changed = ProcessCleaners(dirt);
        foreach (var reagent in dirt.Contents.ToArray())
        {
            if (!_prototype.Resolve<ReagentPrototype>(reagent.Reagent.Prototype, out var prototype) ||
                prototype.EvaporationSpeed <= 0)
                continue;

            var remove = FixedPoint2.Min(prototype.EvaporationSpeed, reagent.Quantity);
            if (remove > 0)
                changed |= dirt.RemoveReagent(reagent.Reagent, remove) > 0;
        }

        // При изменении Refresh придёт через SolutionContainerChangedEvent; иначе сушить нечего —
        // Refresh снимет предмет с сушки.
        if (changed)
            _solutions.UpdateChemicals(solutionEnt.Value);
        else
            Refresh(ent, dirt);
    }

    private bool ProcessCleaners(Solution dirt)
    {
        var changed = false;
        foreach (var cleaner in dirt.Contents.ToArray())
        {
            if (!_prototype.Resolve<ReagentPrototype>(cleaner.Reagent.Prototype, out var prototype))
                continue;

            var multiplier = GetCleanMultiplier(prototype);
            if (multiplier <= 0)
                continue;

            var washable = GetWashableVolume(dirt);
            if (washable <= 0)
                break;

            var cleanAmount = FixedPoint2.Min(
                cleaner.Quantity * multiplier,
                washable);
            var removed = RemoveWashableDirt(dirt, cleanAmount);
            if (removed <= 0)
                continue;

            dirt.RemoveReagent(cleaner.Reagent,
                FixedPoint2.Min(cleaner.Quantity, removed / multiplier));
            changed = true;
        }

        return changed;
    }

    private FixedPoint2 RemoveWashableDirt(Solution dirt, FixedPoint2 amount)
    {
        var washable = GetWashableVolume(dirt);
        var remaining = FixedPoint2.Min(amount, washable);
        var removed = FixedPoint2.Zero;
        if (remaining <= 0)
            return removed;

        var original = remaining;
        foreach (var reagent in dirt.Contents.ToArray())
        {
            if (remaining <= 0 || IsCleaner(reagent.Reagent))
                continue;
            var quantity = FixedPoint2.Min(reagent.Quantity / washable * original, remaining);
            var current = dirt.RemoveReagent(reagent.Reagent, quantity);
            removed += current;
            remaining -= current;
        }

        return removed;
    }

    /// <summary>
    /// Раствор грязи создаётся лениво, при первом попадании грязи. Держать его в прототипе нельзя:
    /// базовые прототипы обуви, перчаток, шапок, униформы и т.д. объявляют свой
    /// SolutionContainerManager (раствор «food» для молей), и он целиком заменяет родительский —
    /// «dirt» у них пропадал, и такие вещи не пачкались вовсе. Заодно чистая одежда (почти вся
    /// одежда на карте) не тащит лишнюю сущность раствора.
    /// </summary>
    private bool TryEnsureDirtSolution(EntityUid clothing, ClothingDirtableComponent component,
        [NotNullWhen(true)] out Entity<SolutionComponent>? solutionEnt, [NotNullWhen(true)] out Solution? solution)
    {
        if (_solutions.TryGetSolution(clothing, component.Solution, out solutionEnt, out solution))
            return true;

        solution = null;
        if (!_net.IsServer ||
            !_solutions.EnsureSolutionEntity(clothing, component.Solution, out solutionEnt, component.Capacity) ||
            solutionEnt is not { } created)
            return false;

        solution = created.Comp.Solution;
        return true;
    }

    /// <summary>Объём «грязи» — всего, что не является моющим средством.</summary>
    private FixedPoint2 GetWashableVolume(Solution dirt)
    {
        var total = FixedPoint2.Zero;
        foreach (var reagent in dirt.Contents)
        {
            if (!IsCleaner(reagent.Reagent))
                total += reagent.Quantity;
        }
        return total;
    }

    private bool IsCleaner(ReagentId reagent)
        => _prototype.Resolve<ReagentPrototype>(reagent.Prototype, out var prototype) &&
           GetCleanMultiplier(prototype) > 0;

    private float GetCleanMultiplier(ReagentPrototype prototype)
    {
        if (!_cleanMultipliers.TryGetValue(prototype.ID, out var multiplier))
        {
            multiplier = ComputeCleanMultiplier(prototype);
            _cleanMultipliers[prototype.ID] = multiplier;
        }
        return multiplier;
    }

    private static float ComputeCleanMultiplier(ReagentPrototype prototype)
    {
        if (prototype.ReactiveEffects == null)
            return 0f;
        foreach (var entry in prototype.ReactiveEffects.Values)
        {
            if (!entry.Methods.Contains(ReactionMethod.Touch))
                continue;
            foreach (var effect in entry.Effects)
            {
                if (effect is CleanDirt clean)
                    return clean.Multiplier;
            }
        }
        return 0f;
    }

    /// <summary>
    /// Шаги квантования цвета грязи. Кровь капает на одежду каждый тик кровотечения, и без округления
    /// цвет менялся бы на тысячные доли каждый раз: сетевое состояние предмета, пересборка слоёв
    /// одежды и новый шейдер у каждого игрока рядом — ради разницы, которую глаз не видит.
    /// С шагом 1/20 по покрытию и 1/32 по каналам цвет обновляется только заметными ступенями.
    /// </summary>
    private const float DirtAlphaSteps = 20f;
    private const float DirtColorSteps = 32f;

    private static float Quantize(float value, float steps)
        => MathF.Round(value * steps) / steps;

    private void Refresh(Entity<ClothingDirtableComponent> ent, Solution dirt)
    {
        if (_net.IsServer)
        {
            var dryable = false;
            foreach (var reagent in dirt.Contents)
            {
                if (reagent.Quantity <= 0 ||
                    !_prototype.Resolve<ReagentPrototype>(reagent.Reagent.Prototype, out var prototype) ||
                    prototype.EvaporationSpeed <= 0)
                    continue;
                dryable = true;
                break;
            }

            if (dryable) _drying.Add(ent.Owner);
            else _drying.Remove(ent.Owner);
        }

        var visibleVolume = GetWashableVolume(dirt);
        Color? color = null;
        var visualCapacity = FixedPoint2.Min(ent.Comp.Capacity, ent.Comp.MaxReagentAmount);
        if (visibleVolume > 0 && visualCapacity > 0)
        {
            var alpha = Math.Clamp(Quantize(visibleVolume.Float() / visualCapacity.Float(), DirtAlphaSteps),
                ent.Comp.MinVisualCoverage, 1f);

            // Раствор под цвет собираем только когда грязь реально видна.
            var visibleDirt = new Solution();
            foreach (var reagent in dirt.Contents)
            {
                if (!IsCleaner(reagent.Reagent))
                    visibleDirt.AddReagent(reagent.Reagent, reagent.Quantity);
            }
            var mixed = visibleDirt.GetColor(_prototype);
            color = new Color(
                Quantize(mixed.R, DirtColorSteps),
                Quantize(mixed.G, DirtColorSteps),
                Quantize(mixed.B, DirtColorSteps),
                alpha);
        }
        if (ent.Comp.DirtColor == color)
            return;
        ent.Comp.DirtColor = color;
        Dirty(ent);
        _item.VisualsChanged(ent.Owner);
    }
}

/// <summary>Как грязь попадает на тело — определяет, какие зоны (и чья одежда) пачкаются.</summary>
[Flags]
public enum DirtExposure : byte
{
    None = 0,
    /// <summary>Брызги: облили, забрызгали, окатили из ведра.</summary>
    Splash = 1 << 0,
    /// <summary>Шаг по луже — только ступни.</summary>
    Ground = 1 << 1,
    /// <summary>Ползком по луже — всё тело.</summary>
    Crawl = 1 << 2,
    /// <summary>Руки (мытьё рук).</summary>
    Hands = 1 << 3,
    /// <summary>Лицо (умывание).</summary>
    Face = 1 << 4,
    FullBody = 1 << 5,
}
