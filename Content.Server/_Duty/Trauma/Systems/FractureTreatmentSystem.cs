// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.Events;
using Content.Shared._Duty.Trauma.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Random;

namespace Content.Server._Duty.Trauma.Systems;

/// <summary>
/// _Duty: шинирование переломов. Только другим игроком: DoAfter 10с, шанс успеха 25%. Успех
/// стабилизирует самую тяжёлую незашинированную зону (ускоренное сращивание, меньше штраф);
/// провал — боль, крик и урон пациенту. Полное сращение всё равно идёт временем (см. FractureSystem).
/// </summary>
public sealed class FractureTreatmentSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly FractureSystem _fracture = default!;

    // ── Тюнинг (Phase 6). ──────────────────────────────────────────────────────
    private static readonly TimeSpan SplintTime = TimeSpan.FromSeconds(10);
    private const float SplintSuccessChance = 0.25f;
    private const float SplintFailDamage = 10f;

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
            Act = () => StartSplint(patient, user),
        });
    }

    private void StartSplint(EntityUid patient, EntityUid user)
    {
        var doAfter = new DoAfterArgs(EntityManager, user, SplintTime, new FractureSplintDoAfterEvent(), patient, patient)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
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

        if (_random.Prob(SplintSuccessChance) && _fracture.TrySplintWorstZone(patient))
        {
            _popup.PopupEntity(Loc.GetString("trauma-splint-success"), patient, patient);
            return;
        }

        // Провал: боль, крик и урон.
        var dmg = new DamageSpecifier();
        dmg.DamageDict.Add("Blunt", SplintFailDamage);
        _damageable.TryChangeDamage(patient, dmg, ignoreResistances: true);

        _popup.PopupEntity(Loc.GetString("trauma-splint-fail-scream"), patient, PopupType.LargeCaution);
    }
}
