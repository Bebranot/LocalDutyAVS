// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx. Отличие: у нас уже есть готовый хук на скорость залезания
// (ADT Quirks — CheckClimbSpeedModifiersEvent), используем его вместо их отдельного
// GetClimbDelayEvent.

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Traits;

/// <summary>Паркур: быстрее залезает на столы/перила и подобное.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ParkourComponent : Component
{
    [DataField]
    public float ClimbDelayMultiplier = 0.5f;
}
