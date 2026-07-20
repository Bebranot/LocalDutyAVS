// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma.Components;

namespace Content.Shared._Duty.Trauma;

/// <summary>
/// _Duty: ключи локализации для зон тела и тиров переломов. Общие для осмотра здоровья,
/// анализатора и клиентского UI — чтобы названия не расходились между местами вывода.
/// </summary>
public static class TraumaLoc
{
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
}
