// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Gravity;
using Content.Shared._Duty.Movement;
using Content.Shared.Climbing.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Gravity;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Input;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Standing;
using Content.Shared.StepTrigger.Systems;
using Robust.Client.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: прыжок (порт Space Onyx).
///
/// В первой версии порта прыжок был недоступен вовсе: функцию Jump не добавили в контекст ввода
/// human, и нажатие Space до системы не доходило. Плюс прыжок через ловушки молча не работал
/// (движок не спрашивал наступившего), а в невесомости прыжок уносил назад отдачей броска.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedJumpSystem))]
public sealed class JumpTests : GameTest
{
    /// <summary>Клавиша прыжка должна быть в контексте ввода персонажа — иначе Space глохнет.</summary>
    [Test]
    public async Task JumpIsBoundInHumanContext()
    {
        await Client.WaitAssertion(() =>
        {
            var input = Client.ResolveDependency<IInputManager>();
            Assert.That(input.Contexts.TryGetContext("human", out var human), Is.True);
            Assert.That(human!.FunctionExistsHierarchy(ContentKeyFunctions.Jump), Is.True,
                "Jump не добавлен в контекст human (ContentContexts) — клавиша прыжка ничего не делает.");
        });
    }

    /// <summary>Все играбельные расы умеют прыгать.</summary>
    [Test]
    public async Task EveryPlayableSpeciesCanJump()
    {
        var protoMan = Server.ResolveDependency<IPrototypeManager>();
        var compFactory = Server.ResolveDependency<IComponentFactory>();

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var species in protoMan.EnumeratePrototypes<SpeciesPrototype>())
                {
                    if (!species.RoundStart || !protoMan.TryIndex(species.Prototype, out var mob))
                        continue;

                    Assert.That(mob.TryGetComponent<JumpComponent>(out _, compFactory), Is.True,
                        $"У расы {species.ID} нет компонента Jump.");
                }
            });
        });
    }

    /// <summary>Прыжок на месте: тратит стамину, ставит откат, заканчивается сам.</summary>
    [Test]
    public async Task StationaryJumpCostsStaminaAndEnds()
    {
        var map = await Pair.CreateTestMap();
        var human = await SpawnAtPosition("MobHuman", map.GridCoords);
        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var jumpSys = SEntMan.System<SharedJumpSystem>();
            var staminaSys = SEntMan.System<SharedStaminaSystem>();
            var jump = SEntMan.GetComponent<JumpComponent>(human);
            var stamina = SEntMan.GetComponent<StaminaComponent>(human);

            var before = staminaSys.GetStaminaDamage(human, stamina);
            Assert.That(jumpSys.TryJump((human, jump)), Is.True, "Стоящий живой человек не смог прыгнуть.");
            Assert.That(jump.IsJumping, Is.True);
            Assert.That(staminaSys.GetStaminaDamage(human, stamina), Is.GreaterThan(before), "Прыжок не потратил стамину.");
            Assert.That(jumpSys.TryJump((human, jump)), Is.False, "Второй прыжок в воздухе прошёл.");
        });

        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<JumpComponent>(human).IsJumping, Is.False,
                "Прыжок на месте не закончился сам.");
        });
    }

    /// <summary>Лёжа прыгать нельзя.</summary>
    [Test]
    public async Task CannotJumpWhileDown()
    {
        var map = await Pair.CreateTestMap();
        var human = await SpawnAtPosition("MobHuman", map.GridCoords);

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<StandingStateSystem>().Down(human);
            var jump = SEntMan.GetComponent<JumpComponent>(human);
            Assert.That(SEntMan.System<SharedJumpSystem>().TryJump((human, jump)), Is.False);
        });
    }

    /// <summary>
    /// Прыжок с направлением уносит вперёд. На тестовой карте нет гравитации — раньше именно тут
    /// отдача броска (бросаем сами себя) толкала тело назад сильнее, чем сам прыжок вперёд.
    /// </summary>
    [Test]
    public async Task DirectionalJumpMovesForward()
    {
        var map = await Pair.CreateTestMap();
        var human = await SpawnAtPosition("MobHuman", map.GridCoords);
        await RunTicksSync(5);

        var startX = 0f;
        await Server.WaitAssertion(() =>
        {
            var xformSys = SEntMan.System<SharedTransformSystem>();
            startX = xformSys.GetWorldPosition(human).X;

            SEntMan.GetComponent<InputMoverComponent>(human).HeldMoveButtons = MoveButtons.Right;
            var jump = SEntMan.GetComponent<JumpComponent>(human);
            Assert.That(SEntMan.System<SharedJumpSystem>().TryJump((human, jump)), Is.True);
            SEntMan.GetComponent<InputMoverComponent>(human).HeldMoveButtons = MoveButtons.None;
        });

        await RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            var x = SEntMan.System<SharedTransformSystem>().GetWorldPosition(human).X;
            Assert.That(x, Is.GreaterThan(startX + 0.1f), $"Прыжок вправо не сдвинул вправо: было {startX}, стало {x}.");
        });
    }

    /// <summary>
    /// Запрыгнуть на стол можно сколько угодно раз, а не только первый: после ухода со стола
    /// состояние «залез» должно полностью сниматься.
    /// </summary>
    [Test]
    public async Task CanJumpOntoTableRepeatedly()
    {
        var map = await Pair.CreateTestMap();
        var mapSys = SEntMan.System<SharedMapSystem>();

        await Server.WaitPost(() =>
        {
            for (var x = -1; x <= 4; x++)
                mapSys.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);

            var gravity = SEntMan.EnsureComponent<GravityComponent>(map.Grid);
            SEntMan.System<GravitySystem>().EnableGravity(map.Grid, gravity);
        });

        var start = new EntityCoordinates(map.Grid, 1.5f, 0.5f);
        var human = await SpawnAtPosition("MobHuman", start);
        await SpawnAtPosition("Table", new EntityCoordinates(map.Grid, 2.5f, 0.5f));
        await Server.WaitPost(() =>
        {
            // Случайное спотыкание делает тест плавающим; оно проверяется в JumpInputTests.
            var jump = SEntMan.GetComponent<JumpComponent>(human);
            jump.StumbleChanceMin = 0f;
            jump.StumbleChanceMax = 0f;
        });
        await RunTicksSync(10);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await Server.WaitAssertion(() =>
            {
                var mover = SEntMan.GetComponent<InputMoverComponent>(human);
                var jump = SEntMan.GetComponent<JumpComponent>(human);
                mover.HeldMoveButtons = MoveButtons.Right;
                var diag = $"IsJumping={jump.IsJumping} NextJump={jump.NextJump} now={Server.ResolveDependency<IGameTiming>().CurTime} " +
                           $"down={SEntMan.System<StandingStateSystem>().IsDown(human)} " +
                           $"thrown={SEntMan.HasComponent<Content.Shared.Throwing.ThrownItemComponent>(human)} " +
                           $"canMove={SEntMan.System<Content.Shared.ActionBlocker.ActionBlockerSystem>().CanMove(human)} " +
                           $"stam={SEntMan.GetComponent<StaminaComponent>(human).StaminaDamage}";
                Assert.That(SEntMan.System<SharedJumpSystem>().TryJump((human, jump)), Is.True,
                    $"Попытка {attempt}: прыжок не начался. {diag}");
                mover.HeldMoveButtons = MoveButtons.None;
            });

            await RunSeconds(1f);

            await Server.WaitAssertion(() =>
            {
                var x = SEntMan.System<SharedTransformSystem>().GetWorldPosition(human).X;
                var climbing = SEntMan.GetComponent<ClimbingComponent>(human);
                var jump = SEntMan.GetComponent<JumpComponent>(human);
                Assert.Multiple(() =>
                {
                    Assert.That(jump.IsJumping, Is.False, $"Попытка {attempt}: прыжок не закончился.");
                    Assert.That(x, Is.GreaterThan(2f), $"Попытка {attempt}: не долетел до стола, x = {x}.");
                    Assert.That(climbing.IsClimbing, Is.True, $"Попытка {attempt}: приземлился, но не на стол.");
                    Assert.That(jump.JumpMounted, Is.True, $"Попытка {attempt}: не отмечен как стоящий на столе.");
                });

                // Уходим со стола обратно на пол.
                SEntMan.System<SharedTransformSystem>().SetCoordinates(human, start);
            });

            await RunSeconds(1.5f);

            await Server.WaitAssertion(() =>
            {
                var climbing = SEntMan.GetComponent<ClimbingComponent>(human);
                var jump = SEntMan.GetComponent<JumpComponent>(human);
                Assert.Multiple(() =>
                {
                    Assert.That(climbing.IsClimbing, Is.False, $"Попытка {attempt}: ушёл со стола, а «залез» не снялся.");
                    Assert.That(climbing.DisabledFixtureMasks, Is.Empty, $"Попытка {attempt}: коллизия со столами осталась снятой.");
                    Assert.That(jump.JumpMounted, Is.False);
                });
            });
        }
    }

    /// <summary>В прыжке мышеловка не срабатывает — наступившего теперь спрашивают отдельным событием.</summary>
    [Test]
    public async Task JumpingSkipsStepTraps()
    {
        var map = await Pair.CreateTestMap();
        var human = await SpawnAtPosition("MobHuman", map.GridCoords);
        var trap = await SpawnAtPosition("Mousetrap", map.GridCoords);
        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var jump = SEntMan.GetComponent<JumpComponent>(human);

            var grounded = new StepTriggerTripperAttemptEvent(trap);
            SEntMan.EventBus.RaiseLocalEvent(human, ref grounded);
            Assert.That(grounded.Cancelled, Is.False, "На земле мышеловка должна срабатывать.");

            Assert.That(SEntMan.System<SharedJumpSystem>().TryJump((human, jump)), Is.True);
            var airborne = new StepTriggerTripperAttemptEvent(trap);
            SEntMan.EventBus.RaiseLocalEvent(human, ref airborne);
            Assert.That(airborne.Cancelled, Is.True, "В прыжке мышеловка сработала.");
        });
    }
}
