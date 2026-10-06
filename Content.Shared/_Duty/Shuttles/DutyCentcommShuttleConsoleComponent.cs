// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Shuttles;

/// <summary>
/// _Duty: консоль шаттла ЦК — во вкладке БСС появляется кнопка прыжка «на ЦК» / «на станцию».
///
/// Прыжок тот же, что у админ-команд dutyftlcentcomm/dutyftlstation: случайная свободная точка рядом
/// с целью, без диска координат. Кнопка нужна, чтобы шаттл ЦК летал сам, без админа.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DutyCentcommShuttleConsoleComponent : Component
{
    /// <summary>
    /// Шаттл консоли сейчас на карте ЦК — кнопка ведёт на станцию, иначе на ЦК.
    /// Считает сервер: клиент не знает, какая карта — ЦК.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool AtCentcomm;

    /// <summary>
    /// Секунд в гиперпространстве; null — как у обычного FTL.
    /// </summary>
    [DataField]
    public float? TravelTime;
}

[Serializable, NetSerializable]
public sealed class DutyCentcommShuttleJumpMessage : BoundUserInterfaceMessage;
