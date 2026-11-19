// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Duty.Concussion;
using Content.Shared._Duty.Concussion;
using Content.Shared.Alert;
using Content.Shared.CCVar;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: тесты контузии (Concussion): наличие компонента у всех играбельных рас, ленивое
/// затухание шкалы, детекция выстрелов с фолл-офом/радиусом/затуханием за стеной, масштабирование
/// и кап взрывов, защита слуха (head/ears/mask), CVar и алерт-полоска (включая скрытие через 5с).
/// Клиентские визуально-звуковые эффекты (ConcussionOverlay, звон, occlusion) в headless-тесте не проверяются.
/// </summary>
[TestFixture]
[TestOf(typeof(ConcussionSystem))]
public sealed class DutyConcussionTests : GameTest
{
    [SidedDependency(Side.Server)] private readonly ConcussionSystem _concussion = null!;
    [SidedDependency(Side.Server)] private readonly InventorySystem _inventory = null!;
    [SidedDependency(Side.Server)] private readonly SharedHandsSystem _hands = null!;
    [SidedDependency(Side.Server)] private readonly AlertsSystem _alerts = null!;

    private const string GunProto = "WeaponPistolMk58";
    private const string HelmetProto = "ClothingHeadHelmetBasic";
    private const string BombHelmetProto = "ClothingHeadHelmetBombSuit";

