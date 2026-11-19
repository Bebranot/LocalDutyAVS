// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Client._Duty.Neurointerface.Tablet;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Station.Systems;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared._Duty.Neurointerface;
using Content.Shared.AlertLevel;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Implants;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio.Components;
using Content.Shared.Station.Components;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Duty.Neurointerface;

/// <summary>
/// _Duty: интеграционные тесты нейроинтерфейса NT-SEC (маска, планшет, отряды, приказы, таймеры, задачи, статусы бойцов).
/// </summary>
[TestFixture]
public sealed class DutyNeurointerfaceTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [SidedDependency(Side.Server)] private readonly InventorySystem _inventory = default!;
    [SidedDependency(Side.Server)] private readonly AlertLevelSystem _alertLevel = default!;
    [SidedDependency(Side.Server)] private readonly StationSystem _station = default!;
    [SidedDependency(Side.Server)] private readonly DamageableSystem _damageable = default!;
    [SidedDependency(Side.Server)] private readonly MobThresholdSystem _thresholds = default!;
    [SidedDependency(Side.Server)] private readonly MobStateSystem _mobState = default!;
    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;
    [SidedDependency(Side.Server)] private readonly SharedAccessSystem _access = default!;
    [SidedDependency(Side.Client)] private readonly IUserInterfaceManager _uiMan = default!;

    private const string MaskProto = "DutyClothingMaskGasSecurityNeuro";
    private const string TabletProto = "DutyNeuroSquadTablet";
    private static readonly ProtoId<DamageTypePrototype> SlashDamage = "Slash";

    private async Task<EntityUid> SetupStationWithAlertAndBeaconAsync()
    {
        var station = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            station = SEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            SEntMan.EnsureComponent<StationDataComponent>(station);
            SEntMan.EnsureComponent<AlertLevelComponent>(station);
            _station.AddGridToStation(station, MapData.Grid, name: "NT-SEC Test Station");

            SEntMan.SpawnEntity("DefaultStationBeaconArmory", SEntMan.GetCoordinates(PlayerCoords));
        });

        await RunTicks(10);
        return station;
    }

    /// <summary>
    /// Управление с планшета требует доступа Security (AccessReader на прототипе) — выдаём его игроку напрямую.
    /// </summary>
    private async Task GrantSecurityAccessAsync()
    {
        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<AccessComponent>(SPlayer);
            Assert.That(_access.TrySetTags(SPlayer, new ProtoId<AccessLevelPrototype>[] { "Security" }), Is.True);
        });
    }

    private DutyNeuroTabletBuiState GetTabletState(NetEntity tabletNet)
    {
        Assert.That(TryGetBui(DutyNeuroUiKey.Tablet, out var bui, tabletNet), Is.True,
            "BUI планшета DutyNeuroUiKey.Tablet должен быть открыт.");
        Assert.That(bui, Is.InstanceOf<DutyNeuroTabletBoundUserInterface>());
        var neuroBui = (DutyNeuroTabletBoundUserInterface) bui!;
        Assert.That(neuroBui.CurrentState, Is.Not.Null,
            "Состояние DutyNeuroTabletBuiState должно прийти с сервера.");
        return neuroBui.CurrentState!;
    }

    /// <summary>
    /// Надевание маски прикрепляет DutyNeurointerfaceWearerComponent, назначает отряд KILO-9,
    /// определяет локацию и код тревоги станции; снятие маски удаляет компонент.
    /// </summary>
    [Test]
    public async Task MaskEquipLocationAlertAndUnequipTest()
    {
        var station = await SetupStationWithAlertAndBeaconAsync();

        var mask = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            mask = SEntMan.SpawnEntity(MaskProto, SEntMan.GetCoordinates(PlayerCoords));
            Assert.That(_inventory.TryEquip(SPlayer, mask, "mask", force: true), Is.True,
                "Не удалось надеть тактический противогаз с нейроинтерфейсом.");
        });

        await RunTicks(10);
        await RunSeconds(0.6f);

        var initialAlertName = string.Empty;
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<DutyNeurointerfaceWearerComponent>(SPlayer, out var wearer), Is.True,
                "На носителе маски должен появиться DutyNeurointerfaceWearerComponent.");

            Assert.Multiple(() =>
            {
                Assert.That(wearer!.Active, Is.True);
                Assert.That(wearer.SquadCallsign, Is.EqualTo("KILO-9"));
                Assert.That(wearer.OwnerStatus, Is.EqualTo(DutyNeuroMemberStatus.Alive));
                Assert.That(wearer.LocationName, Is.Not.Empty.And.Not.EqualTo("---"),
                    "Должна определиться локация ближайшего маяка NavMap.");
                Assert.That(wearer.AlertCodeName, Is.Not.Empty);
            });

            initialAlertName = wearer!.AlertCodeName;
        });

        await Server.WaitPost(() =>
        {
            _alertLevel.SetLevel(station, "Red", playSound: false, announce: false, force: true);
        });

        await RunTicks(10);
        await RunSeconds(0.6f);

        await Server.WaitAssertion(() =>
        {
            var wearer = SEntMan.GetComponent<DutyNeurointerfaceWearerComponent>(SPlayer);
            var redProto = SProtoMan.Index<AlertLevelPrototype>("Red");

            Assert.Multiple(() =>
            {
                Assert.That(wearer.AlertCodeColor, Is.EqualTo(redProto.Color));
                Assert.That(wearer.AlertCodeName, Is.Not.EqualTo(initialAlertName),
                    "Код тревоги на HUD должен обновиться после смены уровня угрозы на Red.");
            });
        });

        await Server.WaitPost(() =>
        {
            Assert.That(_inventory.TryUnequip(SPlayer, "mask", force: true), Is.True,
                "Не удалось снять маску с носителя.");
        });

        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<DutyNeurointerfaceWearerComponent>(SPlayer), Is.False,
                "После снятия маски DutyNeurointerfaceWearerComponent должен быть удалён.");
        });
    }

    /// <summary>
    /// Открытие BUI планшета, постановка приказов (Hold, Assault, Retreat очищает прошлые),
    /// и отправка SOS срабатывает в радиоканал Security кодом 11-99.
    /// </summary>
    [Test]
    public async Task TabletBuiOrdersAndSecuritySosRadioTest()
    {
        await SetupStationWithAlertAndBeaconAsync();
        await GrantSecurityAccessAsync();

        await Server.WaitPost(() =>
        {
            var mask = SEntMan.SpawnEntity(MaskProto, SEntMan.GetCoordinates(PlayerCoords));
            Assert.That(_inventory.TryEquip(SPlayer, mask, "mask", force: true), Is.True);

            var activeRadio = SEntMan.EnsureComponent<ActiveRadioComponent>(SPlayer);
            activeRadio.Channels.Add("Security");
            SEntMan.EnsureComponent<IntrinsicRadioReceiverComponent>(SPlayer);
            SEntMan.EnsureComponent<TelecomExemptComponent>(SPlayer);
        });

        var tabletNet = await PlaceInHands(TabletProto);
        await RunTicks(10);
        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<TelecomExemptComponent>(SEntMan.GetEntity(tabletNet));
        });

        await UseInHand();
        await RunTicks(15);

        await Client.WaitAssertion(() =>
        {
            _ = GetTabletState(tabletNet);
        });

        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroCreateOrderMessage(DutyNeuroOrderType.Hold, PlayerCoords, "Оружейная"), tabletNet);
        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroCreateOrderMessage(DutyNeuroOrderType.Assault, PlayerCoords, "Бриг"), tabletNet);

        await Client.WaitAssertion(() =>
        {
            var state = GetTabletState(tabletNet);
            Assert.That(state.Orders.Select(o => o.OrderType), Does.Contain(DutyNeuroOrderType.Hold));
            Assert.That(state.Orders.Select(o => o.OrderType), Does.Contain(DutyNeuroOrderType.Assault));
        });

        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroCreateOrderMessage(DutyNeuroOrderType.Retreat, PlayerCoords, "Шаттл"), tabletNet);

        await Client.WaitAssertion(() =>
        {
            var state = GetTabletState(tabletNet);
            Assert.That(state.Orders, Has.Count.EqualTo(1),
                "Приказ Retreat должен очищать предыдущие не-Retreat приказы.");
            Assert.That(state.Orders[0].OrderType, Is.EqualTo(DutyNeuroOrderType.Retreat));
            Assert.That(state.Orders[0].SquadId, Is.EqualTo(state.ActiveSquadId),
                "Приказ должен принадлежать активному отряду планшета.");
        });

        // Retreat уже дал радиосигнал: ждём общий кулдаун callout-ов, иначе сигнал SOS будет отброшен.
        await RunSeconds(3.1f);

        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroCreateOrderMessage(DutyNeuroOrderType.Sos, PlayerCoords, "Мостик"), tabletNet);
        await RunTicks(15);

        await Client.WaitAssertion(() =>
        {
            var chat = _uiMan.GetUIController<ChatUIController>();
            Assert.That(chat.History.Any(entry =>
                    entry.Msg.Channel == ChatChannel.Radio &&
                    entry.Msg.Message.Contains("11-99", StringComparison.Ordinal)),
                Is.True,
                "Приказ SOS должен передавать сигнал 11-99 по каналу Security.");
        });
    }

    /// <summary>
    /// Управление отрядами (создание, приглашение бойца, назначение лидера),
    /// таймерами (создание, старт, пауза, сброс, удаление) и задачами (создание, переключение, удаление).
    /// </summary>
    [Test]
    public async Task SquadManagementTimersAndTasksTest()
    {
        await SetupStationWithAlertAndBeaconAsync();
        await GrantSecurityAccessAsync();

        var mate = EntityUid.Invalid;
        var mateNet = NetEntity.Invalid;
        await Server.WaitPost(() =>
        {
            var mask = SEntMan.SpawnEntity(MaskProto, SEntMan.GetCoordinates(PlayerCoords));
            Assert.That(_inventory.TryEquip(SPlayer, mask, "mask", force: true), Is.True);

            mate = SEntMan.SpawnEntity("MobHuman", SEntMan.GetCoordinates(PlayerCoords));
            mateNet = SEntMan.GetNetEntity(mate);
        });

        var tabletNet = await PlaceInHands(TabletProto);
        await RunTicks(10);
        await UseInHand();
        await RunTicks(15);

        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroCreateSquadMessage("ALPHA-1"), tabletNet);

        var alphaSquadId = string.Empty;
        await Client.WaitAssertion(() =>
        {
            var state = GetTabletState(tabletNet);
            var alpha = state.Squads.FirstOrDefault(s => s.Callsign == "ALPHA-1");
            Assert.That(alpha, Is.Not.Null, "Новый отряд ALPHA-1 должен появиться в списке.");
            alphaSquadId = alpha!.SquadId;
        });

        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroSelectSquadMessage(alphaSquadId), tabletNet);
        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroInviteMemberMessage(alphaSquadId, Player), tabletNet);
        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroInviteMemberMessage(alphaSquadId, mateNet), tabletNet);
        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroSetLeaderMessage(alphaSquadId, mateNet), tabletNet);

        await Client.WaitAssertion(() =>
        {
            var state = GetTabletState(tabletNet);
            var alpha = state.Squads.First(s => s.SquadId == alphaSquadId);
            Assert.That(alpha.Members.Any(m => m.Entity == mateNet && m.IsLeader), Is.True,
                "Приглашённый боец должен стать лидером отряда ALPHA-1.");
        });

        // Таймеры: создание -> старт -> пауза -> сброс -> удаление
        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroCreateTimerMessage("Штурм", 30), tabletNet);

        var timerId = 0;
        await Client.WaitAssertion(() =>
        {
            var state = GetTabletState(tabletNet);
            var timer = state.Timers.FirstOrDefault(t => t.Title == "Штурм");
            Assert.That(timer, Is.Not.Null);
            timerId = timer!.TimerId;
        });

        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroTimerActionMessage(timerId, DutyNeuroTimerAction.Start), tabletNet);
        await Client.WaitAssertion(() =>
        {
            var state = GetTabletState(tabletNet);
            Assert.That(state.Timers.First(t => t.TimerId == timerId).Status, Is.EqualTo(DutyNeuroTimerStatus.Running));
        });

        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroTimerActionMessage(timerId, DutyNeuroTimerAction.Pause), tabletNet);
        await Client.WaitAssertion(() =>
        {
            var state = GetTabletState(tabletNet);
            Assert.That(state.Timers.First(t => t.TimerId == timerId).Status, Is.EqualTo(DutyNeuroTimerStatus.Paused));
        });

        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroTimerActionMessage(timerId, DutyNeuroTimerAction.Reset), tabletNet);
        await Client.WaitAssertion(() =>
        {
            var state = GetTabletState(tabletNet);
            Assert.That(state.Timers.First(t => t.TimerId == timerId).Status, Is.EqualTo(DutyNeuroTimerStatus.Stopped));
        });

        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroTimerActionMessage(timerId, DutyNeuroTimerAction.Delete), tabletNet);
        await Client.WaitAssertion(() =>
        {
            var state = GetTabletState(tabletNet);
            Assert.That(state.Timers.Any(t => t.TimerId == timerId), Is.False);
        });

        // Задачи: создание -> переключение -> удаление
        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroCreateTaskMessage("Зачистка", "Проверить отсек", "Офицер", DutyNeuroTaskPriority.High), tabletNet);

        var taskId = 0;
        await Client.WaitAssertion(() =>
        {
            var state = GetTabletState(tabletNet);
            var task = state.Tasks.FirstOrDefault(t => t.Title == "Зачистка");
            Assert.That(task, Is.Not.Null);
            Assert.That(task!.Completed, Is.False);
            Assert.That(task.Priority, Is.EqualTo(DutyNeuroTaskPriority.High));
            taskId = task.TaskId;
        });

        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroToggleTaskMessage(taskId), tabletNet);
        await Client.WaitAssertion(() =>
        {
            var state = GetTabletState(tabletNet);
            Assert.That(state.Tasks.First(t => t.TaskId == taskId).Completed, Is.True);
        });

        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroDeleteTaskMessage(taskId), tabletNet);
        await Client.WaitAssertion(() =>
        {
            var state = GetTabletState(tabletNet);
            Assert.That(state.Tasks.Any(t => t.TaskId == taskId), Is.False);
        });
    }

    /// <summary>
    /// Критическое состояние создаёт запись ТРЕКЕР и статус Critical;
    /// смерть фиксирует DeathRoundTime на погибшем и убирает его из списка бойцов HUD у соотрядцев.
    /// </summary>
    [Test]
    public async Task CriticalAndDeadMobStateUpdatesTrackerAndSquadRosterTest()
    {
        await SetupStationWithAlertAndBeaconAsync();

        var mate = EntityUid.Invalid;
        var mateNet = NetEntity.Invalid;
        await Server.WaitPost(() =>
        {
            var playerMask = SEntMan.SpawnEntity(MaskProto, SEntMan.GetCoordinates(PlayerCoords));
            Assert.That(_inventory.TryEquip(SPlayer, playerMask, "mask", force: true), Is.True);
            _implants.AddImplant(SPlayer, "TrackingImplant");
            SEntMan.EnsureComponent<TelecomExemptComponent>(SPlayer);

            mate = SEntMan.SpawnEntity("MobHuman", SEntMan.GetCoordinates(PlayerCoords));
            mateNet = SEntMan.GetNetEntity(mate);
            var mateMask = SEntMan.SpawnEntity(MaskProto, SEntMan.GetCoordinates(PlayerCoords));
            Assert.That(_inventory.TryEquip(mate, mateMask, "mask", force: true), Is.True);

            _implants.AddImplant(mate, "TrackingImplant");
            SEntMan.EnsureComponent<TelecomExemptComponent>(mate);
        });

        await RunTicks(10);
        await RunSeconds(0.6f);

        FixedPoint2 critThreshold = default;
        FixedPoint2 deadThreshold = default;

        await Server.WaitAssertion(() =>
        {
            var mateWearer = SEntMan.GetComponent<DutyNeurointerfaceWearerComponent>(mate);
            var playerEntry = mateWearer.SquadMembers.FirstOrDefault(m => m.Entity == Player);
            Assert.That(playerEntry, Is.Not.Null, "Живой соотрядец с активной сессией должен быть в SquadMembers у напарника.");
            Assert.That(playerEntry!.Status, Is.EqualTo(DutyNeuroMemberStatus.Alive));
        });

        await Server.WaitPost(() =>
        {
            Assert.That(_thresholds.TryGetThresholdForState(SPlayer, MobState.Critical, out var maybeCrit), Is.True);
            Assert.That(_thresholds.TryGetDeadThreshold(SPlayer, out var maybeDead), Is.True);
            critThreshold = maybeCrit!.Value;
            deadThreshold = maybeDead!.Value;

            var slashProto = SProtoMan.Index(SlashDamage);
            _damageable.TryChangeDamage(SPlayer, new DamageSpecifier(slashProto, critThreshold + FixedPoint2.New(5)), ignoreResistances: true);
        });

        await RunTicks(10);
        await RunSeconds(0.6f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(_mobState.IsCritical(SPlayer), Is.True, "Игрок должен находиться в критическом состоянии.");

            var playerWearer = SEntMan.GetComponent<DutyNeurointerfaceWearerComponent>(SPlayer);
            var mateWearer = SEntMan.GetComponent<DutyNeurointerfaceWearerComponent>(mate);

            Assert.Multiple(() =>
            {
                Assert.That(playerWearer.OwnerStatus, Is.EqualTo(DutyNeuroMemberStatus.Critical));
                Assert.That(mateWearer.Trackers.Any(t => t.Entity == Player), Is.True,
                    "Критическое состояние носителя трекера должно создать запись в Trackers у соотрядцев.");

                var playerEntry = mateWearer.SquadMembers.FirstOrDefault(m => m.Entity == Player);
                Assert.That(playerEntry, Is.Not.Null);
                Assert.That(playerEntry!.Status, Is.EqualTo(DutyNeuroMemberStatus.Critical));
            });
        });

        await Server.WaitPost(() =>
        {
            var slashProto = SProtoMan.Index(SlashDamage);
            var extra = (deadThreshold - critThreshold) + FixedPoint2.New(10);
            _damageable.TryChangeDamage(SPlayer, new DamageSpecifier(slashProto, extra), ignoreResistances: true);
        });

        await RunTicks(10);
        await RunSeconds(0.6f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(_mobState.IsDead(SPlayer), Is.True, "Игрок должен умереть.");

            var playerWearer = SEntMan.GetComponent<DutyNeurointerfaceWearerComponent>(SPlayer);
            var mateWearer = SEntMan.GetComponent<DutyNeurointerfaceWearerComponent>(mate);

            Assert.Multiple(() =>
            {
                Assert.That(playerWearer.OwnerStatus, Is.EqualTo(DutyNeuroMemberStatus.Dead));
                Assert.That(playerWearer.DeathRoundTime, Is.Not.Null,
                    "На погибшем носителе должно быть зафиксировано DeathRoundTime для экрана flatline.");
                Assert.That(mateWearer.SquadMembers.Any(m => m.Entity == Player), Is.False,
                    "Погибший боец должен исчезать из списка бойцов HUD у живых соотрядцев.");
            });
        });

        await Server.WaitPost(() =>
        {
            Assert.That(_thresholds.TryGetThresholdForState(mate, MobState.Critical, out var maybeMateCrit), Is.True);
            var slashProto = SProtoMan.Index(SlashDamage);
            _damageable.TryChangeDamage(mate, new DamageSpecifier(slashProto, maybeMateCrit!.Value + FixedPoint2.New(5)), ignoreResistances: true);
        });

        await RunTicks(10);
        await RunSeconds(0.6f);

        await Server.WaitAssertion(() =>
        {
            var playerWearer = SEntMan.GetComponent<DutyNeurointerfaceWearerComponent>(SPlayer);
            Assert.That(playerWearer.Trackers.Any(t => t.Entity == mateNet), Is.True,
                "Критическое состояние напарника должно отображаться в Trackers.");
        });
    }

    /// <summary>
    /// Без доступа Security планшет не управляет приказами; приказы видны только своему отряду;
    /// убранный из отряда носитель не возвращается в него автоматически.
    /// </summary>
    [Test]
    public async Task AccessSquadScopedOrdersAndRemoveMemberTest()
    {
        await SetupStationWithAlertAndBeaconAsync();

        await Server.WaitPost(() =>
        {
            var mask = SEntMan.SpawnEntity(MaskProto, SEntMan.GetCoordinates(PlayerCoords));
            Assert.That(_inventory.TryEquip(SPlayer, mask, "mask", force: true), Is.True);
        });

        var tabletNet = await PlaceInHands(TabletProto);
        await RunTicks(10);
        await UseInHand();
        await RunTicks(15);

        // Без доступа приказ не создаётся.
        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroCreateOrderMessage(DutyNeuroOrderType.Hold, PlayerCoords, "Оружейная"), tabletNet);
        await Client.WaitAssertion(() =>
        {
            Assert.That(GetTabletState(tabletNet).Orders, Is.Empty,
                "Без доступа Security планшет не должен создавать приказы.");
        });

        await GrantSecurityAccessAsync();

        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroCreateOrderMessage(DutyNeuroOrderType.Hold, PlayerCoords, "[color=red]Оружейная[/color]"), tabletNet);

        var kiloSquadId = string.Empty;
        await Client.WaitAssertion(() =>
        {
            var state = GetTabletState(tabletNet);
            kiloSquadId = state.ActiveSquadId;
            Assert.That(state.Orders, Has.Count.EqualTo(1));
            Assert.That(state.Orders[0].ZoneName, Is.EqualTo("Оружейная"),
                "Разметка из клиентской зоны должна вырезаться сервером.");
        });

        // Приказ другого отряда не виден ни на планшете с KILO-9, ни у носителя из KILO-9.
        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroCreateSquadMessage("BRAVO-2"), tabletNet);
        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroCreateOrderMessage(DutyNeuroOrderType.Assault, PlayerCoords, "Бриг"), tabletNet);
        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroSelectSquadMessage(kiloSquadId), tabletNet);

        await Client.WaitAssertion(() =>
        {
            var state = GetTabletState(tabletNet);
            Assert.That(state.Orders.Select(o => o.OrderType), Is.EquivalentTo(new[] { DutyNeuroOrderType.Hold }));
        });

        await Server.WaitAssertion(() =>
        {
            var wearer = SEntMan.GetComponent<DutyNeurointerfaceWearerComponent>(SPlayer);
            Assert.That(wearer.Orders.Select(o => o.OrderType), Is.EquivalentTo(new[] { DutyNeuroOrderType.Hold }),
                "Носитель должен видеть только приказы своего отряда.");
        });

        // Убираем носителя из отряда и ждём несколько плановых пересчётов: он не должен вернуться.
        await SendBui(DutyNeuroUiKey.Tablet, new DutyNeuroRemoveMemberMessage(kiloSquadId, Player), tabletNet);
        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            var wearer = SEntMan.GetComponent<DutyNeurointerfaceWearerComponent>(SPlayer);
            Assert.Multiple(() =>
            {
                Assert.That(wearer.SquadCallsign, Is.Not.EqualTo("KILO-9"));
                Assert.That(wearer.SquadMembers, Is.Empty);
                Assert.That(wearer.Orders, Is.Empty);
            });
        });

        await Client.WaitAssertion(() =>
        {
            var state = GetTabletState(tabletNet);
            var kilo = state.Squads.First(s => s.SquadId == kiloSquadId);
            Assert.That(kilo.Members.Any(m => m.Entity == Player), Is.False,
                "Убранный боец не должен автоматически возвращаться в отряд.");
        });
    }
}
