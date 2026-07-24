// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.Events;
using Content.Shared._Duty.Trauma.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Random;

namespace Content.Server._Duty.Trauma.Systems;

/// <summary>
/// _Duty: вправление вывиха голыми руками. Сам себе — дольше и ненадёжнее (10с, 30%), другой
/// игрок — быстрее и надёжнее (5с, 70%). Провал не усугубляет травму: просто боль и потерянное
/// время. Успех снимает вывих, зона переходит в короткую остаточную слабость.
/// </summary>
public sealed class DislocationTreatmentSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly DislocationSystem _dislocation = default!;

    // ── Тюнинг (Phase 6). ──────────────────────────────────────────────────────
    private static readonly TimeSpan SelfReduceTime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan OtherReduceTime = TimeSpan.FromSeconds(5);
    private const float SelfReduceChance = 0.3f;
    private const float OtherReduceChance = 0.7f;

    public override void Initialize()
    {
        SubscribeLocalEvent<DislocationComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<DislocationComponent, DislocationReduceDoAfterEvent>(OnReduceDoAfter);
    }

    private void OnGetVerbs(Entity<DislocationComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var patient = ent.Owner;
        if (!_dislocation.HasDislocation(patient))
            return;

        var user = args.User;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("trauma-verb-reduce-dislocation"),
            Act = () => StartReduce(patient, user),
        });
    }

    private void StartReduce(EntityUid patient, EntityUid user)
    {
        var isSelf = user == patient;
        var delay = isSelf ? SelfReduceTime : OtherReduceTime;
        var chance = isSelf ? SelfReduceChance : OtherReduceChance;

        var doAfter = new DoAfterArgs(EntityManager, user, delay, new DislocationReduceDoAfterEvent(chance), patient, patient)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupEntity(Loc.GetString("trauma-reduce-start"), patient, patient);
    }

    private void OnReduceDoAfter(Entity<DislocationComponent> ent, ref DislocationReduceDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        var patient = ent.Owner;

        if (_random.Prob(args.SuccessChance) && _dislocation.TryReduceFirst(patient))
            _popup.PopupEntity(Loc.GetString("trauma-reduce-success"), patient, patient);
        else
            _popup.PopupEntity(Loc.GetString("trauma-reduce-fail"), patient, PopupType.MediumCaution);
    }
}
