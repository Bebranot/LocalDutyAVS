// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.Trauma.Components;

/// <summary>
/// _Duty: жгут самодельный — быстрая остановка артерии им не работает, остаётся только пошаговое
/// окно лечения.
///
/// Маркер именно «запрещающий», а не «разрешающий», потому что <c>DutyImprovisedTourniquet</c>
/// наследуется от штатного <c>Tourniquet</c>: любой компонент/тег, выданный фабричному жгуту,
/// достался бы и самоделке. Так же и наоборот — вешать маркер надо на самоделку, а вендорный
/// прототип не трогать вовсе.
/// </summary>
[RegisterComponent]
public sealed partial class MakeshiftTourniquetComponent : Component
{
}
