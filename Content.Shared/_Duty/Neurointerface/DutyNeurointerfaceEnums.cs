// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Neurointerface;

/// <summary>
/// _Duty: ключи пользовательского интерфейса системы NT-SEC Neurointerface.
/// </summary>
[Serializable, NetSerializable]
public enum DutyNeuroUiKey : byte
{
    /// <summary>
    /// Тактический планшет нейроинтерфейса СБ.
    /// </summary>
    Tablet
}

/// <summary>
/// _Duty: типы тактических приказов, отображаемых на HUD и карте планшета.
/// </summary>
[Serializable, NetSerializable]
public enum DutyNeuroOrderType : byte
{
    Move,
    Assault,
    Hold,
    Retreat,
    Sos,
    Recon
}

/// <summary>
/// _Duty: состояние бойца в отряде нейроинтерфейса.
/// </summary>
[Serializable, NetSerializable]
public enum DutyNeuroMemberStatus : byte
{
    Alive,
    Critical,
    Dead,
    Offline
}

/// <summary>
/// _Duty: состояние тактического таймера на планшете.
/// </summary>
[Serializable, NetSerializable]
public enum DutyNeuroTimerStatus : byte
{
    Stopped,
    Running,
    Paused
}

/// <summary>
/// _Duty: действия управления тактическим таймером.
/// </summary>
[Serializable, NetSerializable]
public enum DutyNeuroTimerAction : byte
{
    Start,
    Pause,
    Reset,
    Delete
}

/// <summary>
/// _Duty: приоритет тактической задачи отряда.
/// </summary>
[Serializable, NetSerializable]
public enum DutyNeuroTaskPriority : byte
{
    Normal,
    High,
    Critical
}

/// <summary>
/// _Duty: типы быстрых радиосигналов (callouts), передаваемых через нейроинтерфейс.
/// </summary>
[Serializable, NetSerializable]
public enum DutyNeuroCalloutType : byte
{
    Acknowledge,
    Retreat,
    Sos,
    Medic
}

/// <summary>
/// _Duty: чья это нейросеть. Отряды, приказы и трекеры не пересекаются между сетями:
/// оперативники Горлекса видят только своих, NT-SEC — только своих. Сеть красит HUD и выбирает радиоканал.
/// </summary>
[Serializable, NetSerializable]
public enum DutyNeuroNetwork : byte
{
    NtSec,
    Gorlex
}
