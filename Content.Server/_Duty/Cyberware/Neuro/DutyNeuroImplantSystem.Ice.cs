// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Cyberware.Neuro;
using Content.Shared._Duty.Neurointerface;
using Content.Shared.Database;
using Content.Shared.Popups;

namespace Content.Server._Duty.Cyberware.Neuro;

public sealed partial class DutyNeuroImplantSystem
{
    private void InitializeIce()
    {
        SubscribeLocalEvent<DutyIceWormImplantComponent, DutyIceWormActionEvent>(OnIceWorm);
    }

    private void OnIceWorm(Entity<DutyIceWormImplantComponent> ent, ref DutyIceWormActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        var target = args.Target;
        if (!CheckReady(ent.Owner, user))
            return;

        // Дальность проверяет и сам экшен (range в прототипе), но экшен не знает про грид/карту — перестрахуемся.
        if (!_transform.InRange(user, target, ent.Comp.Range))
        {
            Deny(user, "duty-ice-worm-out-of-range");
            return;
        }

        if (!TryComp<DutyNeurointerfaceWearerComponent>(target, out var wearer) || wearer.Network != DutyNeuroNetwork.NtSec)
        {
            Deny(user, "duty-ice-worm-no-target");
            return;
        }

        if (!_neuro.TryStartIntrusion(target, ent.Comp.Duration))
        {
            Deny(user, "duty-ice-worm-no-target");
            return;
        }

        _popup.PopupEntity(Loc.GetString("duty-ice-worm-success", ("target", _neuro.GetDisplayName(target))), user, user, PopupType.Medium);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(user):user} launched ICE worm into neurointerface of {ToPrettyString(target):target}");

        StartCooldown(ent.Owner, args.Action, ent.Comp.Cooldown);
        args.Handled = true;
    }
}
