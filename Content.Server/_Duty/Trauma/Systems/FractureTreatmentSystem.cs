// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma;
using Content.Shared._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.Events;
using Content.Shared._Duty.Trauma.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Duty.Trauma.Systems;

/// <summary>
/// _Duty: шинирование переломов. Только другим игроком: DoAfter 10с, шанс успеха 25%. Требует
/// материал (готовая шина, либо 2 ткани + 1 доска в руках лечащего) — расходуется по завершении
/// DoAfter независимо от исхода ролла (как и провал шинирования — материал уже был использован).
/// Успех стабилизирует самую тяжёлую незашинированную зону (ускоренное сращивание, меньше штраф);
/// провал — боль, крик и урон пациенту. Полное сращение всё равно идёт временем (см. FractureSystem).
/// </summary>
public sealed class FractureTreatmentSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly FractureSystem _fracture = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly TagSystem _tag = default!;

    // ── Тюнинг (Phase 6). ──────────────────────────────────────────────────────
    private static readonly TimeSpan SplintTime = TimeSpan.FromSeconds(10);
    private const float SplintSuccessChance = 0.25f;
    private const float SplintFailDamage = 10f;

    /// <summary>Сколько ткани нужно для шинирования (если нет готовой шины).</summary>
    private const int RequiredClothCount = 2;

    /// <summary>Сколько досок нужно для шинирования (если нет готовой шины).</summary>
    private const int RequiredWoodCount = 1;

    /// <summary>Тег готовой шины (расходуется целиком за одно применение).</summary>
    private static readonly ProtoId<TagPrototype> SplintTag = "Splint";

    private static readonly ProtoId<StackPrototype> ClothStack = "Cloth";
    private static readonly ProtoId<StackPrototype> WoodStack = "WoodPlank";

    public override void Initialize()
    {
        SubscribeLocalEvent<FractureComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<FractureComponent, FractureSplintDoAfterEvent>(OnSplintDoAfter);
    }

    private void OnGetVerbs(Entity<FractureComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var patient = ent.Owner;
        var user = args.User;

        // Только другим игроком, и только пока есть что шинировать.
        if (user == patient || !_fracture.HasUnsplintedFracture(patient))
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("trauma-verb-splint"),
            Priority = TraumaLoc.TreatmentVerbPriority,
            Act = () => StartSplint(patient, user),
        });
    }

    private void StartSplint(EntityUid patient, EntityUid user)
    {
        if (!TryFindSplintMaterial(user, out _))
        {
            _popup.PopupEntity(Loc.GetString("trauma-splint-need-material"), user, user);
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, user, SplintTime, new FractureSplintDoAfterEvent(), patient, patient)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupEntity(Loc.GetString("trauma-splint-start"), patient, user);
    }

    private void OnSplintDoAfter(Entity<FractureComponent> ent, ref FractureSplintDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        var patient = ent.Owner;

        // Материал заново ищем в руках лечащего на момент завершения (DoAfter уже защитил от
        // перемещения/урона) и тратим его независимо от исхода ролла ниже.
        if (!TryFindSplintMaterial(args.User, out var material))
        {
            _popup.PopupEntity(Loc.GetString("trauma-splint-need-material"), patient, args.User);
            return;
        }

        ConsumeSplintMaterial(material);

        if (_random.Prob(SplintSuccessChance) && _fracture.TrySplintWorstZone(patient))
        {
            _popup.PopupEntity(Loc.GetString("trauma-splint-success"), patient, patient);
            return;
        }

        // Провал: боль, крик и урон. origin = пациент, чтобы этот урон сам не вызвал новую травму.
        var dmg = new DamageSpecifier();
        dmg.DamageDict.Add("Blunt", SplintFailDamage);
        _damageable.TryChangeDamage(patient, dmg, ignoreResistances: true, origin: patient);

        _popup.PopupEntity(Loc.GetString("trauma-splint-fail-scream"), patient, PopupType.LargeCaution);
    }

    /// <summary>Материал шинирования: либо готовая шина, либо пара стаков (ткань + доска).</summary>
    private readonly record struct SplintMaterial(EntityUid? SplintItem, EntityUid? Cloth, EntityUid? Wood);

    /// <summary>
    /// Ищет материал во ВСЕХ занятых руках лечащего (не только активной — два разных стака не
    /// влезут в одну руку): готовая шина (тег <see cref="SplintTag"/>) — приоритетно, иначе пара
    /// стаков «ткань ≥2» + «доска ≥1».
    /// </summary>
    private bool TryFindSplintMaterial(EntityUid user, out SplintMaterial material)
    {
        material = default;

        EntityUid? cloth = null;
        EntityUid? wood = null;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (_tag.HasTag(held, SplintTag))
            {
                material = new SplintMaterial(held, null, null);
                return true;
            }

            if (!TryComp<StackComponent>(held, out var stack))
                continue;

            if (cloth is null && stack.StackTypeId == ClothStack && _stack.GetCount((held, stack)) >= RequiredClothCount)
                cloth = held;
            else if (wood is null && stack.StackTypeId == WoodStack && _stack.GetCount((held, stack)) >= RequiredWoodCount)
                wood = held;
        }

        if (cloth is null || wood is null)
            return false;

        material = new SplintMaterial(null, cloth, wood);
        return true;
    }

    private void ConsumeSplintMaterial(SplintMaterial material)
    {
        if (material.SplintItem is { } splint && !Deleted(splint))
        {
            QueueDel(splint);
            return;
        }

        if (material.Cloth is { } cloth && !Deleted(cloth) && TryComp<StackComponent>(cloth, out var clothStack))
            _stack.SetCount(cloth, _stack.GetCount((cloth, clothStack)) - RequiredClothCount, clothStack);

        if (material.Wood is { } wood && !Deleted(wood) && TryComp<StackComponent>(wood, out var woodStack))
            _stack.SetCount(wood, _stack.GetCount((wood, woodStack)) - RequiredWoodCount, woodStack);
    }
}
