// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.Cyberware;

// События носителя пробрасываются на импланты своими событиями, а не подписками каждого импланта
// на носителя: иначе второй SubscribeLocalEvent<..., EmpPulseEvent> на одну пару уронит сервер (Duplicate Subscriptions).

/// <summary>
/// _Duty: поднимается на каждом импланте носителя перед ЭМИ-сбоем. Любой имплант (анти-ЭМИ экран)
/// может отменить сбой для всего тела.
/// </summary>
[ByRefEvent]
public record struct DutyCyberEmpShieldEvent(EntityUid Host, bool Cancelled = false);

/// <summary>_Duty: поднимается на каждом импланте носителя, когда ЭМИ выключил кибер.</summary>
[ByRefEvent]
public readonly record struct DutyCyberEmpEvent(EntityUid Host, TimeSpan Until);

/// <summary>_Duty: поднимается на каждом импланте носителя, когда ЭМИ-сбой закончился.</summary>
[ByRefEvent]
public readonly record struct DutyCyberEmpEndedEvent(EntityUid Host);
