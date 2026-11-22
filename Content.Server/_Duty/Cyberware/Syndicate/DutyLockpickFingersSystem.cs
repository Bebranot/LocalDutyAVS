// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Syndicate;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Database;
using Content.Shared.Administration.Logs;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Interaction;
using Content.Shared.Lock;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Duty.Cyberware.Syndicate;

/// <summary>
/// _Duty: «Пальцы-отмычки» — долгое аварийное открытие шлюза и вскрытие замков шкафчиков.
/// Шумно: звук тревоги шлюза, запись «несанкционированный доступ» в журнал доступа двери (его читают с консоли/картриджа) и админ-лог.
/// </summary>
public sealed partial class DutyLockpickFingersSystem : EntitySystem
{
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private SharedDoorSystem _door = default!;
    [Dependency] private LockSystem _lock = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyLockpickFingersComponent, DutyLockpickActionEvent>(OnAction);
        SubscribeLocalEvent<DutyLockpickFingersComponent, DutyLockpickDoAfterEvent>(OnDoAfter);
    }

    private void OnAction(Entity<DutyLockpickFingersComponent> ent, ref DutyLockpickActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        var target = args.Target;

        if (!_cyber.IsModuleOnline(ent))
        {
            Deny(ent, user, "duty-syndicate-offline");
            args.Handled = true;
            return;
        }

        float delay;
        if (HasComp<DoorComponent>(target))
        {
            delay = ent.Comp.DoorDelay;
        }
        else if (TryComp<LockComponent>(target, out var lockComp) && lockComp.Locked)
        {
            delay = ent.Comp.LockDelay;
        }
        else
        {
            Deny(ent, user, "duty-lockpick-nothing");
            args.Handled = true;
            return;
        }

        if (!_interaction.InRangeUnobstructed(user, target, ent.Comp.Range))
        {
            Deny(ent, user, "duty-lockpick-too-far");
            args.Handled = true;
            return;
        }

        var doArgs = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(delay), new DutyLockpickDoAfterEvent(), ent.Owner, target: target)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            DistanceThreshold = ent.Comp.Range + 0.5f,
            // Пальцы — часть тела: руки не нужны, но пока ты ковыряешься, ты на виду.
            NeedHand = false,
        };

        args.Handled = true;
        if (!_doAfter.TryStartDoAfter(doArgs))
            return;

        _cyber.SetModuleState(ent, DutyCyberModuleState.Active);
        _popup.PopupEntity(Loc.GetString("duty-lockpick-start"), user, user);
    }

    private void OnDoAfter(Entity<DutyLockpickFingersComponent> ent, ref DutyLockpickDoAfterEvent args)
    {
        _cyber.SetModuleState(ent, DutyCyberModuleState.Online);

        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        args.Handled = true;
        var user = args.User;

        if (!_cyber.IsModuleOnline(ent))
        {
            Deny(ent, user, "duty-syndicate-offline");
            return;
        }

        if (TryComp<DoorComponent>(target, out var door))
        {
            if (door.State == DoorState.Welded)
            {
                Deny(ent, user, "duty-lockpick-welded");
                return;
            }

            if (TryComp<DoorBoltComponent>(target, out var bolt) && bolt.BoltsDown)
                _door.SetBoltsDown((target, bolt), false, user);

            if (door.State is DoorState.Closed or DoorState.Denying)
                _door.StartOpening(target, door, user);

            _audio.PlayPvs(ent.Comp.DoorNoise, target);
            _popup.PopupEntity(Loc.GetString("duty-lockpick-door-alarm"), target, PopupType.MediumCaution);

            // Журнал доступа самой двери: тот же, что читает СБ с картриджа доступа.
            if (TryComp<AccessReaderComponent>(target, out var reader))
                _access.LogAccess((target, reader), Loc.GetString("duty-lockpick-access-log"), force: true);
        }
        else if (TryComp<LockComponent>(target, out var lockComp) && lockComp.Locked)
        {
            _lock.Unlock(target, user, lockComp);
            _audio.PlayPvs(ent.Comp.LockNoise, target);
            _popup.PopupEntity(Loc.GetString("duty-lockpick-lock-click"), target, PopupType.SmallCaution);
        }
        else
        {
            return;
        }

        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(user):user} bypassed {ToPrettyString(target):target} with lockpick fingers implant");
    }

    private void Deny(Entity<DutyLockpickFingersComponent> ent, EntityUid user, string locId)
    {
        _popup.PopupEntity(Loc.GetString(locId), user, user, PopupType.SmallCaution);
        _audio.PlayPvs(new Robust.Shared.Audio.SoundCollectionSpecifier("DutyCyberDeny"), user);
    }
}
