// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Neurointerface;
using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Duty.Cyberware.Neuro;

// Импланты «нейросетевой» линейки. Почти всё здесь — переиспользование нейроинтерфейса NT-SEC
// (отряды, метки, обводка, дешифровка), поэтому механика живёт в DutyNeurointerfaceSystem,
// а импланты только включают/выключают её через публичное API.

/// <summary>
/// _Duty: имплант-источник нейроинтерфейса: «NT-SEC (внутренний)» и «Тактическая сеть Горлекса».
/// Пока имплант вживлён — носитель подключён к сети <see cref="Network"/> так же, как в маске.
/// </summary>
[RegisterComponent]
public sealed partial class DutyNeuroLinkImplantComponent : Component
{
    [DataField]
    public DutyNeuroNetwork Network = DutyNeuroNetwork.NtSec;

    /// <summary>Действия, которые даёт имплант сверх <c>implantAction</c> (11-41, 10-4, приказы командира).</summary>
    [DataField]
    public List<EntProtoId> ExtraActions = new();

    [ViewVariables]
    public List<EntityUid> GrantedActions = new();
}

/// <summary>
/// _Duty: «Трекер СБ v2». Носитель виден ТРЕКЕРОМ во всей сети NT-SEC (не только в своём отряде),
/// а при крите/смерти имплант сам выходит в канал СБ.
/// </summary>
[RegisterComponent]
public sealed partial class DutyTrackerV2ImplantComponent : Component;

/// <summary>_Duty: маркер на носителе «Трекера СБ v2» (ставится и снимается имплантом).</summary>
[RegisterComponent]
public sealed partial class DutyTrackerV2Component : Component;

/// <summary>_Duty: «Тревожная кнопка» командования: 11-99 в канал СБ и метка SOS у всей сети NT-SEC.</summary>
[RegisterComponent]
public sealed partial class DutyPanicButtonImplantComponent : Component
{
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    /// <summary>Сколько метка SOS висит у сети, если её не сняли.</summary>
    [DataField]
    public TimeSpan MarkerDuration = TimeSpan.FromMinutes(2);
}

public sealed partial class DutyPanicButtonActionEvent : InstantActionEvent;

/// <summary>_Duty: одна точка назначения навигатора: группа маяков станции.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class DutyNavigatorDestination
{
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Предпочтительные маяки (главный в отделе). Из найденных берётся ближайший к носителю.</summary>
    [DataField]
    public List<EntProtoId> Beacons = new();

    /// <summary>Запасной поиск: любой маяк такого цвета (у маяков отдела общий цвет), если главных на карте нет.</summary>
    [DataField]
    public Color? BeaconColor;

    [DataField]
    public SpriteSpecifier? Icon;
}

/// <summary>_Duty: «Навигатор» — стрелка и ромб к выбранному отделу.</summary>
[RegisterComponent]
public sealed partial class DutyNavigatorImplantComponent : Component
{
    [DataField]
    public List<DutyNavigatorDestination> Destinations = new();
}

public sealed partial class DutyNavigatorOpenMenuEvent : InstantActionEvent;

[Serializable, NetSerializable]
public enum DutyNavigatorUiKey : byte
{
    Key
}

/// <summary>Выбор точки навигатора; индекс &lt; 0 — выключить.</summary>
[Serializable, NetSerializable]
public sealed class DutyNavigatorSelectMessage(int index) : BoundUserInterfaceMessage
{
    public int Index { get; } = index;
}

/// <summary>_Duty: цель навигатора на носителе. Клиент рисует по ней стрелку и ромб с дистанцией.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DutyNavigatorComponent : Component
{
    [AutoNetworkedField]
    public NetCoordinates? Target;

    [AutoNetworkedField]
    public string Label = string.Empty;

    [AutoNetworkedField]
    public Color Color = Color.FromHex("#3DDCFF");
}

/// <summary>_Duty: «Распознаватель угроз» — статус криминальной базы над головой и красная обводка разыскиваемых.</summary>
[RegisterComponent]
public sealed partial class DutyThreatScannerImplantComponent : Component;

/// <summary>_Duty: маркер на носителе распознавателя (клиентская обводка смотрит на него).</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DutyThreatScannerComponent : Component
{
    /// <summary>ShowCriminalRecordIcons поставили мы, а не раса/одежда: снимать можно только тогда.</summary>
    [ViewVariables]
    public bool AddedCriminalIcons;
}

/// <summary>_Duty: «Призрак в сети» (Синдикат) — выпадение из сенсоров, мониторов и нейросети NT.</summary>
[RegisterComponent]
public sealed partial class DutyNeuroGhostImplantComponent : Component
{
    /// <summary>ЭМИ сбивает маскировку на это время.</summary>
    [DataField]
    public TimeSpan EmpExposure = TimeSpan.FromMinutes(1);
}

/// <summary>
/// _Duty: носитель «Призрака в сети». Пока не <see cref="Exposed"/> — его нет в суит-сенсорах,
/// мониторе экипажа, трекерах и списках нейроинтерфейса, распознаватель угроз его не видит.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class DutyNeuroGhostComponent : Component
{
    [AutoNetworkedField]
    public bool Exposed;

    [AutoNetworkedField, AutoPausedField]
    public TimeSpan? ExposedUntil;

    public bool Hidden => !Exposed;
}

/// <summary>_Duty: «Червь ICE» — взлом нейроинтерфейса NT-SEC в упор.</summary>
[RegisterComponent]
public sealed partial class DutyIceWormImplantComponent : Component
{
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(20);

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(3);

    /// <summary>Дистанция в тайлах (дублирует range действия — сервер проверяет сам).</summary>
    [DataField]
    public float Range = 5f;
}

public sealed partial class DutyIceWormActionEvent : EntityTargetActionEvent;

/// <summary>_Duty: приказ командира тактической сети Горлекса в точку под курсором.</summary>
public sealed partial class DutyGorlexOrderActionEvent : WorldTargetActionEvent
{
    [DataField]
    public DutyNeuroOrderType Order = DutyNeuroOrderType.Assault;
}
