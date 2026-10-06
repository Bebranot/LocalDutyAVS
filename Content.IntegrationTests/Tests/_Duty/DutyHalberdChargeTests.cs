// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Duty.Weapons.Halberd;
using Content.Shared.Actions;
using Content.Shared.Damage.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: рывок алебардой через настоящий экшен игрока. Рывок предсказывается клиентом, поэтому
/// главное, что здесь крепится, — клиент и сервер проезжают одинаково (иначе у бегущего рывок
/// назад), а стена сбоку не обрывает рывок: раньше он упирался во всё, что касалось хитбокса.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedHalberdChargeSystem))]
public sealed class DutyHalberdChargeTests : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private const string HalberdProto = "DutyBSOHalberd";

    /// <summary>Не больше 5 плиток сверх стартовой — дальше грид получает свою атмосферу (см. DutySlideTests).</summary>
    private const int FloorTiles = 5;

    /// <summary>Дистанция рывка в тесте: полоса пола короткая.</summary>
    private const float TestDistance = 3f;

    /// <summary>Пробег без препятствий: проехал заданную дистанцию, клиент там же, где сервер.</summary>
    [Test]
    public async Task ChargeTravelsAndClientMatchesServer()
    {
        await PrepareFloor();
        await GiveWieldedHalberd();

        var startX = await StartCharge();
        await RunSeconds(1f);

        var serverX = 0f;
        await Server.WaitAssertion(() =>
        {
            serverX = LocalX();
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<HalberdChargingComponent>(SPlayer), Is.False, "Рывок не закончился.");
                Assert.That(serverX - startX, Is.InRange(TestDistance - 0.6f, TestDistance + 0.6f),
                    $"Рывок должен провезти около {TestDistance} тайлов, а провёз {serverX - startX}.");
            });
        });

        await Client.WaitAssertion(() =>
        {
            var clientX = CEntMan.GetComponent<TransformComponent>(CPlayer).LocalPosition.X;
            Assert.That(clientX, Is.EqualTo(serverX).Within(0.25f),
                "Клиент закончил рывок не там, где сервер — у бегущего будет рывок назад.");
        });
    }

    /// <summary>
    /// Стена вдоль пути рывок не обрывает. Целимся под ~7° к стене: тело врезается в неё по
    /// касательной и должно проскользнуть дальше, а не «споткнуться».
    /// </summary>
    [Test]
    public async Task WallAlongsideDoesNotStopCharge()
    {
        await PrepareFloor();
        await Server.WaitPost(() =>
        {
            for (var x = 0; x <= FloorTiles; x++)
                SEntMan.SpawnEntity("WallSolid", MapData.GridCoords.Offset(new Vector2(x + 0.5f, 1.5f)));
        });
        await RunTicks(5);
        await GiveWieldedHalberd();

        var startX = await StartCharge(new Vector2(6f, 1.2f));
        await RunSeconds(1f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(LocalX() - startX, Is.GreaterThan(TestDistance - 0.8f),
                "Стена сбоку остановила рывок — бегущий «спотыкается» о соседнюю стену.");
        });
    }

    /// <summary>Стена впереди: рывок обрывается у неё, бегущий падает.</summary>
    [Test]
    public async Task WallAheadStopsAndKnocksDown()
    {
        await PrepareFloor();
        await GiveWieldedHalberd();

        float wallX = 0;
        await Server.WaitPost(() =>
        {
            wallX = MathF.Floor(LocalX()) + 2f;
            SEntMan.SpawnEntity("WallSolid", MapData.GridCoords.Offset(new Vector2(wallX + 0.5f, 0.5f)));
        });
        await RunTicks(5);

        await StartCharge();
        await RunSeconds(0.6f);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<HalberdChargingComponent>(SPlayer), Is.False, "Рывок не оборвался о стену.");
                Assert.That(SEntMan.HasComponent<KnockedDownComponent>(SPlayer), Is.True, "Удар о стену должен ронять.");
                Assert.That(LocalX(), Is.LessThan(wallX), "Рывок прошёл сквозь стену.");
            });
        });
    }

    /// <summary>Моб на пути получает удар, бегущий остаётся на ногах.</summary>
    [Test]
    public async Task HitsMobAhead()
    {
        await PrepareFloor();

        var target = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            target = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(2.5f, 0.5f)));
        });
        await RunTicks(5);
        await GiveWieldedHalberd();

        var before = await TotalDamage(target);
        await StartCharge();
        await RunSeconds(0.6f);
        var after = await TotalDamage(target);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<HalberdChargingComponent>(SPlayer), Is.False, "Рывок не остановился на цели.");
                Assert.That(after - before, Is.GreaterThan(10f), "Цель рывка не получила урона.");
                Assert.That(SEntMan.HasComponent<KnockedDownComponent>(SPlayer), Is.False, "Попадание в моба не должно ронять бегущего.");
            });
        });
    }

    // ── Помощники ─────────────────────────────────────────────

    private async Task PrepareFloor()
    {
        for (var x = 1; x <= FloorTiles; x++)
        {
            await SetTile(Plating, SEntMan.GetNetCoordinates(MapData.GridCoords.Offset(new Vector2(x + 0.5f, 0.5f))), MapData.Grid);
            await SetTile(Plating, SEntMan.GetNetCoordinates(MapData.GridCoords.Offset(new Vector2(x + 0.5f, 1.5f))), MapData.Grid);
        }

        await SetTile(Plating, SEntMan.GetNetCoordinates(MapData.GridCoords.Offset(new Vector2(0.5f, 1.5f))), MapData.Grid);

        await AddGravity();
        await AddAtmosphere();
        await RunTicks(5);
    }

    private async Task GiveWieldedHalberd()
    {
        var halberd = SEntMan.GetEntity(await PlaceInHands(HalberdProto));

        await Server.WaitPost(() =>
        {
            var charge = SEntMan.GetComponent<HalberdChargeComponent>(halberd);
            charge.ChargeDistance = TestDistance;

            var wieldable = SEntMan.GetComponent<WieldableComponent>(halberd);
            Assert.That(SEntMan.System<SharedWieldableSystem>().TryWield(halberd, wieldable, SPlayer), Is.True,
                "Не получилось взять алебарду в две руки.");
        });
        await RunTicks(5);

        // Клиент тоже должен знать дистанцию: параметры приходят из прототипа, а в тесте мы её меняем.
        await Client.WaitPost(() =>
        {
            var clientHalberd = CEntMan.GetEntity(SEntMan.GetNetEntity(halberd));
            CEntMan.GetComponent<HalberdChargeComponent>(clientHalberd).ChargeDistance = TestDistance;
        });
    }

    /// <summary>Жмёт экшен рывка с клиента (предиктивно, как игрок) и ждёт старта на сервере.</summary>
    private async Task<float> StartCharge(Vector2? aim = null)
    {
        NetEntity action = default;
        NetCoordinates target = default;
        await Server.WaitPost(() =>
        {
            var held = HandSys.GetActiveItem(SPlayer);
            Assert.That(held, Is.Not.Null);
            var actionUid = SEntMan.GetComponent<HalberdChargeComponent>(held!.Value).ChargeActionEntity;
            Assert.That(actionUid, Is.Not.Null, "Экшен рывка не выдан при wield.");
            action = SEntMan.GetNetEntity(actionUid!.Value);
            target = SEntMan.GetNetCoordinates(MapData.GridCoords.Offset(aim ?? new Vector2(20f, 0.5f)));
        });

        await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new RequestPerformActionEvent(action, target)));

        var startX = float.NaN;
        for (var i = 0; i < 10 && float.IsNaN(startX); i++)
        {
            await RunTicks(1);
            await Server.WaitPost(() =>
            {
                if (SEntMan.TryGetComponent<HalberdChargingComponent>(SPlayer, out var charging))
                    startX = charging.Origin.X - SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer).X + LocalX();
            });
        }

        Assert.That(float.IsNaN(startX), Is.False, "Экшен рывка не начал рывок на сервере.");
        return startX;
    }

    private async Task<float> TotalDamage(EntityUid uid)
    {
        var total = 0f;
        await Server.WaitPost(() => total = SEntMan.System<DamageableSystem>().GetTotalDamage(uid).Float());
        return total;
    }

    private float LocalX()
    {
        return SEntMan.GetComponent<TransformComponent>(SPlayer).LocalPosition.X;
    }
}
