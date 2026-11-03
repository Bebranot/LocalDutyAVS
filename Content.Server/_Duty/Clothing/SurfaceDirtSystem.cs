// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx (FootprintSystem.TryPuddleInteraction), без самих следов.

using Content.Shared._Duty.Clothing;
using Content.Shared._Duty.Traits;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Gravity;
using Content.Shared.Inventory;
using Content.Shared.Standing;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._Duty.Clothing;

/// <summary>
/// _Duty: кто идёт по луже — пачкает обувь, кто ползёт — всю одежду.
/// <para>
/// В Onyx это висит на MoveEvent каждого персонажа — событие на каждое смещение на каждом тике.
/// Здесь вместо этого редкий опрос: раз в <see cref="CheckInterval"/> смотрим, стоит ли
/// движущийся персонаж на тайле с лужей. Один «шаг» грязи за интервал по объёму получается
/// сопоставимым с шагами Onyx, а стоит это пару поисков по заякоренным сущностям в секунду.
/// </para>
/// </summary>
public sealed class SurfaceDirtSystem : EntitySystem
{
    [Dependency] private readonly ClothingDirtSystem _dirt = default!;
    [Dependency] private readonly SharedGravitySystem _gravity = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;

    private const float CheckInterval = 0.5f;

    /// <summary>Медленнее этого (м/с, в квадрате) — стоит на месте, грязь не набирается.</summary>
    private const float MinSpeedSquared = 0.25f;

    private float _accumulator;

    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<SurfaceDirtSourceComponent> _sourceQuery;
    private EntityQuery<LightStepComponent> _lightStepQuery;

    public override void Initialize()
    {
        base.Initialize();

        _gridQuery = GetEntityQuery<MapGridComponent>();
        _sourceQuery = GetEntityQuery<SurfaceDirtSourceComponent>();
        _lightStepQuery = GetEntityQuery<LightStepComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < CheckInterval)
            return;
        _accumulator = 0f;

        var query = EntityQueryEnumerator<InventoryComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var physics, out var xform))
        {
            if (physics.LinearVelocity.LengthSquared() < MinSpeedSquared
                || xform.GridUid is not { } gridUid
                || !_gridQuery.TryComp(gridUid, out var grid)
                || _lightStepQuery.HasComp(uid))
                continue;

            var tile = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
            while (anchored.MoveNext(out var anchoredUid))
            {
                if (!_sourceQuery.TryComp(anchoredUid, out var source))
                    continue;

                TryApply(uid, source, anchoredUid.Value);
                break;
            }
        }
    }

    private void TryApply(EntityUid walker, SurfaceDirtSourceComponent source, EntityUid surface)
    {
        if (!_solutions.TryGetSolution(surface, source.Solution, out _, out var solution)
            || solution.Volume < source.MinimumVolume
            || _gravity.IsWeightless(walker))
            return;

        var amount = FixedPoint2.Min(solution.Volume, source.TransferAmount);
        if (_standing.IsDown(walker))
            _dirt.TryDirtyWornPuddleCrawl(walker, solution, amount);
        else
            _dirt.TryDirtyWornPuddleStep(walker, solution, amount);
    }
}
