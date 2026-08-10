// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.Trauma;

/// <summary>
/// _Duty: тип тяжёлой травмы, который может «выпасть» при попадании.
/// Решение о типе принимает роллер травм по типу и величине урона одного удара.
/// Сотрясение мозга здесь отсутствует намеренно — оно не роллится напрямую по удару,
/// а запускается тяжёлым переломом головы.
/// </summary>
public enum TraumaType : byte
{
    /// <summary>Перелом зоны тела (тупой урон). Имеет тиры тяжести.</summary>
    Fracture,

    /// <summary>Вывих суставной зоны (тупой урон средней силы). Конкурирует с переломом.</summary>
    Dislocation,

    /// <summary>Артериальное кровотечение (режущий/колющий урон). Без привязки к зоне.</summary>
    ArterialBleed,
}
