// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._RMC14.Vehicle;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Shared.Buckle;
using Content.Shared.CombatMode;
using Content.Shared.Construction;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Vehicle.Components;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: самодельный Т-90М. Экипаж сидит на отдельной карте интерьера, поэтому проверяем сквозные цепочки,
/// которые не ловят ни сборка, ни линтер: вход, езда, выстрел наружу, снос стен с предохранителем.
/// </summary>
[TestFixture]
public sealed class DutyTankTests : GameTest
{
    [SidedDependency(Side.Server)] private readonly SharedMapSystem _map = null!;
    [SidedDependency(Side.Server)] private readonly ItemSlotsSystem _slots = null!;
    [SidedDependency(Side.Server)] private readonly SharedContainerSystem _container = null!;
    [SidedDependency(Side.Server)] private readonly SharedInteractionSystem _interaction = null!;
    [SidedDependency(Side.Server)] private readonly SharedBuckleSystem _buckle = null!;
    [SidedDependency(Side.Server)] private readonly SharedTransformSystem _transform = null!;
    [SidedDependency(Side.Server)] private readonly SharedGunSystem _gun = null!;
    [SidedDependency(Side.Server)] private readonly VehicleTurretSystem _turret = null!;
    [SidedDependency(Side.Server)] private readonly SharedCombatModeSystem _combat = null!;

    [SidedDependency(Side.Server)] private readonly SharedVerbSystem _verbs = null!;
    [SidedDependency(Side.Server)] private readonly ConstructionSystem _construction = null!;
    [SidedDependency(Side.Server)] private readonly IPrototypeManager _proto = null!;
    [SidedDependency(Side.Server)] private readonly ILocalizationManager _loc = null!;

    private EntityUid _grid;
    private EntityUid _tank;
    private EntityUid _crew;

    /// <summary>Площадка 41×41, танк в центре со всеми модулями, член экипажа уже внутри.</summary>
    private async Task SetUpTank()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            _grid = map.Grid.Owner;
            for (var x = -20; x <= 20; x++)
            {
                for (var y = -20; y <= 20; y++)
                {
                    _map.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
                }
            }

            _tank = SEntMan.SpawnEntity("DutyVehicleT90M", new EntityCoordinates(_grid, 0.5f, 0.5f));
            Mount("wheel-1", "DutyHardpointT90Tracks");
            Mount("primary", "DutyHardpointT90Turret");
            Mount("armor", "DutyHardpointT90Relikt");
            Mount("secondary", "DutyHardpointT90Kord");

