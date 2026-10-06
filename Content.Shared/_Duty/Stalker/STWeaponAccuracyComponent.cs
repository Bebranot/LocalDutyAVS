// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.Stalker;

/// <summary>
/// _Duty: точность ствола из портированного STALKER-14. Разброс ствола (minAngle/maxAngle в его
/// Gun) делится на множитель: больше — точнее. У марксманских полуавтоматов 3, у короткоствольного
/// AK-104 — 0.9. Углы в YAML этих стволов заданы с расчётом на этот делитель, без него стволы
/// стреляли почти веером. См. <see cref="STWeaponAccuracySystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class STWeaponAccuracyComponent : Component
{
    /// <summary>Множитель точности, когда ствол взят в обе руки (или если хвата у ствола нет вовсе).</summary>
    [DataField]
    public float AccuracyMultiplier = 1f;

    /// <summary>Множитель точности без хвата двумя руками.</summary>
    [DataField]
    public float AccuracyMultiplierUnwielded = 1f;

    /// <summary>Дополнительный множитель от модулей (прицел и т.п.), перемножается с основным.</summary>
    [DataField]
    public float ModifiedAccuracyMultiplier = 1f;
}
