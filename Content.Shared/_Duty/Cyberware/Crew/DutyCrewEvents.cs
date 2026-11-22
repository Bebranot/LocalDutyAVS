// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Actions;

namespace Content.Shared._Duty.Cyberware.Crew;

/// <summary>Переключатель модуля (мезоны, ночное зрение, магнитные ступни). Поднимается на импланте.</summary>
public sealed partial class DutyCyberGrantToggleEvent : InstantActionEvent;

public sealed partial class DutyCrewClockEvent : InstantActionEvent;

public sealed partial class DutyCrewMultitoolEvent : InstantActionEvent;

public sealed partial class DutyCrewAdrenalineEvent : InstantActionEvent;

public sealed partial class DutyCrewRecorderEvent : InstantActionEvent;

/// <summary>
/// _Duty: поднимается на импланте после того, как выданные компоненты добавлены/сняты с носителя.
/// Нужен имплантам, которым после выдачи надо донастроить носителя (магнитные ступни).
/// </summary>
[ByRefEvent]
public readonly record struct DutyCyberGrantChangedEvent(EntityUid Host, bool Granted);
