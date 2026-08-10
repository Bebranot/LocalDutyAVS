// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.Trauma;

/// <summary>
/// _Duty: помощники для классификации <see cref="BodyZone"/> по крупным категориям.
/// Используется системами травм, чтобы не хардкодить списки зон (рука/нога/сустав)
/// в нескольких местах — это единая точка расширения при добавлении новых зон.
/// </summary>
public static class BodyZoneCategory
{
    /// <summary>Является ли зона рукой (левой или правой).</summary>
    public static bool IsArm(BodyZone zone) => zone is BodyZone.LeftArm or BodyZone.RightArm;

    /// <summary>Является ли зона ногой (левой или правой).</summary>
    public static bool IsLeg(BodyZone zone) => zone is BodyZone.LeftLeg or BodyZone.RightLeg;

    /// <summary>
    /// Является ли зона суставной конечностью (рука/нога) — только такие зоны
    /// могут получить вывих. Торс и голова суставами не считаются.
    /// </summary>
    public static bool IsJoint(BodyZone zone) => IsArm(zone) || IsLeg(zone);
}
