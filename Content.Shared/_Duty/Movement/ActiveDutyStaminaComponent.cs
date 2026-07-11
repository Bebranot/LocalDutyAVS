// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Movement;

/// <summary>
/// _Duty: маркер «выносливость сейчас меняется» (тратится при беге или восстанавливается).
/// Только такие сущности обрабатываются в <see cref="DutySprintSystem.Update"/>; снимается,
/// когда запас снова полон и игрок не бежит.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ActiveDutyStaminaComponent : Component;
