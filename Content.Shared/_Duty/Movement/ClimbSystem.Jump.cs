// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Movement;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Events;
using Content.Shared.Physics;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;

namespace Content.Shared.Climbing.Systems;

/// <summary>
/// _Duty: продолжение <see cref="ClimbSystem"/> для прыжка (см. <see cref="SharedJumpSystem"/>,
/// портировано из Space Onyx). Прыжок «запрыгивает» на стол не через обычный DoAfter, а
/// напрямую — толкает игрока броском и по приземлению сам включает climb-фикстуры.
/// </summary>
public sealed partial class ClimbSystem
{
    private const int JumpClimbGroup = (int) (CollisionGroup.TableLayer | CollisionGroup.LowImpassable);

    public bool StartJumpClimb(EntityUid uid, ClimbingComponent climbing, EntityUid climbable, ClimbableComponent climbableComp)
    {
        if (!TryComp(uid, out FixturesComponent? fixtures))
            return false;

        EnsureClimbFixtures(uid, climbing, fixtures);
        climbing.IsClimbing = true;
        climbing.NextTransition = null;
        Dirty(uid, climbing);

        var start = new StartClimbEvent(climbable);
        var climbed = new ClimbedOnEvent(uid, uid);
        RaiseLocalEvent(uid, ref start);
        RaiseLocalEvent(climbable, ref climbed);
        return true;
    }

    public void EnsureMountedState(EntityUid uid, ClimbingComponent climbing)
    {
        if (!TryComp(uid, out FixturesComponent? fixtures))
            return;

        EnsureClimbFixtures(uid, climbing, fixtures);
        climbing.IsClimbing = true;
        climbing.NextTransition = null;
        Dirty(uid, climbing);
    }

    private void EnsureClimbFixtures(EntityUid uid, ClimbingComponent climbing, FixturesComponent fixtures)
    {
        // Терпим любое частично применённое состояние: upsert, а не Add, снимаем только те биты,
        // что реально стоят.
        foreach (var (name, fixture) in fixtures.Fixtures)
        {
            if (name == ClimbingFixtureName || !fixture.Hard)
                continue;
            if ((fixture.CollisionMask & JumpClimbGroup) == 0)
                continue;
            climbing.DisabledFixtureMasks[name] = fixture.CollisionMask & JumpClimbGroup;
            _physics.SetCollisionMask(uid, name, fixture, fixture.CollisionMask & ~JumpClimbGroup, fixtures);
        }

        if (!fixtures.Fixtures.ContainsKey(ClimbingFixtureName))
        {
            _fixtureSystem.TryCreateFixture(
                uid,
                new PhysShapeCircle(0.35f),
                ClimbingFixtureName,
                collisionLayer: (int) CollisionGroup.None,
                collisionMask: JumpClimbGroup,
                hard: false,
                manager: fixtures);
        }
    }

    public void FinishJumpClimb(EntityUid uid, ClimbingComponent climbing)
    {
        // Обычное приземление на пол: не залезали — снимать нечего. Без этой отсечки каждый прыжок
        // гонял StopClimb: лишний Dirty по сети и ложный EndClimbEvent.
        if (!climbing.IsClimbing && climbing.DisabledFixtureMasks.Count == 0)
            return;

        if (!TryComp(uid, out FixturesComponent? fixtures))
            return;

        StopClimb(uid, climbing, fixtures);
    }

    private bool IsJumpClimbing(EntityUid uid)
    {
        return TryComp<JumpComponent>(uid, out var jump) && jump.IsJumping && jump.MountTable;
    }
}
