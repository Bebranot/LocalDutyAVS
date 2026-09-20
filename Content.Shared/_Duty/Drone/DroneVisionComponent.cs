// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Drone;

/// <summary>_Duty: тепловизор дрона — рендерит живых мобов как чёрные силуэты (см. клиентский
/// DroneVisionOverlay). Оригинал у Onyx фильтровал по BodyComponent.ThermalVisibility — своего
/// поля у нашего (ванильного) BodyComponent нет, поэтому вместо него берём MobStateComponent.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DroneVisionComponent : Component;
