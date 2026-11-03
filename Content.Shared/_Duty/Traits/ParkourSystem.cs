// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Climbing.Events;

namespace Content.Shared._Duty.Traits;

public sealed class ParkourSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ParkourComponent, CheckClimbSpeedModifiersEvent>(OnCheckClimbSpeed);
    }

    private void OnCheckClimbSpeed(Entity<ParkourComponent> ent, ref CheckClimbSpeedModifiersEvent args)
    {
        args.Time *= ent.Comp.ClimbDelayMultiplier;
    }
}
