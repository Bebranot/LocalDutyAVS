// SPDX-FileCopyrightText: 2026 LocalDuty
// SPDX-License-Identifier: MIT

using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.SpawnMenu;

/// <summary>
/// Клиент просит сервер прислать актуальный список доступных ему предметов.
/// Сервер молча игнорирует запрос, если у сикея нет ни одной группы доступа.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutySpawnMenuRequestEvent : EntityEventArgs;

/// <summary>
/// Ответ сервера: что игроку можно спавнить и сколько осталось.
/// Присылается и в ответ на запрос, и после каждой удачной выдачи.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutySpawnMenuStateEvent(List<DutySpawnMenuEntry> entries, float range) : EntityEventArgs
{
    public List<DutySpawnMenuEntry> Entries { get; } = entries;

    /// <summary>Максимальная дистанция выдачи в клетках — клиент рисует по ней радиус.</summary>
    public float Range { get; } = range;
}

/// <summary>
/// Строка меню в том виде, в каком её видит клиент.
/// </summary>
[Serializable, NetSerializable]
public struct DutySpawnMenuEntry
{
    public string Proto;
    public string Name;

    /// <summary>Сколько ещё можно выдать. -1 — без ограничения.</summary>
    public int Remaining;
}

/// <summary>
/// Клиент кликнул по точке в мире, выбрав предмет в меню. Всё остальное — проверки доступа,
/// лимита и дистанции — делает сервер, клиенту здесь не верим ни в чём.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutySpawnMenuSpawnEvent(string proto, NetCoordinates coordinates) : EntityEventArgs
{
    public string Proto { get; } = proto;
    public NetCoordinates Coordinates { get; } = coordinates;
}
