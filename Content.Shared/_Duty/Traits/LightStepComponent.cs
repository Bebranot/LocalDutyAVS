// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx.

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Traits;

/// <summary>Лёгкая поступь: звук шагов тише.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class LightStepComponent : Component
{
    [DataField]
    public float FootstepVolumeModifier = -8f;
}

[ByRefEvent]
public record struct ModifyFootstepVolumeEvent(float Modifier = 0f);
