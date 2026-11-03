// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx.

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Traits;

/// <summary>Прожорливость: ест/пьёт быстрее (не действует на принудительном кормлении).</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VoraciousComponent : Component
{
    [DataField]
    public float EatingDelayMultiplier = 0.5f;
}

[ByRefEvent]
public record struct GetEatingDelayEvent(TimeSpan Delay);
