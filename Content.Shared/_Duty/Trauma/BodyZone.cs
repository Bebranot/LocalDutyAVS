// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.Trauma;

/// <summary>
/// _Duty: абстрактная зона тела для системы тяжёлых травм (переломы, вывихи).
/// Это НЕ сущность-часть-тела — в движке (nubody) конечностей как сущностей нет,
/// зона существует только как логический тег состояния на самой сущности моба.
/// Какие зоны вообще доступны конкретному существу, задаёт
/// <see cref="Components.TraumaTargetComponent.AvailableZones"/>.
/// </summary>
public enum BodyZone : byte
{
    Head,
    Torso,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg,
}
