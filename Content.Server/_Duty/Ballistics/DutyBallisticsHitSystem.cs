// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Ballistics;
using Content.Shared.CCVar;
using Content.Shared.Projectiles;
using Robust.Shared.Configuration;

namespace Content.Server._Duty.Ballistics;

/// <summary>
/// _Duty: в момент попадания пули передаёт цели её баллистический контекст (пробитие и бонусы
/// травм). ProjectileHitEvent поднимается сервером на пуле прямо перед TryChangeDamage, поэтому
/// броня (<see cref="DutyBallisticsSystem"/>) и роллер травм заберут контекст в том же вызове.
/// Подписка на свой компонент: пара (ProjectileComponent, ProjectileHitEvent) занята ThermalCloak.
/// </summary>
public sealed partial class DutyBallisticsHitSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private DutyBallisticsSystem _ballistics = default!;

    private float _penetrationScale = 1f;
    private float _traumaScale = 1f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyBallisticsComponent, ProjectileHitEvent>(OnProjectileHit);

        Subs.CVar(_cfg, DutyCCVars.BallisticsPenetrationScale, v => _penetrationScale = v, true);
        Subs.CVar(_cfg, DutyCCVars.BallisticsTraumaScale, v => _traumaScale = v, true);
    }

    private void OnProjectileHit(Entity<DutyBallisticsComponent> ent, ref ProjectileHitEvent args)
    {
        // Пуля, игнорирующая резисты, броню и так не видит — пробитие ей не нужно
        // (а поправка к броне без брони ничего бы и не сделала, но контекст лучше не оставлять).
        var penetration = ent.Comp.ArmorPenetration * _penetrationScale;
        if (TryComp<ProjectileComponent>(ent, out var projectile) && projectile.IgnoreResistances)
            penetration = 0f;

        var trauma = new BallisticTraumaContext(
            ent.Comp.ArteryBonus * _traumaScale,
            ent.Comp.FractureChance * _traumaScale,
            ent.Comp.MinTraumaDamage);

        _ballistics.SetPendingHit(args.Target, args.Shooter, penetration, trauma);
    }
}
