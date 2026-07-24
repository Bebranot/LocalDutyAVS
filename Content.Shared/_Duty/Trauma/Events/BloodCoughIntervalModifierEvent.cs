// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.Trauma.Events;

/// <summary>
/// _Duty: поднимается <c>BloodCoughSystem</c> (ADT, Content.Server) перед тем как выбрать
/// интервал до следующего кашля кровью — травмы торса могут учащать кашель, домножая интервал.
///
/// Определено в Shared, а не рядом с BloodCough, потому что подписчик (система торс-эффектов
/// переломов) живёт в Content.Shared и не может зависеть от Content.Server; событие — обратный
/// канал без нарушения слоёв сборки.
/// </summary>
/// <param name="Multiplier">
/// Множитель интервала до следующего кашля. Меньше 1 — кашель чаще; клампится на стороне
/// BloodCoughSystem, чтобы не уйти в ноль/отрицательные значения.
/// </param>
[ByRefEvent]
public record struct BloodCoughIntervalModifierEvent(float Multiplier = 1f);
