// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Drunk;

namespace Content.Shared._Duty.Traits;

public sealed class AlcoholToleranceSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AlcoholToleranceComponent, SharedDrunkSystem.DrunkEvent>(OnDrunk);
    }

    private void OnDrunk(Entity<AlcoholToleranceComponent> ent, ref SharedDrunkSystem.DrunkEvent args)
    {
        args.Duration *= ent.Comp.DrunkDurationMultiplier;
    }
}
