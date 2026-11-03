// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Timing;

namespace Content.Shared._Duty.Ballistics;

/// <summary>
/// _Duty: цель, к которой применяются бронепробитие и баллистические травмы.
///
/// Урон движка не знает, ЧЕМ он нанесён (в DamageModifyEvent/DamageDealtEvent есть только стрелок),
/// поэтому сервер в момент попадания пули кладёт сюда «контекст попадания», а броня и роллер травм
/// забирают его синхронно внутри того же TryChangeDamage. Контекст привязан к тику и стрелку и
/// стирается при чтении: если урон отменили до брони/травм, он не «прилипнет» к чужому удару.
/// Поля — runtime-состояние сервера, не сохраняются и не синхронизируются.
/// </summary>
[RegisterComponent, Access(typeof(DutyBallisticsSystem))]
public sealed partial class DutyBallisticTargetComponent : Component
{
    [ViewVariables]
    public bool HasPendingPenetration;

    [ViewVariables]
    public float PendingPenetration;

    [ViewVariables]
    public EntityUid? PendingShooter;

    [ViewVariables]
    public GameTick PendingPenetrationTick;

    [ViewVariables]
    public bool HasPendingTrauma;

    [ViewVariables]
    public BallisticTraumaContext PendingTrauma;

    [ViewVariables]
    public GameTick PendingTraumaTick;
}

/// <summary>Бонусы травм от одной пули, уже с учётом серверного множителя.</summary>
public record struct BallisticTraumaContext(float ArteryBonus, float FractureChance, float MinTraumaDamage);
