// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.Traits;

public sealed class LightStepSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LightStepComponent, ModifyFootstepVolumeEvent>(OnModifyFootstepVolume);
    }

    private void OnModifyFootstepVolume(Entity<LightStepComponent> ent, ref ModifyFootstepVolumeEvent args)
    {
        args.Modifier += ent.Comp.FootstepVolumeModifier;
    }
}
