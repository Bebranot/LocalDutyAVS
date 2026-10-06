// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Duty.Shuttles;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.CCVar;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// Ивентовый FTL: шаттл долетает до ЦК и обратно до станции, не садится в станцию,
/// а занятая во время полёта точка выхода переносится, а не давит того, кто там оказался.
/// </summary>
[TestFixture]
[TestOf(typeof(DutyEventFtlSystem))]
public sealed class DutyEventFtlTests : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        DummyTicker = true,
        Dirty = true,
    };

    [Test]
    public async Task JumpToCentcommAndBack()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();
        var shuttleSys = server.System<ShuttleSystem>();
        var eventFtl = server.System<DutyEventFtlSystem>();
        var xformSys = server.System<SharedTransformSystem>();

        // ЦК создаётся только в настоящем раунде — как в EvacShuttleTest.
        server.CfgMan.SetCVar(CCVars.EmergencyShuttleEnabled, true);
        server.CfgMan.SetCVar(CCVars.GameDummyTicker, false);
        var gameMap = server.CfgMan.GetCVar(CCVars.GameMap);
        server.CfgMan.SetCVar(CCVars.GameMap, "Saltern");

        await server.WaitPost(() => ticker.RestartRound());
        await pair.RunTicksSync(25);
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));

        var centcommComp = entMan.AllComponentsList<StationCentcommComponent>().Single().Component;
        var centcomm = centcommComp.Entity!.Value;
        var centcommMap = centcommComp.MapEntity!.Value;
        var station = entMan.AllComponentsList<StationDataComponent>().Single().Component.Grids.Single();
        var stationMap = server.Transform(station).MapUid!.Value;

        var testMap = await pair.CreateTestMap();
        var shuttle = testMap.Grid.Owner;
        Assert.That(entMan.HasComponent<ShuttleComponent>(shuttle));

        var flight = shuttleSys.DefaultStartupTime + eventFtl.MinTravelTime + 2f;

        // Станцию и ЦК отправлять нельзя.
        await server.WaitAssertion(() =>
        {
            Assert.That(eventFtl.TryStart(station, DutyEventFtlTarget.Centcomm, eventFtl.MinTravelTime, out _, out _, out _), Is.False);
            Assert.That(eventFtl.TryStart(centcomm, DutyEventFtlTarget.Station, eventFtl.MinTravelTime, out _, out _, out _), Is.False);
        });

        // Туда: к ЦК. По пути в точку выхода «влетает» человек — точка должна переехать.
        EntityUid bystander = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(eventFtl.TryStart(shuttle, DutyEventFtlTarget.Centcomm, eventFtl.MinTravelTime, out var dest, out var anchor, out var error), Is.True, error);
            Assert.That(anchor, Is.EqualTo(centcomm));
            Assert.That(entMan.HasComponent<DutyEventFtlComponent>(shuttle));

            // Повторный прыжок, пока шаттл в пути, — отказ.
            Assert.That(eventFtl.TryStart(shuttle, DutyEventFtlTarget.Station, eventFtl.MinTravelTime, out _, out _, out _), Is.False);

            bystander = entMan.SpawnEntity("MobHuman", xformSys.ToMapCoordinates(dest));
        });

        await pair.RunSeconds(flight);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.Transform(shuttle).MapUid, Is.EqualTo(centcommMap));
            Assert.That(entMan.Deleted(bystander), Is.False, "шаттл раздавил того, кто был в точке выхода");
            AssertClear(entMan, xformSys, shuttle, centcomm);
            Assert.That(ShuttleBox(entMan, xformSys, shuttle).Contains(xformSys.GetWorldPosition(bystander)), Is.False);
        });

        // Ждём конца кулдауна FTL, иначе второй прыжок не заведётся.
        await pair.RunSeconds((float) server.CfgMan.GetCVar(CCVars.FTLCooldown) + 1f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<FTLComponent>(shuttle), Is.False);
            Assert.That(entMan.HasComponent<DutyEventFtlComponent>(shuttle), Is.False);

            // Обратно: к станции.
            Assert.That(eventFtl.TryStart(shuttle, DutyEventFtlTarget.Station, eventFtl.MinTravelTime, out _, out var anchor, out var error), Is.True, error);
            Assert.That(anchor, Is.EqualTo(station));
        });

        await pair.RunSeconds(flight);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.Transform(shuttle).MapUid, Is.EqualTo(stationMap));
            AssertClear(entMan, xformSys, shuttle, station);
        });

        server.CfgMan.SetCVar(CCVars.EmergencyShuttleEnabled, false);
        server.CfgMan.SetCVar(CCVars.GameMap, gameMap);
    }

    private static Box2 ShuttleBox(IEntityManager entMan, SharedTransformSystem xformSys, EntityUid grid)
    {
        return xformSys.GetWorldMatrix(grid).TransformBox(entMan.GetComponent<MapGridComponent>(grid).LocalAABB);
    }

    private static void AssertClear(IEntityManager entMan, SharedTransformSystem xformSys, EntityUid shuttle, EntityUid anchor)
    {
        Assert.That(ShuttleBox(entMan, xformSys, shuttle).Intersects(ShuttleBox(entMan, xformSys, anchor)), Is.False,
            "шаттл вышел из FTL внутри станции");
    }
}
