// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Traits;

namespace Content.Server._Duty.Traits;

public sealed class ColdBloodedSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ColdBloodedComponent, ModifyThermalRegulationEvent>(OnModifyThermalRegulation);
    }

    private void OnModifyThermalRegulation(Entity<ColdBloodedComponent> ent, ref ModifyThermalRegulationEvent args)
    {
        args.MetabolismHeatMultiplier *= ent.Comp.MetabolismHeatMultiplier;
        args.ImplicitHeatingMultiplier = 0f;
        args.ShiveringMultiplier = 0f;
    }
}
