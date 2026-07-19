// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.Trauma.Events;

/// <summary>
/// _Duty: поднимается на существе, когда роллер травм решил, что при попадании выпала травма.
/// Само по себе событие ничего не применяет — его слушают системы-механики (артерия, перелом,
/// вывих) и накладывают свой эффект. Единая точка расширения набора травм.
/// </summary>
/// <param name="Type">Тип выпавшей травмы.</param>
/// <param name="Zone">Зона тела (для перелома/вывиха) или null для беззонных травм (артерия).</param>
/// <param name="Damage">Величина урона удара, вызвавшего травму — для масштабирования эффекта.</param>
[ByRefEvent]
public readonly record struct TraumaRolledEvent(TraumaType Type, BodyZone? Zone, float Damage);
