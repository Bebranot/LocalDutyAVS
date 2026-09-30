// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Duty.Movement;
using Content.Shared.Climbing.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Input;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: прыжок на стол через настоящий ввод игрока — клиент предсказывает прыжок, сервер его
/// подтверждает. Серверные тесты в <see cref="JumpTests"/> этот путь не покрывают, а баги прыжка
/// («запрыгнул один раз, дальше не выходит») жили именно в расхождении клиента и сервера.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedJumpSystem))]
public sealed class JumpInputTests : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [Test]
    public async Task JumpOntoTableRepeatedlyViaInput([Values(0f, 1f)] float stumbleChance)
    {
        // Пол на несколько клеток вправо, стол — в соседней клетке, гравитация.
        for (var x = 1; x <= 3; x++)
            await SetTile(Plating, SEntMan.GetNetCoordinates(MapData.GridCoords.Offset(new Vector2(x + 0.5f, 0.5f))), MapData.Grid);
        await AddGravity();
        // Без атмосферы персонаж получает урон от вакуума, и он обрывает DoAfter подъёма.
        await AddAtmosphere();
        await SpawnTarget("Table");
        await Server.WaitPost(() =>
        {
            // Спотыкание случайно — фиксируем: 0 (никогда) или 1 (каждое приземление, в т.ч. на столе).
            var jump = SEntMan.GetComponent<JumpComponent>(SPlayer);
            jump.StumbleChanceMin = stumbleChance;
            jump.StumbleChanceMax = stumbleChance;
        });
        await RunTicks(10);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await Server.WaitPost(() =>
            {
                // Случайное спотыкание при приземлении и накопленная стамина — не то, что мы тут
                // проверяем; убираем их, чтобы тест ловил именно поломку залезания.
                SEntMan.System<StandingStateSystem>().Stand(SPlayer, force: true);
                var stamina = SEntMan.GetComponent<StaminaComponent>(SPlayer);
                stamina.StaminaDamage = 0;
                SEntMan.Dirty(SPlayer, stamina);
            });
            await RunTicks(5);

            var sStarted = TimeSpan.Zero;
            await Server.WaitPost(() => sStarted = SEntMan.GetComponent<JumpComponent>(SPlayer).JumpStarted);

            // Жмём вправо и прыжок, как игрок.
            await SetMovementKey(DirectionFlag.East, BoundKeyState.Down);
            await RunTicks(2);
            await PressKey(ContentKeyFunctions.Jump);
            await SetMovementKey(DirectionFlag.East, BoundKeyState.Up);
            await RunSeconds(1f);

            await Server.WaitAssertion(() =>
            {
                var x = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer).X;
                var climbing = SEntMan.GetComponent<ClimbingComponent>(SPlayer);
                var jump = SEntMan.GetComponent<JumpComponent>(SPlayer);
                Assert.Multiple(() =>
                {
                    Assert.That(jump.JumpStarted, Is.Not.EqualTo(sStarted), $"Попытка {attempt}: сервер не начал прыжок.");
                    Assert.That(jump.IsJumping, Is.False, $"Попытка {attempt}: прыжок не закончился.");
                    Assert.That(x, Is.GreaterThan(1.2f), $"Попытка {attempt}: не запрыгнул на стол, x = {x}.");
                    Assert.That(climbing.IsClimbing, Is.True, $"Попытка {attempt}: сервер не считает, что стоим на столе.");
                });
            });

            // Упал при приземлении — в ADT автоподъёма нет: ждём конца оглушения и встаём кнопкой, как игрок.
            if (stumbleChance > 0f)
            {
                await RunSeconds(2.5f);
                await PressKey(ContentKeyFunctions.ToggleKnockdown);
                // Подъём в ADT длится дольше при потраченной стамине (до ×2) — ждём с запасом.
                await RunSeconds(5f);
                await Server.WaitAssertion(() =>
                {
                    var climbing = SEntMan.GetComponent<ClimbingComponent>(SPlayer);
                    Assert.That(SEntMan.System<StandingStateSystem>().IsDown(SPlayer), Is.False,
                        $"Попытка {attempt}: упал на столе и не смог встать кнопкой.");
                });
            }

            // Уходим со стола влево: чётные попытки — пешком, нечётные — спрыгиваем.
            if (attempt % 2 == 0)
            {
                await Move(DirectionFlag.West, 0.6f);
            }
            else
            {
                await SetMovementKey(DirectionFlag.West, BoundKeyState.Down);
                await RunTicks(2);
                await PressKey(ContentKeyFunctions.Jump);
                await SetMovementKey(DirectionFlag.West, BoundKeyState.Up);
            }
            await RunSeconds(1f);

            await Server.WaitAssertion(() =>
            {
                var x = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer).X;
                var climbing = SEntMan.GetComponent<ClimbingComponent>(SPlayer);
                Assert.Multiple(() =>
                {
                    Assert.That(x, Is.LessThan(1f), $"Попытка {attempt}: не сошёл со стола, x = {x}.");
                    Assert.That(climbing.IsClimbing, Is.False, $"Попытка {attempt}: сошёл со стола, а «залез» остался.");
                    Assert.That(climbing.DisabledFixtureMasks, Is.Empty, $"Попытка {attempt}: коллизия со столами не вернулась.");
                });
            });

            await Client.WaitAssertion(() =>
            {
                var climbing = CEntMan.GetComponent<ClimbingComponent>(CPlayer);
                Assert.That(climbing.IsClimbing, Is.False, $"Попытка {attempt}: у клиента «залез» остался.");
            });

            // Обратно на стартовую клетку — чтобы следующая попытка прыгала с того же места.
            await Server.WaitPost(() =>
            {
                SEntMan.System<SharedTransformSystem>().SetCoordinates(SPlayer, SEntMan.GetCoordinates(PlayerCoords));
                SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().SetLinearVelocity(SPlayer, Vector2.Zero);
            });
            await RunTicks(10);
        }
    }
}
