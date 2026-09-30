// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Friction;
using Content.Shared.Movement.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;

namespace Content.Shared._Duty.Slide;

/// <summary>
/// _Duty: ведёт скорость скользящих. Физ-контроллер, а не Update системы, потому что скорость
/// надо выставлять после мувера и трения: иначе трение лежачего съедало бы подкат за пару тиков.
/// Сама логика — в <see cref="SharedDutySlideSystem.UpdateSlide"/>.
/// </summary>
public sealed partial class DutySlideController : VirtualController
{
    [Dependency] private SharedDutySlideSystem _slide = default!;

    // Подкат может закончиться прямо в цикле (RemComp), поэтому сначала собираем список.
    private readonly List<Entity<DutySlidingComponent, PhysicsComponent, TransformComponent>> _active = new();

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
        var query = EntityQueryEnumerator<DutySlidingComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var slide, out var physics, out var xform))
        {
            if (prediction && !physics.Predict)
                continue;

            _active.Add((uid, slide, physics, xform));
        }

        foreach (var ent in _active)
        {
            if (ent.Comp1.LifeStage > ComponentLifeStage.Running)
                continue;

            _slide.UpdateSlide(ent, frameTime);
        }
    }
}
