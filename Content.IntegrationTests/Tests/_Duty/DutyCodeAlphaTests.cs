// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Duty.CodeAlpha;
using Content.Server.Mind;
using Content.Server.NukeOps;
using Content.Server.Station.Systems;
using Content.Shared._Duty.CodeAlpha;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.AlertLevel;
using Content.Shared.GameTicking;
using Content.Shared.Ghost.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Maps;
using Content.Shared.Mind.Components;
using Content.Shared.NukeOps;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: интеграционные тесты протокола «Альфа» (<see cref="DutyCodeAlphaSystem"/>,
/// <see cref="DutyCodeAlphaCommand"/>, <see cref="SharedDutyCodeAlphaAccessSystem"/>)
/// и его прототипов/локали.
/// </summary>
[TestFixture]
[TestOf(typeof(DutyCodeAlphaSystem))]
public sealed class DutyCodeAlphaTests : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        DummyTicker = true,
        Dirty = true,
    };

    [SidedDependency(Side.Server)] private readonly IGameTiming _timing = null!;
    [SidedDependency(Side.Server)] private readonly ILocalizationManager _loc = null!;
    [SidedDependency(Side.Server)] private readonly IPrototypeManager _proto = null!;
    [SidedDependency(Side.Server)] private readonly AccessReaderSystem _accessReader = null!;
    [SidedDependency(Side.Server)] private readonly AlertLevelSystem _alertLevel = null!;
    [SidedDependency(Side.Server)] private readonly DutyCodeAlphaSystem _alpha = null!;
    [SidedDependency(Side.Server)] private readonly InventorySystem _inventory = null!;
    [SidedDependency(Side.Server)] private readonly MindSystem _minds = null!;
    [SidedDependency(Side.Server)] private readonly SharedHandsSystem _hands = null!;
    [SidedDependency(Side.Server)] private readonly StationSystem _station = null!;

    private async Task<(EntityUid Station, EntityCoordinates Coords)> CreateStationWithGridAsync()
    {
        var testMap = await Pair.CreateTestMap();
        var station = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            var mapProto = _proto.Index<GameMapPrototype>("Saltern");
            station = _station.InitializeNewStation(
                mapProto.Stations["Saltern"],
                [testMap.Grid.Owner]);
        });

        await Pair.RunTicksSync(2);
        return (station, testMap.GridCoords);
    }

    private EntityUid SpawnHumanWithCard(EntityCoordinates coords, string cardProto = "PassengerIDCard")
    {
        var human = SEntMan.SpawnEntity("MobHuman", coords);
        var mind = _minds.CreateMind(null);
        _minds.TransferTo(mind, human);

        var jumpsuit = SEntMan.SpawnEntity("ClothingUniformJumpsuitColorGrey", coords);
        Assert.That(_inventory.TryEquip(human, jumpsuit, "jumpsuit", force: true), Is.True);

        var card = SEntMan.SpawnEntity(cardProto, coords);
        Assert.That(_inventory.TryEquip(human, card, "id", force: true), Is.True);

        return card;
    }

    /// <summary>
    /// _Duty: уровень тревоги alpha существует в прототипах, валиден, его локаль (включая
    /// instructions) не отдаёт сырые ключи, а на пожарном табло есть визуал для alpha.
    /// Регрессия коммитов 3f279b233c, 887ca3b4bd и 2d27706791.
    /// </summary>
    [Test]
    public async Task AlertLevelPrototypeLocaleAndFireAlarmValid()
    {
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    _proto.TryIndex<AlertLevelPrototype>(DutyCodeAlphaVisuals.AlertLevel, out var alphaProto),
                    Is.True,
                    $"Уровень тревоги '{DutyCodeAlphaVisuals.AlertLevel}' из DutyCodeAlphaVisuals.AlertLevel не найден в AlertLevelPrototype.");

                Assert.That(
                    _proto.HasIndex<AlertLevelPrototype>(DutyCodeAlphaVisuals.GreenLevel),
                    Is.True,
                    $"Уровень тревоги '{DutyCodeAlphaVisuals.GreenLevel}' из DutyCodeAlphaVisuals.GreenLevel не найден в AlertLevelPrototype.");

                if (alphaProto != null)
                {
                    Assert.That(alphaProto.Selectable, Is.False, "Уровень Alpha не должен выбираться игроками вручную.");
                    Assert.That(alphaProto.DisableSelection, Is.True, "Уровень Alpha должен блокировать выбор уровня.");
                    Assert.That(alphaProto.ForceEnableEmergencyLights, Is.True, "Уровень Alpha должен включать аварийное освещение.");
                }

                // Регрессия 2d27706791: КПК, панель призрака, борг и ИИ строят ключ alert-level-{level}-instructions.
                var instructionsKey = $"alert-level-{DutyCodeAlphaVisuals.AlertLevel}-instructions";
                Assert.That(_loc.TryGetString(instructionsKey, out var instructions) && instructions != instructionsKey, Is.True,
                    $"Ключ локали '{instructionsKey}' отсутствует или возвращает сырой ключ.");

                // Регрессия 887ca3b4bd: на пожарном табло должен быть визуал для уровня alpha, иначе оно откатывается на green.
                var fireAlarmProto = _proto.Index<EntityPrototype>("FireAlarm");
                Assert.That(fireAlarmProto.TryGetComponent<AlertLevelDisplayComponent>(out var display, SEntMan.ComponentFactory), Is.True);
                Assert.That(display!.AlertVisuals.ContainsKey(DutyCodeAlphaVisuals.AlertLevel), Is.True,
                    $"В FireAlarm.AlertLevelDisplay.alertVisuals отсутствует ключ '{DutyCodeAlphaVisuals.AlertLevel}'.");
            });
        });
    }

    /// <summary>
    /// _Duty: включение вручную (dutycodealpha on) создаёт компонент на станции, переводит
    /// уровень тревоги в alpha и выдаёт доступ на ID-картах.
    /// </summary>
    [Test]
    public async Task ManualActivationCreatesComponentAndGrantsIdAccess()
    {
        var (station, coords) = await CreateStationWithGridAsync();
        EntityUid card = default;

        await Server.WaitPost(() => card = SpawnHumanWithCard(coords));

        await Server.WaitPost(() => Server.ConsoleHost.ExecuteCommand("dutycodealpha on"));
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(_alpha.IsActive, Is.True, "После dutycodealpha on протокол должен быть активен.");
                Assert.That(_alpha.ActiveStation, Is.EqualTo(station));
                Assert.That(SEntMan.HasComponent<DutyCodeAlphaComponent>(station), Is.True,
                    "На станции должен появиться DutyCodeAlphaComponent.");
                Assert.That(SEntMan.HasComponent<DutyCodeAlphaAccessComponent>(card), Is.True,
                    "На ID-карте игрока должен появиться DutyCodeAlphaAccessComponent.");

                var tags = _accessReader.FindAccessTags(card);
                Assert.That(tags, Does.Contain(new ProtoId<AccessLevelPrototype>("Command")),
                    "ID-карта с кодом Альфа должна получать доступ Command.");
                Assert.That(tags, Does.Contain(new ProtoId<AccessLevelPrototype>("Armory")),
                    "ID-карта с кодом Альфа должна получать доступ Armory.");

                Assert.That(_alertLevel.TryGetLevel(station, out var currentLevel), Is.True);
                Assert.That(currentLevel?.Id, Is.EqualTo(DutyCodeAlphaVisuals.AlertLevel),
                    "Уровень тревоги станции должен переключиться на код Альфа.");

                Assert.That(_alpha.Activate(station), Is.False,
                    "Повторный вызов Activate при активном протоколе должен возвращать false.");
            });
        });
    }

    /// <summary>
    /// _Duty: выключение (dutycodealpha off) корректно откатывает состояние, снимает компоненты
    /// со станции и карт, возвращает зелёный уровень и не ругается на станцию.
    /// </summary>
    [Test]
    public async Task DeactivationRollsBackStateCleanly()
    {
        var (station, coords) = await CreateStationWithGridAsync();
        EntityUid card = default;

        await Server.WaitPost(() => card = SpawnHumanWithCard(coords));
        await Server.WaitPost(() => Server.ConsoleHost.ExecuteCommand("dutycodealpha on"));
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(_alpha.IsActive, Is.True);
            Assert.That(SEntMan.HasComponent<DutyCodeAlphaAccessComponent>(card), Is.True);
        });

        await Server.WaitPost(() => Server.ConsoleHost.ExecuteCommand("dutycodealpha off"));
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(_alpha.IsActive, Is.False, "После dutycodealpha off протокол должен быть выключен.");
                Assert.That(_alpha.ActiveStation, Is.Null);
                Assert.That(SEntMan.HasComponent<DutyCodeAlphaComponent>(station), Is.False,
                    "DutyCodeAlphaComponent должен быть снят со станции.");
                Assert.That(SEntMan.HasComponent<DutyCodeAlphaAccessComponent>(card), Is.False,
                    "DutyCodeAlphaAccessComponent должен быть снят с ID-карты.");

                var tags = _accessReader.FindAccessTags(card);
                Assert.That(tags, Does.Not.Contain(new ProtoId<AccessLevelPrototype>("Command")),
                    "После снятия кода Альфа временный доступ Command должен исчезнуть.");

                Assert.That(_alertLevel.TryGetLevel(station, out var currentLevel), Is.True);
                Assert.That(currentLevel?.Id, Is.EqualTo(DutyCodeAlphaVisuals.GreenLevel),
                    "Уровень тревоги должен вернуться на зелёный.");
            });
        });
    }

    /// <summary>
    /// _Duty: объявление и повторное объявление войны не запускает и не перезапускает код Альфа.
    /// Регрессия коммитов 611da961cf, a196c6c5f7 и d9cc57e0c1.
    /// </summary>
    [Test]
    public async Task WarDeclarationDoesNotStartOrRestartCodeAlpha()
    {
        var (station, coords) = await CreateStationWithGridAsync();
        EntityUid declarator = default;

        await Server.WaitPost(() =>
        {
            declarator = SEntMan.SpawnEntity(null, coords);
            var comp = SEntMan.EnsureComponent<WarDeclaratorComponent>(declarator);
            var ev = new WarDeclaredEvent(WarConditionStatus.WarReady, (declarator, comp));
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
        });

        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(_alpha.IsActive, Is.False,
                "Объявление войны не должно автоматически включать код Альфа.");
        });

        // Включаем вручную и проверяем, что повторное объявление войны не сбрасывает таймер.
        await Server.WaitPost(() => Server.ConsoleHost.ExecuteCommand("dutycodealpha on"));
        await Pair.RunTicksSync(5);

        TimeSpan activatedAt = default;
        TimeSpan deadline = default;

        await Server.WaitPost(() =>
        {
            var alpha = SEntMan.GetComponent<DutyCodeAlphaComponent>(station);
            activatedAt = alpha.ActivatedAt;
            deadline = alpha.Deadline;

            var comp = SEntMan.GetComponent<WarDeclaratorComponent>(declarator);
            var ev = new WarDeclaredEvent(WarConditionStatus.WarReady, (declarator, comp));
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
        });

        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            var alpha = SEntMan.GetComponent<DutyCodeAlphaComponent>(station);
            Assert.That(alpha.ActivatedAt, Is.EqualTo(activatedAt));
            Assert.That(alpha.Deadline, Is.EqualTo(deadline));
        });

        // После снятия кода повторное объявление войны также не должно его перезапускать.
        await Server.WaitPost(() =>
        {
            Server.ConsoleHost.ExecuteCommand("dutycodealpha off");
            var comp = SEntMan.GetComponent<WarDeclaratorComponent>(declarator);
            var ev = new WarDeclaredEvent(WarConditionStatus.WarReady, (declarator, comp));
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
        });

        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(_alpha.IsActive, Is.False,
                "Повторное объявление войны не должно перезапускать снятый код Альфа.");
        });
    }

    /// <summary>
    /// _Duty: силиконовые ID и другие исключённые теги (Borg, BasicSilicon, StationAi,
    /// Xenoborg, Ipc, NuclearOperative, CentralCommand) не выдаются кодом Альфа.
    /// Регрессия коммита 571225d624.
    /// </summary>
    [Test]
    public async Task SiliconAndExcludedTagsNotGranted()
    {
        var (_, coords) = await CreateStationWithGridAsync();
        EntityUid card = default;

        await Server.WaitPost(() =>
        {
            card = SEntMan.SpawnEntity("PassengerIDCard", coords);
            SEntMan.AddComponent<DutyCodeAlphaAccessComponent>(card);
        });

        await Server.WaitAssertion(() =>
        {
            var tags = _accessReader.FindAccessTags(card);

            Assert.Multiple(() =>
            {
                Assert.That(tags, Does.Contain(new ProtoId<AccessLevelPrototype>("Maintenance")));
                Assert.That(tags, Does.Contain(new ProtoId<AccessLevelPrototype>("Command")));

                string[] excluded =
                [
                    "Borg",
                    "BasicSilicon",
                    "StationAi",
                    "Xenoborg",
                    "Ipc",
                    "NuclearOperative",
                    "SyndicateAgent",
                    "CentralCommand",
                ];

                foreach (var tag in excluded)
                {
                    Assert.That(tags, Does.Not.Contain(new ProtoId<AccessLevelPrototype>(tag)),
                        $"Исключённый тег '{tag}' не должен выдаваться кодом Альфа.");
                }
            });
        });
    }

    /// <summary>
    /// _Duty: отсчёт кода Альфа (ActivatedAt и Deadline) всегда идёт от момента объявления кода
    /// (+15 минут). Регрессия коммита d9cc57e0c1.
    /// </summary>
    [Test]
    public async Task CountdownStartsFromActivationTime()
    {
        var (station, _) = await CreateStationWithGridAsync();
        TimeSpan beforeActivation = default;
        TimeSpan afterActivation = default;

        await Server.WaitPost(() =>
        {
            beforeActivation = _timing.CurTime;
            Assert.That(_alpha.Activate(station), Is.True);
            afterActivation = _timing.CurTime;
        });

        await Server.WaitAssertion(() =>
        {
            var alpha = SEntMan.GetComponent<DutyCodeAlphaComponent>(station);
            Assert.Multiple(() =>
            {
                Assert.That(alpha.ActivatedAt, Is.InRange(beforeActivation, afterActivation),
                    "ActivatedAt должен фиксировать момент объявления кода.");
                Assert.That(alpha.Deadline - alpha.ActivatedAt, Is.EqualTo(TimeSpan.FromMinutes(15)),
                    "Deadline должен составлять ровно 15 минут от ActivatedAt.");
            });
        });
    }

    /// <summary>
    /// _Duty: карта сначала ищется в слоте id (чужая карта в руках не помечается, если в слоте
    /// есть своя), а оперативники, призраки и существа без разума доступ не получают.
    /// Регрессия коммитов 611da961cf и d9cc57e0c1.
    /// </summary>
    [Test]
    public async Task CardSearchPriorityAndRoleExclusions()
    {
        var (station, coords) = await CreateStationWithGridAsync();

        EntityUid crewSlotCard = default;
        EntityUid crewHeldCard = default;
        EntityUid nukieCard = default;
        EntityUid ghostCard = default;
        EntityUid mindlessCard = default;

        await Server.WaitPost(() =>
        {
            // Обычный член экипажа: своя карта в слоте id и чужая в руке.
            var crew = SEntMan.SpawnEntity("MobHuman", coords);
            var crewMind = _minds.CreateMind(null);
            _minds.TransferTo(crewMind, crew);

            var jumpsuit = SEntMan.SpawnEntity("ClothingUniformJumpsuitColorGrey", coords);
            Assert.That(_inventory.TryEquip(crew, jumpsuit, "jumpsuit", force: true), Is.True);
            crewSlotCard = SEntMan.SpawnEntity("PassengerIDCard", coords);
            Assert.That(_inventory.TryEquip(crew, crewSlotCard, "id", force: true), Is.True);

            crewHeldCard = SEntMan.SpawnEntity("PassengerIDCard", coords);
            Assert.That(_hands.TryPickupAnyHand(crew, crewHeldCard), Is.True);

            // Оперативник с разумом и картой.
            var nukie = SEntMan.SpawnEntity("MobHuman", coords);
            var nukieMind = _minds.CreateMind(null);
            _minds.TransferTo(nukieMind, nukie);
            SEntMan.EnsureComponent<NukeOperativeComponent>(nukie);
            nukieCard = SEntMan.SpawnEntity("PassengerIDCard", coords);
            Assert.That(_hands.TryPickupAnyHand(nukie, nukieCard), Is.True);

            // Призрак с разумом и картой (карту выдаём до добавления GhostComponent, т.к. призраки не могут поднимать предметы).
            var ghost = SEntMan.SpawnEntity("MobHuman", coords);
            var ghostMind = _minds.CreateMind(null);
            _minds.TransferTo(ghostMind, ghost);
            ghostCard = SEntMan.SpawnEntity("PassengerIDCard", coords);
            Assert.That(_hands.TryPickupAnyHand(ghost, ghostCard), Is.True);
            SEntMan.EnsureComponent<GhostComponent>(ghost);

            // Пустое тело без разума (HasMind == false).
            var mindless = SEntMan.SpawnEntity("MobHuman", coords);
            mindlessCard = SEntMan.SpawnEntity("PassengerIDCard", coords);
            Assert.That(_hands.TryPickupAnyHand(mindless, mindlessCard), Is.True);

            Assert.That(_alpha.Activate(station), Is.True);
        });

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<DutyCodeAlphaAccessComponent>(crewSlotCard), Is.True,
                    "Карта в слоте id должна получить доступ.");
                Assert.That(SEntMan.HasComponent<DutyCodeAlphaAccessComponent>(crewHeldCard), Is.False,
                    "Чужая карта в руке не должна помечаться, если есть карта в слоте id.");
                Assert.That(SEntMan.HasComponent<DutyCodeAlphaAccessComponent>(nukieCard), Is.False,
                    "Оперативник не должен получать доступ кода Альфа.");
                Assert.That(SEntMan.HasComponent<DutyCodeAlphaAccessComponent>(ghostCard), Is.False,
                    "Призрак не должен получать доступ кода Альфа.");
                Assert.That(SEntMan.HasComponent<DutyCodeAlphaAccessComponent>(mindlessCard), Is.False,
                    "Существо без разума не должно получать доступ кода Альфа.");
            });
        });
    }

    /// <summary>
    /// _Duty: при перезапуске раунда состояние DutyCodeAlphaSystem сбрасывается.
    /// </summary>
    [Test]
    public async Task RoundRestartResetsState()
    {
        var (station, _) = await CreateStationWithGridAsync();

        await Server.WaitPost(() => Assert.That(_alpha.Activate(station), Is.True));
        await Server.WaitAssertion(() => Assert.That(_alpha.IsActive, Is.True));

        await Server.WaitPost(() => SEntMan.EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent()));

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(_alpha.IsActive, Is.False);
                Assert.That(_alpha.ActiveStation, Is.Null);
            });
        });
    }
}
