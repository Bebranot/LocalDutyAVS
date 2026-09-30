// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx.

using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Clothing;

/// <summary>
/// _Duty: у раковины можно застирать вещь (ударить ей по раковине) и помыть руки / умыться
/// (альт-действия). Вода берётся из сливаемого раствора самой раковины.
/// </summary>
[RegisterComponent]
public sealed partial class ClothingDirtWasherComponent : Component
{
    [DataField]
    public string CleanerReagent = "Water";

    /// <summary>Сколько воды уходит на одну стирку / умывание.</summary>
    [DataField]
    public FixedPoint2 Amount = FixedPoint2.New(1);

    [DataField]
    public TimeSpan WashTime = TimeSpan.FromSeconds(2);
}

[Serializable, NetSerializable]
public sealed partial class WashClothingDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class WashBodyDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public BodyWashTarget WashTarget;

    public WashBodyDoAfterEvent(BodyWashTarget target)
    {
        WashTarget = target;
    }
}

[Serializable, NetSerializable]
public enum BodyWashTarget : byte
{
    Hands,
    Face,
}
