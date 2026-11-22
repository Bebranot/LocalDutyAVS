// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Recoil;
using Content.Shared.Flash;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Maths;

namespace Content.Shared._Duty.Cyberware.Gorlex;

/// <summary>
/// _Duty: предсказуемая часть имплантов Горлекса — множитель разброса оружия со смартлинком
/// и вспышко-иммунитет барабанного демпфера. Состояние выставляет сервер.
/// </summary>
public sealed partial class DutyGorlexSharedSystem : EntitySystem
{
    [Dependency] private SharedGunSystem _gun = default!;

    public override void Initialize()
    {
        base.Initialize();

        // После _Duty/Recoil: тот множит итоговый разброс на CVar и держит нижнюю границу,
        // наш множитель должен идти поверх, а не перед ним.
        SubscribeLocalEvent<DutyGorlexSmartlinkWeaponComponent, GunRefreshModifiersEvent>(OnGunRefresh,
            after: new[] { typeof(SharedDutyRecoilSystem) });

        SubscribeLocalEvent<DutyDrumDamperHostComponent, FlashAttemptEvent>(OnFlashAttempt);
    }

    /// <summary>Выставить множитель смартлинка оружию и пересчитать его разброс.</summary>
    public void SetSmartlinkMultiplier(Entity<DutyGorlexSmartlinkWeaponComponent> weapon, float multiplier)
    {
        if (MathHelper.CloseTo(weapon.Comp.Multiplier, multiplier))
            return;

        weapon.Comp.Multiplier = multiplier;
        Dirty(weapon);

        if (TryComp<GunComponent>(weapon, out var gun))
            _gun.RefreshModifiers((weapon, gun));
    }

    private void OnGunRefresh(Entity<DutyGorlexSmartlinkWeaponComponent> ent, ref GunRefreshModifiersEvent args)
    {
        var m = ent.Comp.Multiplier;
        if (MathHelper.CloseTo(m, 1f))
            return;

        args.MinAngle = new Angle(args.MinAngle.Theta * m);
        args.MaxAngle = new Angle(args.MaxAngle.Theta * m);
        args.AngleIncrease = new Angle(args.AngleIncrease.Theta * m);
        args.CameraRecoilScalar *= m;
    }

    private void OnFlashAttempt(Entity<DutyDrumDamperHostComponent> ent, ref FlashAttemptEvent args)
    {
        if (ent.Comp.Online)
            args.Cancelled = true;
    }
}