    /// <summary>Все играбельные расы должны иметь ConcussionComponent (через BaseMobSpecies).</summary>
    [Test]
    public async Task EveryPlayableSpeciesHasConcussion()
    {
        var protoMan = Server.ResolveDependency<IPrototypeManager>();
        var compFactory = Server.ResolveDependency<IComponentFactory>();

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var species in protoMan.EnumeratePrototypes<SpeciesPrototype>())
                {
                    // Синтетики (IPC) не имеют ушей и ConcussionComponent по дизайну (см. README.md).
                    if (!species.RoundStart || species.ID == "IPC")
                        continue;

                    Assert.That(protoMan.TryIndex(species.Prototype, out var mob), Is.True,
                        $"Раса {species.ID} ссылается на несуществующий прототип {species.Prototype}.");

                    if (mob is null)
                        continue;

                    Assert.That(mob.TryGetComponent<ConcussionComponent>(out _, compFactory), Is.True,
                        $"У расы {species.ID} нет ConcussionComponent.");
                }
            });
        });
    }

    /// <summary>Шкала лениво затухает со скоростью DecayPerSecond (4/сек) и ограничивается [0, MaxLevel].</summary>
    [Test]
    public async Task LazyDecayAndClamping()
    {
        var human = await Spawn("MobHuman");

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<ConcussionComponent>(human);

            _concussion.AddRaw(human, 150f, comp);
            Assert.That(_concussion.GetCurrentLevel(human, comp), Is.EqualTo(comp.MaxLevel).Within(0.01f),
                "Шкала не должна превышать MaxLevel.");

            comp.Level = 10f;
        });

        await RunSeconds(1f);

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<ConcussionComponent>(human);
            // За 1 секунду должно списаться ~4 единицы (с небольшим допуском на тики).
            Assert.That(_concussion.GetCurrentLevel(human, comp), Is.InRange(5.5f, 6.5f),
                "Затухание за 1с не соответствует DecayPerSecond = 4.");
        });

        await RunSeconds(2f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(_concussion.GetCurrentLevel(human), Is.EqualTo(0f).Within(0.001f),
                "Шкала не должна опускаться ниже нуля.");
        });
    }

    /// <summary>
    /// Выстрел в пределах ShotRange даёт прирост с линейным фолл-офом (не ниже ShotMinFalloff),
    /// за пределами ShotRange и за непростреливаемой стеной прирост равен нулю.
    /// </summary>
    [Test]
    public async Task GunShotAppliesFalloffAndRespectsRangeAndWalls()
    {
        var mapData = await Pair.CreateTestMap();
        var gridCoords = mapData.GridCoords;
        EntityUid human = default;
        EntityUid gun = default;

        await Server.WaitAssertion(() =>
        {
            human = SEntMan.SpawnEntity("MobHuman", gridCoords);
            gun = SEntMan.SpawnEntity(GunProto, gridCoords);
            var comp = SEntMan.GetComponent<ConcussionComponent>(human);
            var emptyAmmo = new List<(EntityUid? Uid, IShootable Shootable)>();

            // В эпицентре (dist = 0) -> полный ShotAmount (0.5).
            var evZero = new GunShotEvent(gun, emptyAmmo, gridCoords, gridCoords);
            SEntMan.EventBus.RaiseLocalEvent(gun, ref evZero);
            Assert.That(_concussion.GetCurrentLevel(human, comp), Is.EqualTo(0.5f).Within(0.02f));

            // На половине радиуса (dist = 2.5) -> falloff = 0.5 -> +0.25.
            comp.Level = 0f;
            var halfCoords = gridCoords.Offset(new Vector2(2.5f, 0f));
            var evHalf = new GunShotEvent(gun, emptyAmmo, halfCoords, gridCoords);
            SEntMan.EventBus.RaiseLocalEvent(gun, ref evHalf);
            Assert.That(_concussion.GetCurrentLevel(human, comp), Is.EqualTo(0.25f).Within(0.02f));

            // На самом краю (dist = 4.9) -> зажато снизу ShotMinFalloff = 0.15 -> +0.075.
            comp.Level = 0f;
            var edgeCoords = gridCoords.Offset(new Vector2(4.9f, 0f));
            var evEdge = new GunShotEvent(gun, emptyAmmo, edgeCoords, gridCoords);
            SEntMan.EventBus.RaiseLocalEvent(gun, ref evEdge);
            Assert.That(_concussion.GetCurrentLevel(human, comp), Is.EqualTo(0.5f * 0.15f).Within(0.02f));

            // За пределами ShotRange (dist = 6) -> 0.
            comp.Level = 0f;
            var farCoords = gridCoords.Offset(new Vector2(6f, 0f));
            var evFar = new GunShotEvent(gun, emptyAmmo, farCoords, gridCoords);
            SEntMan.EventBus.RaiseLocalEvent(gun, ref evFar);
            Assert.That(_concussion.GetCurrentLevel(human, comp), Is.EqualTo(0f).Within(0.001f));

            // Стена между выстрелом (x=2.5) и мобом (x=0.5) на x=1.5 -> преграда глушит в ноль.
            // Кладём тайлы пола под стену и точку выстрела, чтобы WallSolid закрепился на сетке.
            var mapSys = SEntMan.System<SharedMapSystem>();
            var tileMan = Server.ResolveDependency<ITileDefinitionManager>();
            var plating = new Tile(tileMan["Plating"].TileId);
            mapSys.SetTile(mapData.Grid, new Vector2i(0, 0), plating);
            mapSys.SetTile(mapData.Grid, new Vector2i(1, 0), plating);
            mapSys.SetTile(mapData.Grid, new Vector2i(2, 0), plating);

            comp.Level = 0f;
            SEntMan.System<SharedTransformSystem>().SetCoordinates(human, new EntityCoordinates(mapData.Grid, new Vector2(0.5f, 0.5f)));
            var wallCoords = new EntityCoordinates(mapData.Grid, new Vector2(1.5f, 0.5f));
            SEntMan.SpawnEntity("WallSolid", wallCoords);
        });

        // Даём физике/окклюдеру проиндексировать стену для рейкаста InRangeUnOccluded.
        await RunSeconds(0.1f);

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<ConcussionComponent>(human);
            var emptyAmmo = new List<(EntityUid? Uid, IShootable Shootable)>();
            var targetCoords = new EntityCoordinates(mapData.Grid, new Vector2(0.5f, 0.5f));
            var behindWallCoords = new EntityCoordinates(mapData.Grid, new Vector2(2.5f, 0.5f));
            var evWall = new GunShotEvent(gun, emptyAmmo, behindWallCoords, targetCoords);
            SEntMan.EventBus.RaiseLocalEvent(gun, ref evWall);
            Assert.That(_concussion.GetCurrentLevel(human, comp), Is.EqualTo(0f).Within(0.001f),
                "Стена между выстрелом и целью должна глушить контузию.");
        });
    }

    /// <summary>Взрыв масштабирует прирост от урона (30 при 40 урона), упирается в кап 60 и игнорирует <= 0.</summary>
    [Test]
    public async Task ExplosionConcussionScalesAndCaps()
    {
        var human = await Spawn("MobHuman");

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<ConcussionComponent>(human);

            _concussion.ApplyExplosionConcussion(human, 0f);
            Assert.That(_concussion.GetCurrentLevel(human, comp), Is.EqualTo(0f));

            _concussion.ApplyExplosionConcussion(human, 40f);
            Assert.That(_concussion.GetCurrentLevel(human, comp), Is.EqualTo(30f).Within(0.05f),
                "Эталонный урон 40 должен давать BlastAmount = 30.");

            comp.Level = 0f;
            _concussion.ApplyExplosionConcussion(human, 200f);
            Assert.That(_concussion.GetCurrentLevel(human, comp), Is.EqualTo(60f).Within(0.05f),
                "Прирост от одного взрыва не должен превышать BlastMaxAmount = 60.");
        });
    }

    /// <summary>
    /// Защита слуха сканирует слоты head/ears/mask, берёт максимальный Reduction (0.85 у шлема,
    /// 0.95 у сапёрного шлема), игнорирует Enabled == false и предметы в руках.
    /// </summary>
    [Test]
    public async Task HearingProtectionReducesConcussionAndTakesMax()
    {
        var human = await Spawn("MobHuman");
        var helmet = await Spawn(HelmetProto);
        var bombHelmet = await Spawn(BombHelmetProto);

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<ConcussionComponent>(human);

            // Шлем в руках (не надет в head/ears/mask) не защищает.
            Assert.That(_hands.TryPickupAnyHand(human, helmet), Is.True);
            Assert.That(_concussion.GetProtection(human), Is.EqualTo(0f));

            // Обычный шлем на голове -> 0.85, взрыв 40 урона даёт 30 * 0.15 = 4.5.
            Assert.That(_inventory.TryEquip(human, helmet, "head", force: true), Is.True);
            Assert.That(_concussion.GetProtection(human), Is.EqualTo(0.85f).Within(0.001f));
            _concussion.ApplyExplosionConcussion(human, 40f);
            Assert.That(_concussion.GetCurrentLevel(human, comp), Is.EqualTo(4.5f).Within(0.1f));

            // Выключенная защита (откинутое забрало) -> 0.
            var prot = SEntMan.GetComponent<ConcussionProtectionComponent>(helmet);
            prot.Enabled = false;
            Assert.That(_concussion.GetProtection(human), Is.EqualTo(0f));

            // Сапёрный шлем -> 0.95, взрыв 40 урона даёт 30 * 0.05 = 1.5.
            comp.Level = 0f;
            Assert.That(_inventory.TryUnequip(human, "head", force: true), Is.True);
            Assert.That(_inventory.TryEquip(human, bombHelmet, "head", force: true), Is.True);
            Assert.That(_concussion.GetProtection(human), Is.EqualTo(0.95f).Within(0.001f));
            _concussion.ApplyExplosionConcussion(human, 40f);
            Assert.That(_concussion.GetCurrentLevel(human, comp), Is.EqualTo(1.5f).Within(0.1f));
        });
    }

    /// <summary>CVar duty.concussion_enabled = false полностью отключает набор контузии.</summary>
    [Test]
    public async Task DisabledCVarPreventsConcussion()
    {
        var cfg = Server.ResolveDependency<IConfigurationManager>();
        var mapData = await Pair.CreateTestMap();
        var gridCoords = mapData.GridCoords;

        await Server.WaitAssertion(() =>
        {
            var human = SEntMan.SpawnEntity("MobHuman", gridCoords);
            var gun = SEntMan.SpawnEntity(GunProto, gridCoords);
            try
            {
                cfg.SetCVar(DutyCCVars.ConcussionEnabled, false);

                _concussion.ApplyExplosionConcussion(human, 40f);
                var ev = new GunShotEvent(gun, new List<(EntityUid?, IShootable)>(), gridCoords, gridCoords);
                SEntMan.EventBus.RaiseLocalEvent(gun, ref ev);

                Assert.That(_concussion.GetCurrentLevel(human), Is.EqualTo(0f),
                    "При выключенном CVar контузия не должна накапливаться.");
            }
            finally
            {
                cfg.SetCVar(DutyCCVars.ConcussionEnabled, true);
            }
        });
    }

    /// <summary>
    /// Полоска алерта отображается с правильным severity (0..3) и скрывается после 5 секунд
    /// на нулевом уровне (< MinVisibleLevel).
    /// </summary>
    [Test]
    public async Task AlertSeverityAndClearAfterFiveSecondsAtZero()
    {
        var human = await Spawn("MobHuman");

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<ConcussionComponent>(human);

            _concussion.ApplyExplosionConcussion(human, 20f); // ~15% -> severity 0
            Assert.That(_alerts.IsShowingAlert(human, comp.Alert), Is.True);
            Assert.That(comp.ShownSeverity, Is.EqualTo((short) 0));

            _concussion.ApplyExplosionConcussion(human, 30f); // ~37% -> severity 1
            Assert.That(comp.ShownSeverity, Is.EqualTo((short) 1));

            _concussion.ApplyExplosionConcussion(human, 40f); // ~67% -> severity 2
            Assert.That(comp.ShownSeverity, Is.EqualTo((short) 2));

            _concussion.ApplyExplosionConcussion(human, 60f); // 100% -> severity 3
            Assert.That(comp.ShownSeverity, Is.EqualTo((short) 3));

            // Обнуляем шкалу, чтобы не ждать 25 секунд полного затухания — проверяем 5с задержку скрытия.
            comp.Level = 0f;
        });

        await RunSeconds(1f);
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<ConcussionComponent>(human);
            Assert.That(_alerts.IsShowingAlert(human, comp.Alert), Is.True,
                "Алерт не должен пропадать раньше 5 секунд на нуле.");
        });

        await RunSeconds(5.1f);
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<ConcussionComponent>(human);
            Assert.That(_alerts.IsShowingAlert(human, comp.Alert), Is.False,
                "После 5 секунд на нуле алерт должен скрыться.");
            Assert.That(comp.ShownSeverity, Is.Null);
        });
    }
}
