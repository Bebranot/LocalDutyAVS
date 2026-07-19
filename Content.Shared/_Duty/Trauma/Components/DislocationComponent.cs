// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Trauma.Components;

/// <summary>
/// _Duty: вывихи существа по зонам. Бинарное состояние (в отличие от тиров перелома): зона либо
/// вывихнута (почти неработоспособна до вправления), либо нет. После успешного вправления зона
/// какое-то время в «остаточной слабости» — лёгкий дебафф. Сетевой (осмотр + предсказанные
/// эффекты движения/атаки).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DislocationComponent : Component
{
    /// <summary>Активные вывихи — зона почти не работает до вправления.</summary>
    [AutoNetworkedField]
    public HashSet<BodyZone> Dislocated = new();

    /// <summary>Зоны в остаточной слабости после вправления → время окончания.</summary>
    [AutoNetworkedField]
    public Dictionary<BodyZone, TimeSpan> Residual = new();
}
