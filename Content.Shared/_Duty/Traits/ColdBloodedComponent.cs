// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx (Content.Shared._Onyx.Traits).

namespace Content.Shared._Duty.Traits;

/// <summary>Хладнокровие: метаболический нагрев урезан, нет ни неявного согревания, ни дрожи.</summary>
[RegisterComponent]
public sealed partial class ColdBloodedComponent : Component
{
    [DataField]
    public float MetabolismHeatMultiplier = 0.2f;
}

[ByRefEvent]
public record struct ModifyThermalRegulationEvent(
    float MetabolismHeatMultiplier = 1f,
    float ImplicitHeatingMultiplier = 1f,
    float ShiveringMultiplier = 1f);
