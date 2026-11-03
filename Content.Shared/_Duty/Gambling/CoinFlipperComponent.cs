// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx (Content.Shared._Onyx.Gambling.CoinFlipper).

using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Gambling;

/// <summary>Автомат «удвой или проиграй» — кладёшь пачку денег, монетка решает.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CoinFlipperComponent : Component
{
    [DataField]
    public SoundSpecifier SpinSound = new SoundPathSpecifier("/Audio/ADT/Machines/SlotMachine/slotmachine_spin.ogg");

    [DataField]
    public SoundSpecifier LoseSound = new SoundPathSpecifier("/Audio/Machines/buzz-two.ogg");

    [DataField, AutoNetworkedField]
    public float DoAfterTime = 3.8f;

    [DataField, AutoNetworkedField]
    public bool IsSpinning;

    [DataField, AutoNetworkedField]
    public int PrizeAmount;

    [DataField]
    public SoundSpecifier WinSound = new SoundPathSpecifier("/Audio/Effects/Arcade/win.ogg");
}
