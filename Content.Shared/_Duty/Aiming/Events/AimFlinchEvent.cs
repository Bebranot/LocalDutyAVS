// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Aiming.Events;

/// <summary>
/// Сервер → клиент стрелка: попадание сбило прицел. Направление рывка камеры считает клиент —
/// только он знает, где сейчас курсор; сервер передаёт лишь, насколько сильным было попадание.
/// </summary>
[Serializable, NetSerializable]
public sealed class AimFlinchEvent(float damage) : EntityEventArgs
{
    public readonly float Damage = damage;
}