            _crew = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(_grid, 0.5f, 2.8f));
        });

        await RunTicksSync(5);
        await Server.WaitPost(() => _interaction.InteractionActivate(_crew, _tank));
        await RunSeconds(2f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<TransformComponent>(_crew).MapID,
                Is.Not.EqualTo(SEntMan.GetComponent<TransformComponent>(_tank).MapID),
                "После активации люка экипаж должен оказаться на карте интерьера.");
        });
    }

    private void Mount(string slotId, string proto)
    {
        var item = SEntMan.SpawnEntity(proto, new EntityCoordinates(_grid, 0.5f, 5f));
        Assert.That(_slots.TryGetSlot(_tank, slotId, out var slot), Is.True, $"Нет слота {slotId}.");
        Assert.That(_container.Insert(item, slot!.ContainerSlot!), Is.True, $"{proto} не вставился в {slotId}.");
    }

    private EntityUid Seat(string proto)
    {
        var map = SEntMan.GetComponent<TransformComponent>(_crew).MapID;
        var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (xform.MapID == map && meta.EntityPrototype?.ID == proto)
                return uid;
        }

        Assert.Fail($"В интерьере нет {proto}.");
        return default;
    }

    private async Task Sit(string seatProto)
    {
        await Server.WaitAssertion(() =>
        {
            var seat = Seat(seatProto);
            _transform.SetCoordinates(_crew, SEntMan.GetComponent<TransformComponent>(seat).Coordinates);
            Assert.That(_buckle.TryBuckle(_crew, _crew, seat), Is.True, $"Не сел в {seatProto}.");
        });
        await RunTicksSync(10);
    }

    private EntityUid FindProto(EntityUid root, string proto)
    {
        if (!SEntMan.HasComponent<ContainerManagerComponent>(root))
            return default;

        foreach (var container in _container.GetAllContainers(root))
        {
            foreach (var ent in container.ContainedEntities)
            {
                if (SEntMan.GetComponent<MetaDataComponent>(ent).EntityPrototype?.ID == proto)
                    return ent;

                var nested = FindProto(ent, proto);
                if (nested.IsValid())
                    return nested;
            }
        }

        return default;
    }

    private int Ammo(EntityUid gun)
    {
        var ev = new GetAmmoCountEvent();
        SEntMan.EventBus.RaiseLocalEvent(gun, ref ev);
        return ev.Count;
    }

    /// <summary>Выбранное оружие стреляет с карты танка от дула, а не из кресла наводчика.</summary>
    [Test]
    [TestCase("DutyHardpointT90CannonAPFSDS", "DutyBullet125APFSDS", "RMCSeatTankGunner")]
    [TestCase("DutyHardpointT90CannonHEFrag", "DutyBullet125HEFrag", "RMCSeatTankGunner")]
    [TestCase("DutyHardpointT90CannonHEAT", "DutyBullet125HEAT", "RMCSeatTankGunner")]
    [TestCase("DutyHardpointT90PKT", "BulletLightRifle", "RMCSeatTankGunner")]
    [TestCase("DutyHardpointT90Kord", "DutyBulletTank127", "RMCSeatTankCommander")]
    public async Task GunFiresFromTank(string weaponProto, string bulletProto, string seatProto)
    {
        await SetUpTank();
        await Sit(seatProto);

        EntityUid weapon = default;
        await Server.WaitAssertion(() =>
        {
            weapon = FindProto(_tank, weaponProto);
            Assert.That(weapon.IsValid(), Is.True, $"{weaponProto} не установлен.");

            var msg = new VehicleWeaponsSelectMessage(SEntMan.GetNetEntity(weapon))
            {
                Actor = _crew,
                UiKey = VehicleWeaponsUiKey.Key,
            };
            SEntMan.EventBus.RaiseLocalEvent(Seat(seatProto), msg);

            Assert.That(_gun.TryGetGun(_crew, out var selected), Is.True, "Оружие не выбралось.");
            Assert.That(selected.Owner, Is.EqualTo(weapon));
            _combat.SetInCombatMode(_crew, true);
        });
        await RunSeconds(1f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Ammo(weapon), Is.Zero, "Орудие должно собираться без снарядов.");
            _gun.SetBallisticUnspawned((weapon, SEntMan.GetComponent<BallisticAmmoProviderComponent>(weapon)), 5);
            var before = Ammo(weapon);
            Assert.That(before, Is.EqualTo(5));

            Assert.That(_turret.TryGetBarrelWorldRotation(weapon, out var rotation), Is.True);
            var tankMap = _transform.GetMapCoordinates(_tank);
            var target = new MapCoordinates(tankMap.Position + rotation.ToWorldVec() * 10f, tankMap.MapId);

            var gun = (weapon, SEntMan.GetComponent<GunComponent>(weapon));
            Assert.That(_gun.AttemptShoot(_crew, gun, _transform.ToCoordinates(target)), Is.True, "Выстрел не состоялся.");
            Assert.That(Ammo(weapon), Is.EqualTo(before - 1));

            var found = false;
            var query = SEntMan.AllEntityQueryEnumerator<ProjectileComponent, MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var projectile, out var meta, out var xform))
            {
                if (projectile.Weapon != weapon)
                    continue;

                found = true;
                Assert.That(meta.EntityPrototype?.ID, Is.EqualTo(bulletProto));
                Assert.That(xform.MapID, Is.EqualTo(tankMap.MapId), "Снаряд родился не на карте танка.");
                Assert.That((_transform.GetWorldPosition(uid) - tankMap.Position).Length(), Is.LessThan(3f), "Снаряд родился далеко от танка.");
            }

            Assert.That(found, Is.True, "Снаряд не появился.");
        });

        // Свой снаряд не должен взорваться о собственный корпус.
        var hull = 0f;
        await Server.WaitPost(() => hull = SEntMan.GetComponent<HardpointIntegrityComponent>(_tank).Integrity);
        await RunTicksSync(10);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<HardpointIntegrityComponent>(_tank).Integrity, Is.EqualTo(hull), "Танк ранил сам себя."));
    }

    /// <summary>Мехвод ведёт танк: обычная стена сносится, укреплённая и стена у космоса — держат.</summary>
    [Test]
    [TestCase("WallSolid", 6, true)]
    [TestCase("WallReinforced", 6, false)]
    [TestCase("WallSolid", 20, false)]
    public async Task DrivesAndSmashesWalls(string wallProto, int wallDistance, bool smashed)
    {
        await SetUpTank();
        await Sit("RMCSeatTankDriver");

        var walls = new List<EntityUid>();
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<VehicleComponent>(_tank).Operator, Is.EqualTo(_crew), "Мехвод не стал водителем.");

            for (var x = -3; x <= 3; x++)
            {
                walls.Add(SEntMan.SpawnEntity(wallProto, new EntityCoordinates(_grid, x + 0.5f, -wallDistance + 0.5f)));
            }

            var input = SEntMan.GetComponent<InputMoverComponent>(_crew);
