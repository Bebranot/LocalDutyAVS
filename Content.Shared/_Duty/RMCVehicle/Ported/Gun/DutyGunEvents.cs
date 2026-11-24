// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Map;

namespace Content.Shared._Duty.RMCVehicle.Gun;

/// <summary>
/// Поднимается на оружии перед выстрелом и позволяет подменить точку вылета и цель.
/// В RMC это делает расширенный AttemptShootEvent; наш (ADT) его не несёт, а менять сигнатуру
/// апстрим-события — значит ловить конфликты при каждом мерже. Поэтому отдельное событие.
/// Турель танка через него выносит снаряд на дуло и не даёт стрелять, пока ствол не довёрнут.
/// </summary>
[ByRefEvent]
public record struct DutyGunAimEvent(
    EntityUid User,
    EntityCoordinates FromCoordinates,
    EntityCoordinates? ToCoordinates,
    string? Message = null,
    bool Cancelled = false,
    bool ResetCooldown = false);

/// <summary>
/// Поднимается на стрелке в TryGetGun: техника отдаёт оружие, выбранное оператором
/// (стрелок сидит внутри, в руках у него ничего нет).
/// </summary>
[ByRefEvent]
public record struct DutyGetGunEvent(EntityUid? Gun = null);
