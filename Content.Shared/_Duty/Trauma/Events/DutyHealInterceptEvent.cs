// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.Trauma.Events;

/// <summary>
/// _Duty: хук в ванильный <c>HealingSystem.TryHeal</c>. Поднимается на ЦЕЛИ лечения до того, как
/// ваниль начнёт свой DoAfter, и позволяет _Duty-системам увести конкретный предмет в собственную
/// ветку лечения (жгут при артериальном кровотечении).
///
/// Сделано событием, а не прямым вызовом: <c>HealingSystem</c> живёт в вендорном дереве и не должен
/// знать ни про травмы, ни про жгуты. Подписаться на ванильные <c>UseInHandEvent</c>/
/// <c>AfterInteractEvent</c> снаружи нельзя — движок допускает лишь ОДНУ directed-подписку на пару
/// (компонент, событие), а обе уже заняты самим <c>HealingSystem</c>.
/// </summary>
/// <param name="Item">Лечебный предмет (носитель <c>HealingComponent</c>).</param>
/// <param name="User">Кто лечит.</param>
/// <param name="Handled">Ветка перехвачена — ванильное лечение не выполняется.</param>
[ByRefEvent]
public record struct DutyHealInterceptEvent(EntityUid Item, EntityUid User, bool Handled = false);
