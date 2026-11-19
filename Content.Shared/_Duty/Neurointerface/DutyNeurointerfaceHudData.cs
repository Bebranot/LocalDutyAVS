// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Neurointerface;

/// <summary>
/// _Duty: данные о бойце отряда для отображения на HUD и в тактическом планшете.
/// </summary>
[Serializable, NetSerializable]
public sealed record DutyNeuroSquadMemberEntry(
    NetEntity Entity,
    string Name,
    string RoleTitle,
    DutyNeuroMemberStatus Status,
    string Location,
    float HealthPercent,
    bool HasTrackerImplant,
    bool IsLeader);

/// <summary>
/// _Duty: запись о цели с имплантом-трекером для отображения на HUD.
/// </summary>
[Serializable, NetSerializable]
public sealed record DutyNeuroTrackerEntry(
    NetEntity Entity,
    string Name,
    string ZoneName);

/// <summary>
/// _Duty: тактический приказ, выставленный на карту станции.
/// Приказ принадлежит отряду <see cref="SquadId"/>: носители видят только приказы своего отряда.
/// </summary>
[Serializable, NetSerializable]
public sealed record DutyNeuroOrderEntry(
    int OrderId,
    DutyNeuroOrderType OrderType,
    string ZoneName,
    NetCoordinates Coordinates,
    string AuthorName,
    TimeSpan CreatedAt,
    string SquadId = "");

/// <summary>
/// _Duty: сводка по отряду нейроинтерфейса и списку его бойцов.
/// </summary>
[Serializable, NetSerializable]
public sealed record DutyNeuroSquadSummary(
    string SquadId,
    string Callsign,
    int AliveCount,
    int TotalCount,
    string LeaderName,
    List<DutyNeuroSquadMemberEntry> Members);

/// <summary>
/// _Duty: запись тактического таймера на планшете.
/// </summary>
[Serializable, NetSerializable]
public sealed record DutyNeuroTimerEntry(
    int TimerId,
    string Title,
    TimeSpan Duration,
    TimeSpan? EndTime,
    TimeSpan? RemainingWhenPaused,
    DutyNeuroTimerStatus Status);

/// <summary>
/// _Duty: запись тактической задачи отряда на планшете.
/// </summary>
[Serializable, NetSerializable]
public sealed record DutyNeuroTaskEntry(
    int TaskId,
    string Title,
    string Description,
    string AssigneeName,
    DutyNeuroTaskPriority Priority,
    bool Completed);

/// <summary>
/// _Duty: кандидат из экипажа для приглашения в отряд на тактическом планшете.
/// </summary>
[Serializable, NetSerializable]
public sealed record DutyNeuroCrewCandidate(
    NetEntity Entity,
    string Name,
    string JobTitle,
    string DepartmentName,
    bool HasNeuroMask,
    bool HasTrackerImplant,
    string? CurrentSquadCallsign);
