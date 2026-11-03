// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

namespace Content.Client._Duty.Aiming;

/// <summary>
/// Только клиент, только локальный игрок: идущий рывок камеры от попадания во время прицеливания.
/// Снимается сам, когда камера вернулась на место.
/// </summary>
[RegisterComponent]
public sealed partial class AimFlinchComponent : Component
{
    /// <summary>Смещение, с которого начался рывок (если прошлый ещё не отыграл — продолжаем от него).</summary>
    public Vector2 From;

    /// <summary>Крайняя точка рывка, в мировых координатах (тайлы).</summary>
    public Vector2 Kick;

    public TimeSpan Start;

    /// <summary>Текущее смещение — от него стартует следующий рывок, если попадут снова.</summary>
    public Vector2 Current;
}
