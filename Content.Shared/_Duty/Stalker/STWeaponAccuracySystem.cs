// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Recoil;
using Content.Shared._RMC14.Attachable.Systems;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;

namespace Content.Shared._Duty.Stalker;

/// <summary>
/// _Duty: делит разброс ствола на его точность (<see cref="STWeaponAccuracyComponent"/>).
/// Работает в RefreshModifiers, поэтому одинаково на сервере и в предикте клиента; смена хвата
/// пересчитывает модификаторы сама — ванилла делает это только для стволов с GunWieldBonus.
/// </summary>
public sealed partial class STWeaponAccuracySystem : EntitySystem
{
    [Dependency] private SharedGunSystem _gun = default!;

    /// <summary>Тот же пол угла, что у прицеливания и отдачи: GetRecoilAngle падает на нулевых/отрицательных углах.</summary>
    private const double MinTheta = 0.001;

    public override void Initialize()
    {
        base.Initialize();

        // После вычитающих модификаторов хвата и обвесов — делим итоговый разброс, а не базу, из
        // которой потом что-то вычтут. Глобальный множитель разброса (SharedDutyRecoilSystem) —
        // умножение, порядок с ним не важен.
        SubscribeLocalEvent<STWeaponAccuracyComponent, GunRefreshModifiersEvent>(OnRefreshModifiers,
            after: new[] { typeof(SharedWieldableSystem), typeof(AttachableHolderSystem) },
            before: new[] { typeof(SharedDutyRecoilSystem) });
        SubscribeLocalEvent<STWeaponAccuracyComponent, ItemWieldedEvent>(OnWielded);
        SubscribeLocalEvent<STWeaponAccuracyComponent, ItemUnwieldedEvent>(OnUnwielded);
    }

    private void OnRefreshModifiers(Entity<STWeaponAccuracyComponent> ent, ref GunRefreshModifiersEvent args)
    {
        var wielded = !TryComp<WieldableComponent>(ent, out var wieldable) || wieldable.Wielded;
        var accuracy = (wielded ? ent.Comp.AccuracyMultiplier : ent.Comp.AccuracyMultiplierUnwielded)
                       * ent.Comp.ModifiedAccuracyMultiplier;

        if (accuracy <= 0f || MathHelper.CloseTo(accuracy, 1f))
            return;

        var min = Math.Max(args.MinAngle.Theta / accuracy, MinTheta);
        var max = Math.Max(args.MaxAngle.Theta / accuracy, min);

        args.MinAngle = new Angle(min);
        args.MaxAngle = new Angle(max);
    }

    private void OnWielded(Entity<STWeaponAccuracyComponent> ent, ref ItemWieldedEvent args)
    {
        _gun.RefreshModifiers(ent.Owner);
    }

    private void OnUnwielded(EntityUid uid, STWeaponAccuracyComponent comp, ItemUnwieldedEvent args)
    {
        _gun.RefreshModifiers(uid);
    }
}