#pragma warning disable RA0002
            input.HeldMoveButtons = MoveButtons.Down;
#pragma warning restore RA0002
        });

        await RunSeconds(wallDistance > 10 ? 18f : 9f);

        await Server.WaitAssertion(() =>
        {
            var pos = _transform.GetWorldPosition(_tank);
            Assert.That(pos.Y, Is.LessThan(3.5f - wallDistance), $"Танк не доехал до стены: {pos}.");

            var alive = walls.Count(w => SEntMan.EntityExists(w) && !SEntMan.IsQueuedForDeletion(w));
            if (smashed)
                Assert.That(alive, Is.LessThan(walls.Count), $"Стена не снесена, танк на {pos}.");
            else
                Assert.That(alive, Is.EqualTo(walls.Count), $"Предохранитель не сработал, танк на {pos}.");
        });
    }

    /// <summary>
    /// Alt+клик по люку именно сажает внутрь (а не включает подглядывание), внутри можно ходить, клик по выходу выпускает.
    /// </summary>
    [Test]
    public async Task ClimbInWalkAndExit()
    {
        await SetUpTank();
        await ClickExit();

        await Server.WaitAssertion(() =>
        {
            var top = _verbs.GetLocalVerbs(_tank, _crew, typeof(AlternativeVerb)).First();
            Assert.That(top.Text, Is.EqualTo(_loc.GetString("duty-tank-verb-enter")));
            top.Act!();
        });
        await RunSeconds(2f);

        var start = System.Numerics.Vector2.Zero;
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<TransformComponent>(_crew).MapID,
                Is.Not.EqualTo(SEntMan.GetComponent<TransformComponent>(_tank).MapID));
            start = _transform.GetWorldPosition(_crew);
        });

        await SetMoveButtons(MoveButtons.Right);
        await RunSeconds(0.6f);
        await SetMoveButtons(MoveButtons.None);

        await Server.WaitAssertion(() =>
            Assert.That((_transform.GetWorldPosition(_crew) - start).Length(), Is.GreaterThan(0.5f), "Внутри нельзя ходить."));

        // Возвращаемся к люку, чтобы дотянуться до выхода.
        await SetMoveButtons(MoveButtons.Left);
        await RunSeconds(1f);
        await SetMoveButtons(MoveButtons.None);
        // Выход прерывается движением — ждём, пока персонаж остановится.
        await RunSeconds(1f);

        await ClickExit();
    }

    private Task SetMoveButtons(MoveButtons buttons)
    {
        return Server.WaitPost(() =>
        {
#pragma warning disable RA0002
            SEntMan.GetComponent<InputMoverComponent>(_crew).HeldMoveButtons = buttons;
#pragma warning restore RA0002
        });
    }

    private async Task ClickExit()
    {
        await Server.WaitAssertion(() =>
        {
            var exit = Seat("VehicleExitArea");
            _interaction.UserInteraction(_crew, SEntMan.GetComponent<TransformComponent>(exit).Coordinates, exit);
        });
        await RunSeconds(2f);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<TransformComponent>(_crew).MapID,
                Is.EqualTo(SEntMan.GetComponent<TransformComponent>(_tank).MapID), "Клик по выходу не выпустил наружу."));
    }

    /// <summary>
    /// Каждый переход каждого графа сборки танка доводится до конца: новая сущность появляется, остаётся в графе,
    /// а готовое изделие не прикручено к полу.
    /// </summary>
    [Test]
    public async Task EveryConstructionEdgeProducesEntity()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() => Assert.Multiple(() =>
        {
            foreach (var graph in _proto.EnumeratePrototypes<ConstructionGraphPrototype>())
            {
                if (!graph.ID.StartsWith("DutyT90") && !graph.ID.StartsWith("DutyTank"))
                    continue;

                foreach (var node in graph.Nodes.Values)
                {
                    foreach (var edge in node.Edges)
                    {
                        var target = graph.Nodes[edge.Target];
                        var targetProto = target.Entity.GetId(null, null, new GraphNodeEntityArgs(SEntMan));
                        Assert.That(targetProto, Is.Not.Null, $"{graph.ID}/{edge.Target}: нет сущности.");

                        // Без своего Construction сущность теряет граф, и последний шаг падает, съедая заготовку.
                        var def = _proto.Index<EntityPrototype>(targetProto!);
                        Assert.That(def.TryGetComponent<ConstructionComponent>(out var comp, SEntMan.ComponentFactory), Is.True, $"{targetProto}: нет Construction.");
                        Assert.That((string?) comp?.Graph, Is.EqualTo(graph.ID), $"{targetProto}: чужой граф.");
                        Assert.That(comp?.Node, Is.EqualTo(edge.Target), $"{targetProto}: чужой узел.");

                        if (node.Entity.GetId(null, null, new GraphNodeEntityArgs(SEntMan)) is not { } sourceProto)
                            continue;

                        var before = Entities(targetProto!);
                        var source = SEntMan.SpawnEntity(sourceProto, map.GridCoords);
                        foreach (var action in edge.Completed)
                        {
                            action.PerformAction(source, null, SEntMan);
                        }

                        Assert.That(_construction.ChangeNode(source, null, edge.Target), Is.True, $"{graph.ID}: {node.Name} - {edge.Target}.");

                        var made = Entities(targetProto!).Except(before).ToList();
                        Assert.That(made, Has.Count.EqualTo(1), $"{targetProto} не появился.");
                        foreach (var uid in made)
                        {
                            if (target.Edges.Count == 0 && targetProto != "DutyTankWorkshop")
                                Assert.That(SEntMan.GetComponent<TransformComponent>(uid).Anchored, Is.False, $"{targetProto} прикручен к полу.");

                            SEntMan.DeleteEntity(uid);
                        }

                        SEntMan.DeleteEntity(source);
                    }
                }
            }
        }));
    }

    private HashSet<EntityUid> Entities(string proto)
    {
        var result = new HashSet<EntityUid>();
        var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var uid, out var meta))
        {
            if (meta.EntityPrototype?.ID == proto)
                result.Add(uid);
        }

        return result;
    }
}
