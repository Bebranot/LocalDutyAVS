// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma.Components;
using Content.Shared.Verbs;

namespace Content.Shared._Duty.Trauma;

/// <summary>
/// _Duty: ключи локализации для зон тела и тиров переломов. Общие для осмотра здоровья,
/// анализатора и клиентского UI — чтобы названия не расходились между местами вывода.
/// </summary>
public static class TraumaLoc
{
    /// <summary>
    /// Вкладка ПКМ-меню «Самолечение» — общая для ВСЕХ процедур, которые можно делать себе
    /// (остановка артерии, вправление вывиха и т.д.). Одна на всех намеренно: два одинаковых по
    /// тексту, но разных по объекту VerbCategory дали бы в меню две отдельные вкладки с одним
    /// названием.
    /// </summary>
    public static readonly VerbCategory SelfTreatmentCategory =
        new("trauma-verb-category-self-treatment", "/Textures/Interface/VerbIcons/rejuvenate.svg.192dpi.png");

    /// <summary>
    /// Пересортировка ПКМ-меню (2026-08-20): все вербы лечения травм (жгут, вправление вывиха,
    /// шина) — обычный <see cref="Verb"/> (TypePriority 0), как и ванильный Examine. Явно ставим
    /// приоритет ниже нуля, чтобы вкладка "Самолечение" и одиночные вербы лечения других гарантированно
    /// оказывались НИЖЕ ванильных вкладок с приоритетом по умолчанию (0), а не полагались на
    /// алфавитный tie-break.
    /// </summary>
    public const int TreatmentVerbPriority = -10;

    public static string ZoneKey(BodyZone zone) => zone switch
    {
        BodyZone.Head => "trauma-zone-head",
        BodyZone.Torso => "trauma-zone-torso",
        BodyZone.LeftArm => "trauma-zone-left-arm",
        BodyZone.RightArm => "trauma-zone-right-arm",
        BodyZone.LeftLeg => "trauma-zone-left-leg",
        BodyZone.RightLeg => "trauma-zone-right-leg",
        _ => "trauma-zone-torso",
    };

    public static string FractureTierKey(FractureTier tier) => tier switch
    {
        FractureTier.Crack => "trauma-fracture-tier-crack",
        FractureTier.Full => "trauma-fracture-tier-full",
        FractureTier.Open => "trauma-fracture-tier-open",
        _ => "trauma-fracture-tier-crack",
    };

    public static string HeadTraumaTierKey(HeadTraumaTier tier) => tier switch
    {
        HeadTraumaTier.Light => "trauma-head-tier-light",
        HeadTraumaTier.Medium => "trauma-head-tier-medium",
        HeadTraumaTier.Severe => "trauma-head-tier-severe",
        _ => "trauma-head-tier-light",
    };
}
