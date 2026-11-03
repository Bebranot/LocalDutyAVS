// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx.

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Traits;

/// <summary>Непереносимость алкоголя: каждая секунда опьянения тикает Poison-уроном.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AlcoholIntoleranceComponent : Component
{
    [DataField]
    public float PoisonPerSecondOfDrunkenness = 0.08f;
}
