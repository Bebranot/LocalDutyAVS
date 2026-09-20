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
using System.Linq;

namespace Content.Shared._Duty.Clothing;

public sealed partial class ClothingDirtSystem : EntitySystem
{
    public const string DefaultSolutionName = "dirt";

    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    private readonly HashSet<EntityUid> _drying = new();
    private readonly List<EntityUid> _dryingBuffer = new();
    private float _dryUpdateAccumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClothingDirtableComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ClothingDirtableComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ClothingDirtableComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ClothingDirtableComponent, SolutionChangedEvent>(OnSolutionChanged);
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

    private void OnSolutionChanged(Entity<ClothingDirtableComponent> ent, ref SolutionChangedEvent args)
    {
        if (!_net.IsServer || args.Solution.Comp.Solution.Name != ent.Comp.Solution)
            return;
        Refresh(ent, args.Solution.Comp.Solution);
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
            !_solutions.TryGetSolution(clothing, component.Solution, out var solutionEnt, out var dirt))
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

        if (sample.Volume <= 0 || !_solutions.TryAddSolution(solutionEnt.Value, sample))
            return false;
        if (ProcessCleaners(dirt))
            _solutions.UpdateChemicals(solutionEnt.Value);
        Refresh((clothing, component), dirt);
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
        Refresh((clothing, component), dirt);
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

        var washable = dirt.Contents
            .Where(x => !IsCleaner(x.Reagent))
            .Aggregate(FixedPoint2.Zero, (total, x) => total + x.Quantity);
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
        Refresh((clothing, component), dirt);
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
        Refresh((dirtable, component), dirt);
        return true;
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

        if (changed)
            _solutions.UpdateChemicals(solutionEnt.Value);
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

            var washable = dirt.Contents
                .Where(x => !IsCleaner(x.Reagent))
                .Aggregate(FixedPoint2.Zero, (total, reagent) => total + reagent.Quantity);
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
        var washable = dirt.Contents
            .Where(x => !IsCleaner(x.Reagent))
            .Aggregate(FixedPoint2.Zero, (total, reagent) => total + reagent.Quantity);
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

    private bool IsCleaner(ReagentId reagent)
        => _prototype.Resolve<ReagentPrototype>(reagent.Prototype, out var prototype) &&
           GetCleanMultiplier(prototype) > 0;

    private static float GetCleanMultiplier(ReagentPrototype prototype)
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

    private void Refresh(Entity<ClothingDirtableComponent> ent, Solution dirt)
    {
        var dryable = dirt.Contents.Any(x =>
            _prototype.Resolve<ReagentPrototype>(x.Reagent.Prototype, out var prototype) &&
            prototype.EvaporationSpeed > 0 && x.Quantity > 0);
        if (_net.IsServer)
        {
            if (dryable) _drying.Add(ent.Owner);
            else _drying.Remove(ent.Owner);
        }

        var visibleDirt = dirt.Contents.Where(x => !IsCleaner(x.Reagent)).ToArray();
        var visibleVolume = visibleDirt.Aggregate(FixedPoint2.Zero, (total, reagent) => total + reagent.Quantity);
        Color? color = null;
        var visualCapacity = FixedPoint2.Min(ent.Comp.Capacity, ent.Comp.MaxReagentAmount);
        if (visibleVolume > 0 && visualCapacity > 0)
        {
            var alpha = Math.Clamp(visibleVolume.Float() / visualCapacity.Float(),
                ent.Comp.MinVisualCoverage, 1f);
            color = new Solution(visibleDirt).GetColor(_prototype).WithAlpha(alpha);
        }
        if (ent.Comp.DirtColor == color)
            return;
        ent.Comp.DirtColor = color;
        Dirty(ent);
        _item.VisualsChanged(ent.Owner);
    }
}
