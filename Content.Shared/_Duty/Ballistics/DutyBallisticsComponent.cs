// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.Ballistics;

/// <summary>
/// _Duty: баллистические свойства ПУЛИ (вешается на прототип снаряда, не на патрон).
/// Данные статичны и одинаково читаются из прототипа на сервере и клиенте, поэтому компонент
/// не сетевой: клиенту он нужен только для осмотра патрона.
/// </summary>
[RegisterComponent]
public sealed partial class DutyBallisticsComponent : Component
{
    /// <summary>
    /// Доля защиты брони, которую «снимает» пуля: эффективный коэффициент брони
    /// <c>c' = c + p·(1 − c)</c>. 0.3 — жилет 0.4 работает как 0.58. Отрицательное значение —
    /// экспансивные пули: броня режет их сильнее, чем обычные (−0.35 — жилет 0.4 работает как 0.19).
    /// Пробитие касается только брони в слотах инвентаря: щиты, видовые резисты и прочие
    /// модификаторы работают как обычно.
    /// </summary>
    [DataField]
    public float ArmorPenetration;

    /// <summary>
    /// Прибавка к шансу артериального кровотечения (абсолютная: 0.02 = +2%), если пуля пробила
    /// с уроном не ниже <see cref="MinTraumaDamage"/>. Базовый шанс считает TraumaRollSystem.
    /// </summary>
    [DataField]
    public float ArteryBonus;

    /// <summary>
    /// Шанс перелома от самого попадания (0.02 = 2%). Пуля почти не несёт тупого урона, поэтому
    /// обычный blunt-ролл перелома её не видит — крупным калибрам перелом даёт этот отдельный ролл.
    /// </summary>
    [DataField]
    public float FractureChance;

    /// <summary>
    /// Минимальный урон после брони, начиная с которого действуют бонусы травм.
    /// Пуля, которую броня почти остановила, кость не ломает.
    /// </summary>
    [DataField]
    public float MinTraumaDamage = 10f;
}
