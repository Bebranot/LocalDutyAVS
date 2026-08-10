// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.Trauma.Events;

/// <summary>
/// _Duty: поднимается, когда роллер травм решил, что при попадании выпала травма.
/// Само по себе событие ничего не применяет — его слушают системы-механики (артерия, перелом,
/// вывих, сотрясение) и накладывают свой эффект. Единая точка расширения набора травм.
///
/// ВАЖНО: событие широковещательное, а НЕ направленное на компонент. Движок допускает лишь одну
/// подписку на пару (компонент, событие), поэтому направленный вариант падал при старте, как только
/// на него подписалась четвёртая система-механика. Цель несётся полем <see cref="Target"/>.
/// </summary>
public sealed class TraumaRolledEvent : EntityEventArgs
{
    /// <summary>Существо, получившее травму.</summary>
    public readonly EntityUid Target;

    /// <summary>Тип выпавшей травмы.</summary>
    public readonly TraumaType Type;

    /// <summary>Зона тела (для перелома/вывиха) или null для беззонных травм (артерия).</summary>
    public readonly BodyZone? Zone;

    public TraumaRolledEvent(EntityUid target, TraumaType type, BodyZone? zone)
    {
        Target = target;
        Type = type;
        Zone = zone;
    }
}
