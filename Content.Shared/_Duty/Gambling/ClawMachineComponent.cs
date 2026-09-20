// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx (Content.Shared._Onyx.Gambling.ClawMachine).

using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Gambling;

/// <summary>Автомат с клешнёй — аркадная игра на приз.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ClawMachineComponent : Component
{
    [DataField, AutoNetworkedField]
    public float DoAfterTime = 3.9f;

    [DataField]
    public SoundSpecifier PlaySound = new SoundPathSpecifier("/Audio/Machines/Keyboard/keyboard1.ogg");

    [DataField]
    public SoundSpecifier LoseSound = new SoundPathSpecifier("/Audio/Machines/buzz-two.ogg");

    [DataField]
    public SoundSpecifier WinSound = new SoundPathSpecifier("/Audio/Effects/Arcade/win.ogg");

    [DataField, AutoNetworkedField]
    public float WinChance = .10f;

    [DataField, AutoNetworkedField]
    public bool IsSpinning;

    [DataField, AutoNetworkedField]
    public List<EntProtoId>? Rewards;

    [DataField, AutoNetworkedField]
    public List<EntProtoId>? EvilRewards;

    [DataField, AutoNetworkedField]
    public bool Emagged;
}

[Serializable, NetSerializable]
public enum ClawMachineVisuals : byte
{
    Spinning,
    NormalSprite,
}
