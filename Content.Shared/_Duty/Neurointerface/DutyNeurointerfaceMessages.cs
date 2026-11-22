// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Neurointerface;

/// <summary>
/// _Duty: полное состояние интерфейса тактического планшета нейроинтерфейса СБ.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroTabletBuiState : BoundUserInterfaceState
{
    public string StationName = string.Empty;
    public NetEntity? TargetGrid;
    public string ActiveSquadId = string.Empty;
    public NetEntity? UserEntity;
    public string UserCallsign = string.Empty;
    public bool IsUserLeader;
    public string OwnerLocation = "---";
    public string AlertCodeName = string.Empty;
    public Color AlertCodeColor = Color.LimeGreen;
    public List<DutyNeuroSquadSummary> Squads = new();
    public List<DutyNeuroOrderEntry> Orders = new();
    public List<DutyNeuroTimerEntry> Timers = new();
    public List<DutyNeuroTaskEntry> Tasks = new();
    public List<DutyNeuroCrewCandidate> CrewCandidates = new();

    /// <summary>
    /// _Duty: в активном отряде кто-то под «Червём ICE» — планшет подсвечивает кнопку перезагрузки сети.
    /// </summary>
    public bool NetworkIntrusion;

    public DutyNeuroTabletBuiState()
    {
    }

    public DutyNeuroTabletBuiState(
        string stationName,
        NetEntity? targetGrid,
        string activeSquadId,
        NetEntity? userEntity,
        string userCallsign,
        bool isUserLeader,
        string ownerLocation,
        string alertCodeName,
        Color alertCodeColor,
        List<DutyNeuroSquadSummary> squads,
        List<DutyNeuroOrderEntry> orders,
        List<DutyNeuroTimerEntry> timers,
        List<DutyNeuroTaskEntry> tasks,
        List<DutyNeuroCrewCandidate> crewCandidates)
    {
        StationName = stationName;
        TargetGrid = targetGrid;
        ActiveSquadId = activeSquadId;
        UserEntity = userEntity;
        UserCallsign = userCallsign;
        IsUserLeader = isUserLeader;
        OwnerLocation = ownerLocation;
        AlertCodeName = alertCodeName;
        AlertCodeColor = alertCodeColor;
        Squads = squads;
        Orders = orders;
        Timers = timers;
        Tasks = tasks;
        CrewCandidates = crewCandidates;
    }
}

/// <summary>
/// _Duty: запрос на создание тактического приказа на карте планшета.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroCreateOrderMessage(
    DutyNeuroOrderType orderType,
    NetCoordinates coordinates,
    string zoneName) : BoundUserInterfaceMessage
{
    public DutyNeuroOrderType OrderType { get; } = orderType;
    public NetCoordinates Coordinates { get; } = coordinates;
    public string ZoneName { get; } = zoneName;
}

/// <summary>
/// _Duty: запрос на удаление конкретного тактического приказа.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroRemoveOrderMessage(int orderId) : BoundUserInterfaceMessage
{
    public int OrderId { get; } = orderId;
}

/// <summary>
/// _Duty: запрос на очистку всех тактических приказов активного отряда.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroClearOrdersMessage : BoundUserInterfaceMessage;

/// <summary>
/// _Duty: запрос на выбор активного отряда в планшете.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroSelectSquadMessage(string squadId) : BoundUserInterfaceMessage
{
    public string SquadId { get; } = squadId;
}

/// <summary>
/// _Duty: запрос на создание нового отряда с указанным позывным.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroCreateSquadMessage(string callsign) : BoundUserInterfaceMessage
{
    public string Callsign { get; } = callsign;
}

/// <summary>
/// _Duty: запрос на переименование существующего отряда.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroRenameSquadMessage(string squadId, string newCallsign) : BoundUserInterfaceMessage
{
    public string SquadId { get; } = squadId;
    public string NewCallsign { get; } = newCallsign;
}

/// <summary>
/// _Duty: запрос на расформирование отряда.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroDisbandSquadMessage(string squadId) : BoundUserInterfaceMessage
{
    public string SquadId { get; } = squadId;
}

/// <summary>
/// _Duty: запрос на приглашение/добавление сотрудника в отряд.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroInviteMemberMessage(string squadId, NetEntity targetEntity) : BoundUserInterfaceMessage
{
    public string SquadId { get; } = squadId;
    public NetEntity TargetEntity { get; } = targetEntity;
}

/// <summary>
/// _Duty: запрос на исключение бойца из отряда.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroRemoveMemberMessage(string squadId, NetEntity targetEntity) : BoundUserInterfaceMessage
{
    public string SquadId { get; } = squadId;
    public NetEntity TargetEntity { get; } = targetEntity;
}

/// <summary>
/// _Duty: запрос на назначение командира отряда.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroSetLeaderMessage(string squadId, NetEntity targetEntity) : BoundUserInterfaceMessage
{
    public string SquadId { get; } = squadId;
    public NetEntity TargetEntity { get; } = targetEntity;
}

/// <summary>
/// _Duty: запрос на отправку тактического радиосигнала с планшета.
/// <see cref="ZoneName"/> сервер игнорирует и вычисляет зону сам (защита от подмены текста в рации);
/// поле оставлено ради совместимости с клиентом.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroSendCalloutMessage(
    DutyNeuroCalloutType calloutType,
    string? zoneName) : BoundUserInterfaceMessage
{
    public DutyNeuroCalloutType CalloutType { get; } = calloutType;
    public string? ZoneName { get; } = zoneName;
}

/// <summary>
/// _Duty: запрос на создание тактического таймера.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroCreateTimerMessage(string title, int seconds) : BoundUserInterfaceMessage
{
    public string Title { get; } = title;
    public int Seconds { get; } = seconds;
}

/// <summary>
/// _Duty: запрос на управление существующим тактическим таймером.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroTimerActionMessage(int timerId, DutyNeuroTimerAction action) : BoundUserInterfaceMessage
{
    public int TimerId { get; } = timerId;
    public DutyNeuroTimerAction Action { get; } = action;
}

/// <summary>
/// _Duty: запрос на создание новой тактической задачи отряда.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroCreateTaskMessage(
    string title,
    string description,
    string assigneeName,
    DutyNeuroTaskPriority priority) : BoundUserInterfaceMessage
{
    public string Title { get; } = title;
    public string Description { get; } = description;
    public string AssigneeName { get; } = assigneeName;
    public DutyNeuroTaskPriority Priority { get; } = priority;
}

/// <summary>
/// _Duty: запрос на переключение статуса выполнения задачи.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroToggleTaskMessage(int taskId) : BoundUserInterfaceMessage
{
    public int TaskId { get; } = taskId;
}

/// <summary>
/// _Duty: запрос на удаление тактической задачи.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroDeleteTaskMessage(int taskId) : BoundUserInterfaceMessage
{
    public int TaskId { get; } = taskId;
}

/// <summary>
/// _Duty: перезагрузка нейросети активного отряда — снимает «Червя ICE» со всех бойцов отряда.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyNeuroRebootNetworkMessage : BoundUserInterfaceMessage;
