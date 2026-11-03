// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx.

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Loadouts;

/// <summary>Кастомный цвет предмета лоадаута, выбранный игроком при создании персонажа.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class LoadoutTintComponent : Component
{
    [DataField, AutoNetworkedField]
    public Color Color = Color.White;
}
