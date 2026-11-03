// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx.

using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Clothing;

/// <summary>Стиральная машина — закинутые внутрь EntityStorage предметы отмываются по таймеру.</summary>
[RegisterComponent]
public sealed partial class WashingMachineComponent : Component
{
    public const string ContainerId = "entity_storage";

    [DataField]
    public float WashTime = 20f;

    [DataField]
    public string CleanerReagent = "Water";

    [DataField]
    public FixedPoint2 WashAmount = FixedPoint2.New(100);

    [DataField]
    public SoundSpecifier FinishSound = new SoundPathSpecifier("/Audio/Machines/ding.ogg");

    public float RemainingTime;

    public bool IsWashing;
}

[Serializable, NetSerializable]
public enum WashingMachineVisuals : byte
{
    Washing,
}

[Serializable, NetSerializable]
public enum WashingMachineVisualLayers : byte
{
    Washing,
}
