// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx.

using Content.Shared.Eye.Blinding.Systems;

namespace Content.Shared._Duty.Mech;

/// <summary>Пилот сидит в мехе без энергии — камеры/дисплей не работают, пилот слеп.</summary>
[RegisterComponent]
public sealed partial class MechPowerBlindnessComponent : Component;

public sealed partial class MechPowerBlindnessSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MechPowerBlindnessComponent, CanSeeAttemptEvent>(OnCanSee);
    }

    private void OnCanSee(Entity<MechPowerBlindnessComponent> ent, ref CanSeeAttemptEvent args)
    {
        args.Cancel();
    }
}
