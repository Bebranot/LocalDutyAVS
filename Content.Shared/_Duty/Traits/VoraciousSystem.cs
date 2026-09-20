// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.Traits;

public sealed class VoraciousSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VoraciousComponent, GetEatingDelayEvent>(OnGetEatingDelay);
    }

    private void OnGetEatingDelay(Entity<VoraciousComponent> ent, ref GetEatingDelayEvent args)
    {
        args.Delay *= ent.Comp.EatingDelayMultiplier;
    }
}
