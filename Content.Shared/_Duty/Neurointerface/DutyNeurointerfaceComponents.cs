// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Neurointerface;

/// <summary>
/// _Duty: маркер-компонент тактической маски с нейроинтерфейсом NT-SEC.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DutyNeurointerfaceMaskComponent : Component
{
    /// <summary>
    /// Позывной отряда по умолчанию, к которому привязывается носитель маски.
    /// </summary>
    [DataField]
    public string DefaultSquadCallsign = "KILO-9";
}

/// <summary>
/// _Duty: компонент тактического планшета нейроинтерфейса СБ.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class DutyNeurointerfaceTabletComponent : Component
{
    /// <summary>
    /// Грид станции, для которого планшет строит навигационную карту и координаты приказов.
    /// </summary>
    [AutoNetworkedField]
    public NetEntity? TargetGrid;

    /// <summary>
    /// Отображаемое название станции.
    /// </summary>
    [AutoNetworkedField]
    public string StationName = string.Empty;

    /// <summary>
    /// Идентификатор активного отряда, выбранного на планшете.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string ActiveSquadId = "KILO-9";
}

/// <summary>
/// _Duty: компонент состояния нейроинтерфейса на носителе маски.
/// Остаётся на теле после смерти до снятия маски для отображения экрана flatline.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class DutyNeurointerfaceWearerComponent : Component
{
    /// <summary>
    /// Активен ли HUD нейроинтерфейса (маска надета на лицо).
    /// </summary>
    [AutoNetworkedField]
    public bool Active;

    /// <summary>
    /// Позывной текущего отряда носителя.
    /// </summary>
    [AutoNetworkedField]
    public string SquadCallsign = string.Empty;

    /// <summary>
    /// Название текущей зоны / маяка станции, где находится носитель.
    /// </summary>
    [AutoNetworkedField]
    public string LocationName = "---";

    /// <summary>
    /// Локализованная строка состояния носителя для HUD.
    /// </summary>
    [AutoNetworkedField]
    public string StatusText = string.Empty;

    /// <summary>
    /// Текущий статус жизненного цикла носителя.
    /// </summary>
    [AutoNetworkedField]
    public DutyNeuroMemberStatus OwnerStatus = DutyNeuroMemberStatus.Alive;

    /// <summary>
    /// Название текущего кода тревоги на станции.
    /// </summary>
    [AutoNetworkedField]
    public string AlertCodeName = string.Empty;

    /// <summary>
    /// Цвет текущего кода тревоги на станции.
    /// </summary>
    [AutoNetworkedField]
    public Color AlertCodeColor = Color.LimeGreen;

    /// <summary>
    /// Время раунда, в которое носитель погиб (используется для экрана flatline).
    /// </summary>
    [AutoNetworkedField]
    public TimeSpan? DeathRoundTime;

    /// <summary>
    /// Актуальный список бойцов отряда для HUD.
    /// </summary>
    [AutoNetworkedField]
    public List<DutyNeuroSquadMemberEntry> SquadMembers = new();

    /// <summary>
    /// Актуальный список имплантов-трекеров для HUD.
    /// </summary>
    [AutoNetworkedField]
    public List<DutyNeuroTrackerEntry> Trackers = new();

    /// <summary>
    /// Актуальный список активных тактических приказов для HUD и world-space маркеров.
    /// </summary>
    [AutoNetworkedField]
    public List<DutyNeuroOrderEntry> Orders = new();
}

/// <summary>
/// _Duty: действие хотбара для мгновенного запроса медика через нейроинтерфейс.
/// </summary>
public sealed partial class DutyNeuroMedicRequestActionEvent : InstantActionEvent
{
}

/// <summary>
/// _Duty: действие хотбара для подтверждения текущего приказа через нейроинтерфейс.
/// </summary>
public sealed partial class DutyNeuroAcknowledgeOrderActionEvent : InstantActionEvent
{
}
