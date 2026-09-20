// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx.

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Traits;

/// <summary>Устойчивость к алкоголю: опьянение держится вдвое меньше.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AlcoholToleranceComponent : Component
{
    [DataField]
    public float DrunkDurationMultiplier = 0.5f;
}
