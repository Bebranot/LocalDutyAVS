using Content.Shared._Duty.Aiming;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wieldable.Components;

namespace Content.Server._Duty.Aiming;

/// <summary>
/// Прошивание пуль при прицельной стрельбе ЛЁЖА. При выстреле из <see cref="AimableComponent"/>-оружия,
/// когда стрелок целится лёжа, помечает выпущенные пули <see cref="AimPenetrationComponent"/>.
/// Дальше пуля проходит сквозь живые цели: первой — полный урон, следующим — со штрафом, до лимита.
/// Решение о прекращении полёта пули принимает <see cref="Content.Server.Projectiles.ProjectileSystem"/>.
/// </summary>
public sealed class AimPenetrationSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AimableComponent, AmmoShotEvent>(OnAmmoShot);
        SubscribeLocalEvent<AimPenetrationComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    private void OnAmmoShot(Entity<AimableComponent> ent, ref AmmoShotEvent args)
    {
        if (!ent.Comp.ProneAimPenetration)
            return;

        // Стрелок — текущий владелец двуручного оружия. Прошивание только при прицеливании лёжа.
        if (!TryComp<WieldableComponent>(ent, out var wieldable) || wieldable.User is not { } user)
            return;

        if (!TryComp<AimingComponent>(user, out var aiming) || !aiming.IsProne || aiming.Gun != ent.Owner)
            return;

        foreach (var projectile in args.FiredProjectiles)
        {
            if (!HasComp<ProjectileComponent>(projectile))
                continue;

            var pen = EnsureComp<AimPenetrationComponent>(projectile);
            pen.MaxTargets = Math.Max(1, ent.Comp.ProneAimPenetrationTargets);
            pen.FalloffMultiplier = ent.Comp.ProneAimPenetrationFalloff;
        }
    }

    private void OnProjectileHit(Entity<AimPenetrationComponent> ent, ref ProjectileHitEvent args)
    {
        var pen = ent.Comp;

        // Та же цель повторно — урона нет (защита от двойного попадания одной пулей).
        if (!pen.HitEntities.Add(args.Target))
        {
            args.Damage = new DamageSpecifier();
            return;
        }

        // Прошиваются только живые цели. Стена/неживой объект останавливают пулю.
        if (!HasComp<MobStateComponent>(args.Target))
        {
            pen.Hits = pen.MaxTargets;
            return;
        }

        pen.Hits++;

        // Второй и последующим целям — урон со штрафом.
        if (pen.Hits >= 2)
            args.Damage = args.Damage * pen.FalloffMultiplier;
    }
}
