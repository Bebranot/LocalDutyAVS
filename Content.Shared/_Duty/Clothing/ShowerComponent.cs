// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx.

using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Clothing;

/// <summary>Душ — периодически отмывает одежду в радиусе, пока включён.</summary>
[RegisterComponent]
public sealed partial class ShowerComponent : Component
{
    [DataField]
    public bool Enabled;

    [DataField]
    public string CleanerReagent = "Water";

    [DataField]
    public FixedPoint2 WashAmount = FixedPoint2.New(1);

    [DataField]
    public FixedPoint2 PuddleAmount = FixedPoint2.New(10);

    [DataField]
    public float WashInterval = 1f;

    [DataField]
    public float WashRange = 1.25f;

    [DataField]
    public SlotFlags TargetSlots = SlotFlags.WITHOUT_POCKET;

    public float WashAccumulator;
}

[Serializable, NetSerializable]
public enum ShowerVisuals : byte
{
    Enabled,
}

[Serializable, NetSerializable]
public enum ShowerVisualLayers : byte
{
    Water,
}
