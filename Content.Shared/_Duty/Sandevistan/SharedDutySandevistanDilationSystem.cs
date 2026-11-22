// SPDX-FileCopyrightText: 2026 LocalDuty
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.ADT.Heretic.Common;
using Content.Shared.ADT.Sandevistan;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._Duty.Sandevistan;

/// <summary>
/// _Duty: общие (Shared) модификаторы дилатации времени для сущностей с <see cref="SandevistanTimeDilatedComponent"/>:
/// движение, скорость ближнего боя, скорострельность и скорость новых снарядов, скорость DoAfter.
/// </summary>
public sealed class SharedDutySandevistanDilationSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SandevistanTimeDilatedComponent, ComponentStartup>(OnDilatedStartup);
        SubscribeLocalEvent<SandevistanTimeDilatedComponent, ComponentShutdown>(OnDilatedShutdown);
        SubscribeLocalEvent<SandevistanTimeDilatedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<SandevistanTimeDilatedComponent, GetDoAfterDelayMultiplierEvent>(OnGetDoAfterSpeed);
        SubscribeLocalEvent<SandevistanTimeDilatedComponent, GetMeleeAttackRateEvent>(OnGetMeleeAttackRate);
        SubscribeLocalEvent<SandevistanTimeDilatedComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);
    }

    private void OnDilatedStartup(Entity<SandevistanTimeDilatedComponent> ent, ref ComponentStartup args)
    {
        RefreshEntityModifiers(ent);
    }

    private void OnDilatedShutdown(Entity<SandevistanTimeDilatedComponent> ent, ref ComponentShutdown args)
    {
        RefreshEntityModifiers(ent);
    }

    private void RefreshEntityModifiers(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return;

        _speed.RefreshMovementSpeedModifiers(uid);

        if (HasComp<GunComponent>(uid))
            _gun.RefreshModifiers(uid);

        if (TryComp<HandsComponent>(uid, out var hands))
        {
            foreach (var held in _hands.EnumerateHeld((uid, hands)))
            {
                if (HasComp<GunComponent>(held))
                    _gun.RefreshModifiers(held);
            }
        }
    }

    private void OnRefreshSpeed(Entity<SandevistanTimeDilatedComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.LifeStage >= ComponentLifeStage.Stopping)
            return;

        var scale = Math.Clamp(ent.Comp.TimeScale, 0.05f, 1f);
        args.ModifySpeed(scale, scale);
    }

    private void OnGetDoAfterSpeed(Entity<SandevistanTimeDilatedComponent> ent, ref GetDoAfterDelayMultiplierEvent args)
    {
        if (ent.Comp.LifeStage >= ComponentLifeStage.Stopping)
            return;

        var scale = Math.Clamp(ent.Comp.TimeScale, 0.05f, 1f);
        args.Multiplier /= scale;
    }

    private void OnGetMeleeAttackRate(Entity<SandevistanTimeDilatedComponent> ent, ref GetMeleeAttackRateEvent args)
    {
        if (ent.Comp.LifeStage >= ComponentLifeStage.Stopping)
            return;

        if (TryComp<ActiveSandevistanUserComponent>(args.User, out var active) &&
            active.LifeStage < ComponentLifeStage.Stopping)
        {
            return;
        }

        var scale = Math.Clamp(ent.Comp.TimeScale, 0.05f, 1f);
        args.Multipliers *= scale;
    }

    private void OnGunRefreshModifiers(Entity<SandevistanTimeDilatedComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (ent.Comp.LifeStage >= ComponentLifeStage.Stopping)
            return;

        if (TryComp<TransformComponent>(ent, out var xform) &&
            xform.ParentUid.IsValid() &&
            TryComp<ActiveSandevistanUserComponent>(xform.ParentUid, out var active) &&
            active.LifeStage < ComponentLifeStage.Stopping)
        {
            return;
        }

        var scale = Math.Clamp(ent.Comp.TimeScale, 0.05f, 1f);
        args.FireRate *= scale;
        args.ProjectileSpeed *= scale;
    }
}
