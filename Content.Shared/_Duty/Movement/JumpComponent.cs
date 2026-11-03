// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Movement;

/// <summary>
/// _Duty: прыжок (портировано из Space Onyx). Даёт короткий рывок в направлении ввода —
/// либо перепрыгивает через препятствия/степ-триггеры, либо запрыгивает на стол в зоне
/// впереди (см. <see cref="SharedJumpSystem"/>).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class JumpComponent : Component
{
    [DataField]
    public float Distance = 0.7f;

    [DataField]
    public float SprintDistance = 1.1f;

    [DataField]
    public float TableDistance = 1.0f;

    [DataField]
    public float Speed = 8f;

    [DataField]
    public TimeSpan StationaryDuration = TimeSpan.FromSeconds(0.5);

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(1);

    [DataField]
    public float StaminaCost = 30f;

    [DataField]
    public float MinimumStamina = 10f;

    [DataField]
    public float WeightlessStaminaCostMultiplier = 0.5f;

    /// <summary>Шанс споткнуться при приземлении — случайный в этих пределах (0.05–0.10, как в Onyx).</summary>
    [DataField]
    public float StumbleChanceMin = 0.05f;

    [DataField]
    public float StumbleChanceMax = 0.10f;

    [DataField]
    public TimeSpan StumbleDuration = TimeSpan.FromSeconds(2);

    [AutoNetworkedField, AutoPausedField]
    public TimeSpan NextJump;

    [AutoNetworkedField]
    public bool IsJumping;

    [AutoNetworkedField]
    public bool MountTable;

    [AutoNetworkedField, AutoPausedField]
    public TimeSpan JumpStarted;

    [AutoNetworkedField, AutoPausedField]
    public TimeSpan JumpEnds;

    /// <summary>
    /// Стоит на столе, на который запрыгнул. Пока флаг поднят, система каждый тик проверяет, что под
    /// ногами всё ещё стол, и снимает «залезание», как только это не так — иначе коллизия со столами
    /// оставалась снятой, персонаж «парил» и проходил на столы без прыжка.
    /// </summary>
    [AutoNetworkedField]
    public bool JumpMounted;
}
