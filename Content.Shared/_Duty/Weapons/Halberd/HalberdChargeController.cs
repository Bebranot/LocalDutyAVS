// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Friction;
using Content.Shared.Movement.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;

namespace Content.Shared._Duty.Weapons.Halberd;

/// <summary>
/// _Duty: ведёт скорость бегущих с алебардой. Физ-контроллер, а не Update системы: скорость надо
/// выставлять после мувера и трения, иначе трение съедало бы рывок, а ввод движения его перебивал.
/// Сама логика — в <see cref="SharedHalberdChargeSystem.UpdateCharge"/>.
/// </summary>
public sealed partial class HalberdChargeController : VirtualController
{
    [Dependency] private SharedHalberdChargeSystem _charge = default!;

    // Рывок может закончиться прямо в цикле (RemComp), поэтому сначала собираем список.
    private readonly List<Entity<HalberdChargingComponent, PhysicsComponent, TransformComponent>> _active = new();

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(SharedMoverController));
        UpdatesAfter.Add(typeof(TileFrictionController));

        base.Initialize();
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);

        _active.Clear();
        var query = EntityQueryEnumerator<HalberdChargingComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var charging, out var physics, out var xform))
        {
            // Клиент предсказывает только своё тело; чужой рывок приходит состоянием с сервера.
            if (prediction && !physics.Predict)
                continue;

            _active.Add((uid, charging, physics, xform));
        }

        foreach (var ent in _active)
        {
            if (ent.Comp1.LifeStage > ComponentLifeStage.Running)
                continue;

            _charge.UpdateCharge(ent, frameTime);
        }
    }
}
