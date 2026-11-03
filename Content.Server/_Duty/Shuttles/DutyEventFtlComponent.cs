// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._Duty.Shuttles;

/// <summary>
/// _Duty: шаттл сейчас в ивентовом прыжке, запущенном админ-командой.
///
/// Точка выхода выбирается в момент команды, а прилетаем через десятки секунд: за это время туда может
/// подлететь чужой шаттл или игрок в скафандре, а при выходе из FTL всё под гридом сносится (Smimsh).
/// Поэтому пока висит маркер, <see cref="DutyEventFtlSystem"/> перепроверяет точку и при необходимости
/// переносит её. Снимается сам, когда прыжок закончился или прервался.
/// </summary>
[RegisterComponent, Access(typeof(DutyEventFtlSystem))]
public sealed partial class DutyEventFtlComponent : Component
{
    /// <summary>
    /// Грид, вокруг которого искали точку (ЦК или станция): новую точку ищем вокруг него же.
    /// </summary>
    [ViewVariables]
    public EntityUid Anchor;

    [ViewVariables]
    public TimeSpan NextCheck;
}
