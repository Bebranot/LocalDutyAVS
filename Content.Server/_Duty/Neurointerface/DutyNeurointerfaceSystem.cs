// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using Content.Server.AlertLevel;
using Content.Server.GameTicking;
using Content.Server.Pinpointer;
using Content.Shared._Duty.Neurointerface;
using Content.Shared.Access.Systems;
using Content.Shared.ADT.Medical.SuitSensors;
using Content.Shared.AlertLevel;
using Content.Shared.Clothing;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Implants.Components;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Roles.Jobs;
using Content.Shared.Station;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Duty.Neurointerface;

/// <summary>
/// _Duty: основная серверная система тактического нейроинтерфейса СБ (HUD, маска, планшет, трекеры, статусы).
/// </summary>
/// <remarks>
/// Нагрузка: все события лишь ставят флаг <see cref="_dirty"/>, а пересчёт делается в <see cref="Update"/>
/// не чаще раза за тик. Дорогие данные бойцов (поиск ближайшего маяка, должность, здоровье) считаются
/// один раз на пересчёт в <see cref="_memberEntries"/> и переиспользуются всеми носителями и планшетами.
/// </remarks>
public sealed partial class DutyNeurointerfaceSystem : EntitySystem
{
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private AlertLevelSystem _alertLevel = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private DutyNeuroSquadManagerSystem _squads = default!;
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _mobThresholds = default!;
    [Dependency] private NavMapSystem _navMap = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private SharedJobSystem _job = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStationSystem _station = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    /// <summary>
    /// Плановый пересчёт: локации и здоровье меняются без событий, поэтому опрашиваем их с этим шагом.
    /// </summary>
    private const float SyncInterval = 0.5f;

    /// <summary>
    /// Список экипажа для приглашения — самый дорогой (все гуманоиды станции), а меняется редко.
    /// </summary>
    private const float CandidatesInterval = 2f;

    private const string MaskSlotName = "mask";
    private const string TrackingImplantPrototypeId = "TrackingImplant";

    private EntityQuery<DutyNeurointerfaceWearerComponent> _wearerQuery;
    private EntityQuery<DutyNeurointerfaceMaskComponent> _maskQuery;
    private EntityQuery<ImplantedComponent> _implantedQuery;
    private EntityQuery<SubdermalImplantComponent> _subdermalQuery;
    private EntityQuery<ADTSuitSensorComponent> _suitSensorQuery;
    private EntityQuery<DamageableComponent> _damageableQuery;
    private EntityQuery<HumanoidProfileComponent> _humanoidQuery;
    private EntityQuery<MobStateComponent> _mobStateQuery;
    private EntityQuery<AlertLevelComponent> _alertQuery;

    private float _syncAccumulator;
    private float _candidatesAccumulator;
    private bool _dirty;

    // Кэши одного пересчёта. Списки, которые уходят в BUI-состояние, создаются заново на каждый пересчёт
    // и после этого не меняются: состояние сериализуется позже, мутировать их нельзя.
    private readonly Dictionary<EntityUid, DutyNeuroSquadMemberEntry> _memberEntries = new();
    private readonly Dictionary<string, SquadView> _squadViews = new();
    private readonly Dictionary<EntityUid, (string Name, Color Color)> _alertCache = new();
    private readonly Dictionary<CandidateKey, List<DutyNeuroCrewCandidate>> _candidateCache = new();

    // Буферы для сравнения с текущим состоянием носителя: новая копия списка создаётся только при изменении.
    private readonly List<DutyNeuroSquadMemberEntry> _hudMembersBuffer = new();

    private static readonly List<DutyNeuroTrackerEntry> EmptyTrackers = new();
    private static readonly List<DutyNeuroOrderEntry> EmptyOrders = new();

    /// <summary>
    /// Сводка отряда на один пересчёт.
    /// </summary>
    private sealed class SquadView
    {
        public readonly List<DutyNeuroSquadMemberEntry> Members = new();
        public readonly List<DutyNeuroTrackerEntry> Trackers = new();
        public readonly List<DutyNeuroOrderEntry> Orders = new();
        public int AliveCount;
    }

    private readonly record struct CandidateKey(EntityUid Station, MapId Map, NetEntity? TargetGrid);

    public override void Initialize()
    {
        base.Initialize();

        _wearerQuery = GetEntityQuery<DutyNeurointerfaceWearerComponent>();
        _maskQuery = GetEntityQuery<DutyNeurointerfaceMaskComponent>();
        _implantedQuery = GetEntityQuery<ImplantedComponent>();
        _subdermalQuery = GetEntityQuery<SubdermalImplantComponent>();
        _suitSensorQuery = GetEntityQuery<ADTSuitSensorComponent>();
        _damageableQuery = GetEntityQuery<DamageableComponent>();
        _humanoidQuery = GetEntityQuery<HumanoidProfileComponent>();
        _mobStateQuery = GetEntityQuery<MobStateComponent>();
        _alertQuery = GetEntityQuery<AlertLevelComponent>();

        SubscribeLocalEvent<DutyNeurointerfaceMaskComponent, ClothingGotEquippedEvent>(OnMaskEquipped);
        SubscribeLocalEvent<DutyNeurointerfaceMaskComponent, ClothingGotUnequippedEvent>(OnMaskUnequipped);

        SubscribeLocalEvent<DutyNeuroMedicRequestActionEvent>(OnMedicRequestAction);
        SubscribeLocalEvent<DutyNeuroAcknowledgeOrderActionEvent>(OnAcknowledgeOrderAction);

        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<AlertLevelChangedEvent>(OnAlertLevelChanged);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);

        SubscribeLocalEvent<DutyNeurointerfaceTabletComponent, MapInitEvent>(OnTabletMapInit);

        Subs.BuiEvents<DutyNeurointerfaceTabletComponent>(DutyNeuroUiKey.Tablet, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnTabletOpened);
            subs.Event<DutyNeuroCreateOrderMessage>(OnCreateOrder);
            subs.Event<DutyNeuroRemoveOrderMessage>(OnRemoveOrder);
            subs.Event<DutyNeuroClearOrdersMessage>(OnClearOrders);
            subs.Event<DutyNeuroSelectSquadMessage>(OnSelectSquad);
            subs.Event<DutyNeuroCreateSquadMessage>(OnCreateSquad);
            subs.Event<DutyNeuroRenameSquadMessage>(OnRenameSquad);
            subs.Event<DutyNeuroDisbandSquadMessage>(OnDisbandSquad);
            subs.Event<DutyNeuroInviteMemberMessage>(OnInviteMember);
            subs.Event<DutyNeuroRemoveMemberMessage>(OnRemoveMember);
            subs.Event<DutyNeuroSetLeaderMessage>(OnSetLeader);
            subs.Event<DutyNeuroSendCalloutMessage>(OnSendCallout);
            subs.Event<DutyNeuroCreateTimerMessage>(OnCreateTimer);
            subs.Event<DutyNeuroTimerActionMessage>(OnTimerAction);
            subs.Event<DutyNeuroCreateTaskMessage>(OnCreateTask);
            subs.Event<DutyNeuroToggleTaskMessage>(OnToggleTask);
            subs.Event<DutyNeuroDeleteTaskMessage>(OnDeleteTask);
            subs.Event<DutyNeuroRebootNetworkMessage>(OnRebootNetwork);
        });

        InitializeCyber();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _candidatesAccumulator += frameTime;
        if (_candidatesAccumulator >= CandidatesInterval)
        {
            _candidatesAccumulator = 0f;
            // Ленивая пересборка: список построится заново, только если какой-то планшет открыт.
            _candidateCache.Clear();
        }

        _syncAccumulator += frameTime;
        if (!_dirty && _syncAccumulator < SyncInterval)
            return;

        _dirty = false;
        _syncAccumulator = 0f;

        _squads.CleanupDeletedEntities();
        PruneNetworkAlerts();
        RebuildCaches();
        RefreshAllWearers();
        RefreshOpenTablets();

        // Кэш нужен только внутри пересчёта; держать устаревшие записи между тиками незачем.
        _memberEntries.Clear();
        _squadViews.Clear();
        _alertCache.Clear();
    }

    /// <summary>
    /// Просит пересчитать HUD и планшеты на ближайшем тике (один раз, сколько бы событий ни пришло).
    /// </summary>
    private void RequestRefresh(bool candidatesChanged = false)
    {
        _dirty = true;
        if (candidatesChanged)
            _candidateCache.Clear();
    }

    #region Mask Equip / Unequip

    // Маска — один из источников нейроинтерфейса (второй — киберимплант): вся логика подключения в AddSource/RemoveSource.
    private void OnMaskEquipped(Entity<DutyNeurointerfaceMaskComponent> ent, ref ClothingGotEquippedEvent args)
    {
        AddSource(args.Wearer, ent.Owner, ent.Comp.Network);
    }

    private void OnMaskUnequipped(Entity<DutyNeurointerfaceMaskComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        RemoveSource(args.Wearer, ent.Owner);
    }

    private bool HasNeuroMaskEquipped(EntityUid wearer)
    {
        return _inventory.TryGetSlotEntity(wearer, MaskSlotName, out var equippedMask) &&
               _maskQuery.HasComp(equippedMask.Value);
    }

    #endregion

    #region Actions & Callouts

    private void OnMedicRequestAction(DutyNeuroMedicRequestActionEvent args)
    {
        if (args.Handled || !_wearerQuery.TryComp(args.Performer, out var wearer) || _mobState.IsDead(args.Performer))
            return;

        // Погашенная ЭМИ панель не передаёт ничего — как и мёртвая рация.
        if (wearer.Jammed)
        {
            _popup.PopupEntity(Loc.GetString("duty-neuro-jammed-popup"), args.Performer, args.Performer);
            return;
        }

        if (TrySendCallout(args.Performer, DutyNeuroCalloutType.Medic))
            args.Handled = true;
    }

    private void OnAcknowledgeOrderAction(DutyNeuroAcknowledgeOrderActionEvent args)
    {
        if (args.Handled || !_wearerQuery.TryComp(args.Performer, out var wearer) || _mobState.IsDead(args.Performer))
            return;

        if (wearer.Jammed)
        {
            _popup.PopupEntity(Loc.GetString("duty-neuro-jammed-popup"), args.Performer, args.Performer);
            return;
        }

        if (TrySendCallout(args.Performer, DutyNeuroCalloutType.Acknowledge))
            args.Handled = true;
    }

    /// <summary>
    /// Отправляет callout с зоной, вычисленной на сервере: клиентский текст зоны в рацию не попадает.
    /// </summary>
    private bool TrySendCallout(EntityUid sender, DutyNeuroCalloutType type)
    {
        if (_squads.IsCalloutOnCooldown(sender))
        {
            _popup.PopupEntity(Loc.GetString("duty-neuro-callout-cooldown"), sender, sender);
            return false;
        }

        return _squads.SendSecurityCallout(sender, type, GetLocationName(sender), GetNetwork(sender));
    }

    #endregion

    #region MobState, AlertLevel & Session Events

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        var target = args.Target;
        var trackerV2 = _trackerV2Query.HasComp(target);
        if (!trackerV2 && !_wearerQuery.HasComp(target) && !_squads.IsSquadMember(target))
            return;

        // «Призрак в сети» глушит и автоматику трекера: в эфир о нём ничего не уходит.
        if (IsNeuroGhost(target))
        {
            RequestRefresh();
            return;
        }

        // Статусы носителя выставит ResolveOwnerLifecycle на ближайшем пересчёте; здесь — только мгновенное
        // радиооповещение, которое не должно ждать тика. Кулдаун оповещения общий для отряда и трекера v2 —
        // двойного сообщения не будет.
        if (args.NewMobState is MobState.SoftCritical or MobState.Critical)
            _squads.TrySendCriticalTrackerAlert(target, GetMemberDisplayName(target), GetLocationName(target), GetNetwork(target));
        else if (args.NewMobState == MobState.Dead && trackerV2)
            SendTrackerV2DeathAlert(target);

        RequestRefresh();
    }

    private void OnAlertLevelChanged(ref AlertLevelChangedEvent args)
    {
        RequestRefresh();
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        // Онлайн-статус важен только бойцам отрядов и носителям; остальные подключения не трогаем.
        if (_wearerQuery.HasComp(args.Entity) || _squads.IsSquadMember(args.Entity))
            RequestRefresh();
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        if (_wearerQuery.HasComp(args.Entity) || _squads.IsSquadMember(args.Entity))
            RequestRefresh();
    }

    #endregion

    #region Tablet BUI

    private void OnTabletMapInit(Entity<DutyNeurointerfaceTabletComponent> ent, ref MapInitEvent args)
    {
        SyncTabletStationInfo(ent);
    }

    private void OnTabletOpened(Entity<DutyNeurointerfaceTabletComponent> ent, ref BoundUIOpenedEvent args)
    {
        _squads.EnsureDefaultSquad();
        SyncTabletStationInfo(ent);
        EnsureActiveSquad(ent);
        RequestRefresh();
    }

    /// <summary>
    /// Управлять отрядами может только обладатель доступа СБ (AccessReader на прототипе планшета).
    /// Выбор отряда и callout-ы доступны всем: они ничего не меняют у других.
    /// </summary>
    private bool CheckManageAccess(EntityUid tablet, EntityUid actor)
    {
        if (_access.IsAllowed(actor, tablet))
            return true;

        _popup.PopupEntity(Loc.GetString("duty-neuro-tablet-access-denied"), tablet, actor);
        return false;
    }

    /// <summary>
    /// Планшет — инструмент NT-SEC: отряды тактической сети Горлекса через него не видны и не управляются
    /// (иначе подделанным сообщением можно было бы разогнать отряд оперативников).
    /// </summary>
    private bool IsTabletSquad(string squadId)
    {
        return _squads.TryGetSquad(squadId, out var squad) && squad.Network == DutyNeuroNetwork.NtSec;
    }

    private string EnsureActiveSquad(Entity<DutyNeurointerfaceTabletComponent> ent)
    {
        if (IsTabletSquad(ent.Comp.ActiveSquadId))
            return ent.Comp.ActiveSquadId;

        ent.Comp.ActiveSquadId = _squads.GetFallbackSquadId();
        Dirty(ent);
        return ent.Comp.ActiveSquadId;
    }

    private void OnCreateOrder(Entity<DutyNeurointerfaceTabletComponent> ent, ref DutyNeuroCreateOrderMessage msg)
    {
        if (!CheckManageAccess(ent.Owner, msg.Actor))
            return;

        var squadId = EnsureActiveSquad(ent);
        var zone = SanitizeZoneName(msg.ZoneName);
        if (zone.Length == 0)
            zone = GetLocationName(GetCoordinates(msg.Coordinates));

        if (_squads.CreateOrder(squadId, msg.OrderType, msg.Coordinates, zone, msg.Actor, GetMemberDisplayName(msg.Actor)) == null)
            return;

        RequestRefresh();
    }

    private void OnRemoveOrder(Entity<DutyNeurointerfaceTabletComponent> ent, ref DutyNeuroRemoveOrderMessage msg)
    {
        if (!CheckManageAccess(ent.Owner, msg.Actor))
            return;

        if (!_squads.RemoveOrder(EnsureActiveSquad(ent), msg.OrderId))
            return;

        RequestRefresh();
    }

    private void OnClearOrders(Entity<DutyNeurointerfaceTabletComponent> ent, ref DutyNeuroClearOrdersMessage msg)
    {
        if (!CheckManageAccess(ent.Owner, msg.Actor))
            return;

        if (!_squads.ClearOrders(EnsureActiveSquad(ent)))
            return;

        RequestRefresh();
    }

    private void OnSelectSquad(Entity<DutyNeurointerfaceTabletComponent> ent, ref DutyNeuroSelectSquadMessage msg)
    {
        if (!IsTabletSquad(msg.SquadId))
            return;

        ent.Comp.ActiveSquadId = msg.SquadId;
        Dirty(ent);
        RequestRefresh();
    }

    private void OnCreateSquad(Entity<DutyNeurointerfaceTabletComponent> ent, ref DutyNeuroCreateSquadMessage msg)
    {
        if (!CheckManageAccess(ent.Owner, msg.Actor))
            return;

        var created = _squads.CreateSquad(msg.Callsign);
        if (created == null)
            return;

        ent.Comp.ActiveSquadId = created.SquadId;
        Dirty(ent);
        RequestRefresh();
    }

    private void OnRenameSquad(Entity<DutyNeurointerfaceTabletComponent> ent, ref DutyNeuroRenameSquadMessage msg)
    {
        if (!IsTabletSquad(msg.SquadId) || !CheckManageAccess(ent.Owner, msg.Actor))
            return;

        if (!_squads.RenameSquad(msg.SquadId, msg.NewCallsign))
            return;

        RequestRefresh(candidatesChanged: true);
    }

    private void OnDisbandSquad(Entity<DutyNeurointerfaceTabletComponent> ent, ref DutyNeuroDisbandSquadMessage msg)
    {
        if (!IsTabletSquad(msg.SquadId) || !CheckManageAccess(ent.Owner, msg.Actor))
            return;

        if (!_squads.DisbandSquad(msg.SquadId))
            return;

        EnsureActiveSquad(ent);
        RequestRefresh(candidatesChanged: true);
    }

    /// <summary>
    /// В отряд можно звать только живых гуманоидов — те же критерии, что и в списке экипажа,
    /// иначе подделанным сообщением можно «завербовать» любой предмет.
    /// </summary>
    private bool IsValidSquadTarget(EntityUid target)
    {
        return !TerminatingOrDeleted(target) &&
               _humanoidQuery.HasComp(target) &&
               _mobStateQuery.HasComp(target);
    }

    private void OnInviteMember(Entity<DutyNeurointerfaceTabletComponent> ent, ref DutyNeuroInviteMemberMessage msg)
    {
        if (!IsTabletSquad(msg.SquadId) || !CheckManageAccess(ent.Owner, msg.Actor))
            return;

        var target = GetEntity(msg.TargetEntity);
        if (!IsValidSquadTarget(target))
            return;

        // Боец чужой сети (имплант Горлекса) в отряд NT не встаёт: его HUD всё равно отряд не покажет,
        // а соотрядцы NT получили бы обводку оперативника.
        if (_wearerQuery.TryComp(target, out var targetWearer) && targetWearer.Network != DutyNeuroNetwork.NtSec)
        {
            _popup.PopupEntity(Loc.GetString("duty-neuro-tablet-foreign-network"), ent.Owner, msg.Actor);
            return;
        }

        if (!_squads.InviteMember(msg.SquadId, target))
            return;

        RequestRefresh(candidatesChanged: true);
    }

    private void OnRemoveMember(Entity<DutyNeurointerfaceTabletComponent> ent, ref DutyNeuroRemoveMemberMessage msg)
    {
        if (!IsTabletSquad(msg.SquadId) || !CheckManageAccess(ent.Owner, msg.Actor))
            return;

        var target = GetEntity(msg.TargetEntity);
        if (!_squads.RemoveMember(msg.SquadId, target))
            return;

        RequestRefresh(candidatesChanged: true);
    }

    private void OnSetLeader(Entity<DutyNeurointerfaceTabletComponent> ent, ref DutyNeuroSetLeaderMessage msg)
    {
        if (!IsTabletSquad(msg.SquadId) || !CheckManageAccess(ent.Owner, msg.Actor))
            return;

        var target = GetEntity(msg.TargetEntity);
        if (!IsValidSquadTarget(target))
            return;

        if (_wearerQuery.TryComp(target, out var targetWearer) && targetWearer.Network != DutyNeuroNetwork.NtSec)
            return;

        if (!_squads.SetLeader(msg.SquadId, target))
            return;

        RequestRefresh(candidatesChanged: true);
    }

    private void OnSendCallout(Entity<DutyNeurointerfaceTabletComponent> ent, ref DutyNeuroSendCalloutMessage msg)
    {
        // msg.ZoneName намеренно игнорируется: клиент мог прислать что угодно, а текст уходит в рацию от имени игрока.
        if (_mobState.IsDead(msg.Actor))
            return;

        TrySendCallout(msg.Actor, msg.CalloutType);
    }

    private void OnCreateTimer(Entity<DutyNeurointerfaceTabletComponent> ent, ref DutyNeuroCreateTimerMessage msg)
    {
        if (!CheckManageAccess(ent.Owner, msg.Actor))
            return;

        _squads.CreateTimer(msg.Title, msg.Seconds);
        RequestRefresh();
    }

    private void OnTimerAction(Entity<DutyNeurointerfaceTabletComponent> ent, ref DutyNeuroTimerActionMessage msg)
    {
        if (!CheckManageAccess(ent.Owner, msg.Actor))
            return;

        if (!_squads.HandleTimerAction(msg.TimerId, msg.Action))
            return;

        RequestRefresh();
    }

    private void OnCreateTask(Entity<DutyNeurointerfaceTabletComponent> ent, ref DutyNeuroCreateTaskMessage msg)
    {
        if (!CheckManageAccess(ent.Owner, msg.Actor))
            return;

        if (_squads.CreateTask(msg.Title, msg.Description, msg.AssigneeName, msg.Priority) == null)
            return;

        RequestRefresh();
    }

    private void OnToggleTask(Entity<DutyNeurointerfaceTabletComponent> ent, ref DutyNeuroToggleTaskMessage msg)
    {
        if (!CheckManageAccess(ent.Owner, msg.Actor))
            return;

        if (!_squads.ToggleTask(msg.TaskId))
            return;

        RequestRefresh();
    }

    private void OnDeleteTask(Entity<DutyNeurointerfaceTabletComponent> ent, ref DutyNeuroDeleteTaskMessage msg)
    {
        if (!CheckManageAccess(ent.Owner, msg.Actor))
            return;

        if (!_squads.DeleteTask(msg.TaskId))
            return;

        RequestRefresh();
    }

    #endregion

    #region State Building & Sync

    /// <summary>
    /// Считает записи всех бойцов и сводки отрядов один раз на пересчёт.
    /// </summary>
    private void RebuildCaches()
    {
        _memberEntries.Clear();
        _squadViews.Clear();
        _alertCache.Clear();

        foreach (var squad in _squads.GetAllSquads())
        {
            var view = new SquadView();

            foreach (var member in squad.Members)
            {
                if (TerminatingOrDeleted(member))
                    continue;

                var netMember = GetNetEntity(member);
                var name = GetMemberDisplayName(member);

                // «Призрак в сети»: имя в составе отряда остаётся (его знают), а всё остальное — «НЕТ СИГНАЛА».
                // Offline — чтобы старые клиенты и логика «живых» его не учитывали; трекера нет.
                if (IsNeuroGhost(member))
                {
                    var ghostEntry = new DutyNeuroSquadMemberEntry(
                        netMember,
                        name,
                        GetJobAndDepartment(member).JobTitle,
                        DutyNeuroMemberStatus.Offline,
                        NoSignalText,
                        0f,
                        false,
                        squad.Leader == member,
                        NoSignal: true);

                    _memberEntries[member] = ghostEntry;
                    view.Members.Add(ghostEntry);
                    continue;
                }

                var status = GetMemberStatus(member);
                var location = GetLocationName(member);

                var entry = new DutyNeuroSquadMemberEntry(
                    netMember,
                    name,
                    GetJobAndDepartment(member).JobTitle,
                    status,
                    location,
                    GetHealthPercent(member),
                    HasTrackingImplant(member),
                    squad.Leader == member);

                _memberEntries[member] = entry;
                view.Members.Add(entry);

                if (status is DutyNeuroMemberStatus.Alive or DutyNeuroMemberStatus.Critical)
                    view.AliveCount++;

                // Трекер смотрит на сам MobState, а не на статус: офлайн-боец в крите тоже нуждается в помощи.
                if (_mobState.IsCritical(member))
                    view.Trackers.Add(new DutyNeuroTrackerEntry(netMember, name, location));
            }

            _squads.CollectOrders(squad.SquadId, view.Orders);
            _squadViews[squad.SquadId] = view;
        }

        RebuildNetworkTrackers();
    }

    private void RefreshAllWearers()
    {
        var query = EntityQueryEnumerator<DutyNeurointerfaceWearerComponent>();
        while (query.MoveNext(out var uid, out var wearer))
        {
            UpdateWearerState(uid, wearer);
        }
    }

    private void UpdateWearerState(EntityUid uid, DutyNeurointerfaceWearerComponent wearer)
    {
        // Отряд не выдаётся автоматически: боец, которого убрали из отряда, должен остаться без него.
        // Отряд чужой сети не показываем: сети NT-SEC и Горлекса друг друга не видят.
        SquadView? view = null;
        string callsign;
        if (_squads.TryGetMemberSquad(uid, out var squad) &&
            squad.Network == wearer.Network &&
            _squadViews.TryGetValue(squad.SquadId, out view))
        {
            callsign = squad.Callsign;
        }
        else
        {
            callsign = Loc.GetString("duty-neuro-squad-none");
        }

        var intrusionChanged = ExpireIntrusion(wearer);

        var (ownerStatus, statusText, deathTime) = ResolveOwnerLifecycle(uid, wearer.DeathRoundTime);
        var location = _memberEntries.TryGetValue(uid, out var selfEntry) ? selfEntry.Location : GetLocationName(uid);
        var (alertName, alertColor) = GetStationAlertLevel(uid);

        _hudMembersBuffer.Clear();
        if (view != null)
        {
            var selfNet = GetNetEntity(uid);
            foreach (var entry in view.Members)
            {
                // Мёртвые скрываются из списка у других участников отряда. Офлайн (SSD, тело без игрока) остаются:
                // их нужно обводить сквозь стены, а из строк HUD их убирает уже клиент.
                if (entry.Entity != selfNet && entry.Status == DutyNeuroMemberStatus.Dead)
                    continue;

                _hudMembersBuffer.Add(entry);
            }
        }

        // В Orders уходят все приказы отряда (до MaxActiveOrders): world-маркеры рисуют все,
        // а HUD сам берёт последние три.
        // К данным отряда подмешиваются данные всей сети (трекер v2, тревожная кнопка) и ложная метка вторжения.
        var trackers = ComposeTrackers(wearer, view?.Trackers ?? EmptyTrackers);
        var orders = ComposeOrders(wearer, view?.Orders ?? EmptyOrders);

        var membersChanged = !ListEquals(wearer.SquadMembers, _hudMembersBuffer);
        var trackersChanged = !ListEquals(wearer.Trackers, trackers);
        var ordersChanged = !ListEquals(wearer.Orders, orders);

        var changed = membersChanged || trackersChanged || ordersChanged || intrusionChanged ||
                      !wearer.Active ||
                      wearer.SquadCallsign != callsign ||
                      wearer.LocationName != location ||
                      wearer.StatusText != statusText ||
                      wearer.OwnerStatus != ownerStatus ||
                      wearer.AlertCodeName != alertName ||
                      wearer.AlertCodeColor != alertColor ||
                      wearer.DeathRoundTime != deathTime;

        if (!changed)
            return;

        wearer.Active = true;
        wearer.SquadCallsign = callsign;
        wearer.LocationName = location;
        wearer.StatusText = statusText;
        wearer.OwnerStatus = ownerStatus;
        wearer.AlertCodeName = alertName;
        wearer.AlertCodeColor = alertColor;
        wearer.DeathRoundTime = deathTime;

        // Своя копия на компонент: общие списки пересчёта не должны жить в состоянии нескольких сущностей.
        if (membersChanged)
            wearer.SquadMembers = new List<DutyNeuroSquadMemberEntry>(_hudMembersBuffer);
        if (trackersChanged)
            wearer.Trackers = new List<DutyNeuroTrackerEntry>(trackers);
        if (ordersChanged)
            wearer.Orders = new List<DutyNeuroOrderEntry>(orders);

        Dirty(uid, wearer);
    }

    private static bool ListEquals<T>(List<T> a, List<T> b)
    {
        if (a.Count != b.Count)
            return false;

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < a.Count; i++)
        {
            if (!comparer.Equals(a[i], b[i]))
                return false;
        }

        return true;
    }

    private (DutyNeuroMemberStatus Status, string Text, TimeSpan? DeathTime) ResolveOwnerLifecycle(
        EntityUid uid,
        TimeSpan? existingDeathTime)
    {
        if (_mobState.IsDead(uid))
            return (DutyNeuroMemberStatus.Dead, Loc.GetString("duty-neuro-status-dead"), existingDeathTime ?? _gameTicker.RoundDuration());

        if (_mobState.IsCritical(uid))
            return (DutyNeuroMemberStatus.Critical, Loc.GetString("duty-neuro-status-crit"), null);

        return (DutyNeuroMemberStatus.Alive, Loc.GetString("duty-neuro-status-alive"), null);
    }

    private void RefreshOpenTablets()
    {
        // Общие для всех планшетов части состояния строятся лениво и один раз на пересчёт.
        List<DutyNeuroSquadSummary>? summaries = null;
        List<DutyNeuroTimerEntry>? timers = null;
        List<DutyNeuroTaskEntry>? tasks = null;

        var query = EntityQueryEnumerator<DutyNeurointerfaceTabletComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!_ui.IsUiOpen(uid, DutyNeuroUiKey.Tablet))
                continue;

            EntityUid? actor = null;
            foreach (var candidate in _ui.GetActors(uid, DutyNeuroUiKey.Tablet))
            {
                actor = candidate;
                break;
            }

            summaries ??= BuildSquadSummaries();
            timers ??= _squads.GetTimers();
            tasks ??= _squads.GetTasks();

            PushTabletState((uid, comp), actor, summaries, timers, tasks);
        }
    }

    private List<DutyNeuroSquadSummary> BuildSquadSummaries()
    {
        var result = new List<DutyNeuroSquadSummary>();
        foreach (var squad in _squads.GetAllSquads())
        {
            // Планшет NT-SEC отряды Горлекса не видит.
            if (squad.Network != DutyNeuroNetwork.NtSec)
                continue;

            if (!_squadViews.TryGetValue(squad.SquadId, out var view))
                continue;

            string leaderName;
            if (squad.Leader is { } leader && _memberEntries.TryGetValue(leader, out var leaderEntry))
                leaderName = leaderEntry.Name;
            else if (squad.Leader is { } rawLeader && !TerminatingOrDeleted(rawLeader))
                leaderName = GetMemberDisplayName(rawLeader);
            else
                leaderName = "---";

            result.Add(new DutyNeuroSquadSummary(
                squad.SquadId,
                squad.Callsign,
                view.AliveCount,
                view.Members.Count,
                leaderName,
                view.Members));
        }

        return result;
    }

    private void PushTabletState(
        Entity<DutyNeurointerfaceTabletComponent> ent,
        EntityUid? actor,
        List<DutyNeuroSquadSummary> summaries,
        List<DutyNeuroTimerEntry> timers,
        List<DutyNeuroTaskEntry> tasks)
    {
        var activeSquadId = EnsureActiveSquad(ent);
        SyncTabletStationInfo(ent);

        var refEntity = actor ?? ent.Owner;
        var (alertName, alertColor) = GetStationAlertLevel(refEntity);
        var ownerLoc = _memberEntries.TryGetValue(refEntity, out var refEntry)
            ? refEntry.Location
            : GetLocationName(refEntity);

        var userCallsign = string.Empty;
        var isUserLeader = false;
        if (actor != null && _squads.TryGetMemberSquad(actor.Value, out var userSquad))
        {
            userCallsign = userSquad.Callsign;
            isUserLeader = userSquad.Leader == actor.Value;
        }
        else if (_squads.TryGetSquad(activeSquadId, out var activeSquad))
        {
            userCallsign = activeSquad.Callsign;
        }

        // На планшете — только приказы активного отряда: им он и управляет.
        var orders = _squadViews.TryGetValue(activeSquadId, out var activeView)
            ? activeView.Orders
            : new List<DutyNeuroOrderEntry>();

        var state = new DutyNeuroTabletBuiState(
            ent.Comp.StationName,
            ent.Comp.TargetGrid,
            activeSquadId,
            actor != null ? GetNetEntity(actor.Value) : null,
            userCallsign,
            isUserLeader,
            ownerLoc,
            alertName,
            alertColor,
            summaries,
            orders,
            timers,
            tasks,
            GetCrewCandidates(ent));

        state.NetworkIntrusion = IsSquadUnderIntrusion(activeSquadId);

        _ui.SetUiState(ent.Owner, DutyNeuroUiKey.Tablet, state);
    }

    private List<DutyNeuroCrewCandidate> GetCrewCandidates(Entity<DutyNeurointerfaceTabletComponent> tablet)
    {
        var tabletMap = _transform.GetMapId(tablet.Owner);
        var tabletStation = _station.GetOwningStation(tablet.Owner) ?? _station.GetStationInMap(tabletMap);

        // Планшеты одной станции видят один и тот же экипаж — считаем его один раз на всех.
        var key = new CandidateKey(
            tabletStation ?? EntityUid.Invalid,
            tabletStation == null ? tabletMap : MapId.Nullspace,
            tablet.Comp.TargetGrid);

        if (_candidateCache.TryGetValue(key, out var cached))
            return cached;

        var list = BuildCrewCandidates(tabletStation, tabletMap, tablet.Comp.TargetGrid);
        _candidateCache[key] = list;
        return list;
    }

    private List<DutyNeuroCrewCandidate> BuildCrewCandidates(EntityUid? tabletStation, MapId tabletMap, NetEntity? targetGridNet)
    {
        var targetGrid = targetGridNet != null ? GetEntity(targetGridNet.Value) : (EntityUid?) null;

        var list = new List<DutyNeuroCrewCandidate>();
        var query = EntityQueryEnumerator<HumanoidProfileComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (TerminatingOrDeleted(uid))
                continue;

            if (tabletStation != null)
            {
                var entityStation = _station.GetOwningStation(uid, xform);
                if (entityStation != tabletStation && (targetGrid == null || xform.GridUid != targetGrid))
                    continue;
            }
            else if (xform.MapID != tabletMap)
            {
                continue;
            }

            var (jobTitle, deptName) = GetJobAndDepartment(uid);
            // Позывной отряда Горлекса в списке экипажа NT выдал бы оперативника.
            var squadCallsign = _squads.TryGetMemberSquad(uid, out var sq) && sq.Network == DutyNeuroNetwork.NtSec
                ? sq.Callsign
                : null;
            var name = _memberEntries.TryGetValue(uid, out var entry) ? entry.Name : GetMemberDisplayName(uid);

            list.Add(new DutyNeuroCrewCandidate(
                GetNetEntity(uid),
                name,
                jobTitle,
                deptName,
                HasNeuroMaskEquipped(uid),
                HasTrackingImplant(uid),
                squadCallsign));
        }

        list.Sort((a, b) =>
        {
            var maskCmp = b.HasNeuroMask.CompareTo(a.HasNeuroMask);
            if (maskCmp != 0)
                return maskCmp;

            var implantCmp = b.HasTrackerImplant.CompareTo(a.HasTrackerImplant);
            if (implantCmp != 0)
                return implantCmp;

            return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        return list;
    }

    private void SyncTabletStationInfo(Entity<DutyNeurointerfaceTabletComponent> ent)
    {
        var station = _station.GetOwningStation(ent.Owner) ??
                      _station.GetStationInMap(_transform.GetMapId(ent.Owner));

        EntityUid? grid = null;
        string stationName;

        if (station != null)
        {
            grid = _station.GetLargestGrid((station.Value, null));
            stationName = MetaData(station.Value).EntityName;
        }
        else
        {
            stationName = Loc.GetString("duty-neuro-station-default");
        }

        grid ??= Transform(ent.Owner).GridUid;
        var netGrid = grid != null ? GetNetEntity(grid.Value) : (NetEntity?) null;

        if (ent.Comp.TargetGrid == netGrid && ent.Comp.StationName == stationName)
            return;

        ent.Comp.TargetGrid = netGrid;
        ent.Comp.StationName = stationName;
        Dirty(ent);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Чистит зону, пришедшую от клиента: она уходит в рацию (SOS/Retreat) и на HUD других игроков,
    /// поэтому без разметки, управляющих символов и не длиннее <see cref="DutyNeuroSquadManagerSystem.MaxZoneNameLength"/>.
    /// Пустой результат — сигнал вычислить зону по координатам.
    /// </summary>
    private static string SanitizeZoneName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var noMarkup = FormattedMessage.RemoveMarkupPermissive(raw);
        var sb = new StringBuilder(Math.Min(noMarkup.Length, DutyNeuroSquadManagerSystem.MaxZoneNameLength));
        foreach (var c in noMarkup)
        {
            if (char.IsControl(c))
                continue;

            sb.Append(c);
        }

        var clean = sb.ToString().Trim();
        if (clean.Length > DutyNeuroSquadManagerSystem.MaxZoneNameLength)
            clean = clean[..DutyNeuroSquadManagerSystem.MaxZoneNameLength].TrimEnd();

        return clean;
    }

    private DutyNeuroMemberStatus GetMemberStatus(EntityUid member)
    {
        if (_mobState.IsDead(member))
            return DutyNeuroMemberStatus.Dead;

        if (!_player.TryGetSessionByEntity(member, out _))
            return DutyNeuroMemberStatus.Offline;

        if (_mobState.IsCritical(member))
            return DutyNeuroMemberStatus.Critical;

        return DutyNeuroMemberStatus.Alive;
    }

    private string GetLocationName(EntityUid uid)
    {
        var raw = _navMap.GetNearestBeaconString(uid, onlyName: true);
        return CleanBeaconName(raw);
    }

    private string GetLocationName(EntityCoordinates coords)
    {
        if (!coords.IsValid(EntityManager))
            return Loc.GetString("duty-neuro-zone-unknown");

        var raw = _navMap.GetNearestBeaconString(_transform.ToMapCoordinates(coords), onlyName: true);
        return CleanBeaconName(raw);
    }

    private string CleanBeaconName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == Loc.GetString("nav-beacon-pos-no-beacons"))
            return Loc.GetString("duty-neuro-zone-unknown");

        // Permissive: имя маяка задают игроки, кривая разметка не должна ронять пересчёт исключением.
        var clean = FormattedMessage.RemoveMarkupPermissive(raw).Trim();
        return string.IsNullOrWhiteSpace(clean) ? Loc.GetString("duty-neuro-zone-unknown") : clean;
    }

    /// <summary>
    /// Код тревоги станции с кэшем на пересчёт: у всех носителей одной станции он одинаковый.
    /// </summary>
    private (string Name, Color Color) GetStationAlertLevel(EntityUid referenceEntity)
    {
        var station = _station.GetOwningStation(referenceEntity) ??
                      _station.GetStationInMap(_transform.GetMapId(referenceEntity));

        var key = station ?? EntityUid.Invalid;
        if (_alertCache.TryGetValue(key, out var cached))
            return cached;

        (string Name, Color Color) result;
        if (station != null &&
            _alertQuery.TryComp(station.Value, out var alertComp) &&
            _alertLevel.TryGetLevel((station.Value, alertComp), out var level) &&
            _proto.TryIndex<AlertLevelPrototype>(level.Value, out var alertProto))
        {
            result = (alertProto.LocalizedName.ToUpperInvariant(), alertProto.Color);
        }
        else
        {
            result = (Loc.GetString("duty-neuro-alert-default"), Color.LimeGreen);
        }

        _alertCache[key] = result;
        return result;
    }

    private string GetMemberDisplayName(EntityUid uid)
    {
        if (_idCard.TryFindIdCard(uid, out var card) && !string.IsNullOrWhiteSpace(card.Comp.FullName))
            return card.Comp.FullName;

        return MetaData(uid).EntityName;
    }

    private (string JobTitle, string DepartmentName) GetJobAndDepartment(EntityUid uid)
    {
        if (_mind.TryGetMind(uid, out var mindId, out _) &&
            _job.MindTryGetJob(mindId, out var jobProto))
        {
            var deptName = Loc.GetString("duty-neuro-department-civilian");
            if (_job.TryGetPrimaryDepartment(jobProto.ID, out var deptProto))
                deptName = Loc.GetString(deptProto.Name);

            return (jobProto.LocalizedName, deptName);
        }

        if (_idCard.TryFindIdCard(uid, out var card))
        {
            var jobTitle = string.IsNullOrWhiteSpace(card.Comp.LocalizedJobTitle)
                ? Loc.GetString("duty-neuro-job-none")
                : card.Comp.LocalizedJobTitle;

            var deptName = Loc.GetString("duty-neuro-department-unknown");
            if (card.Comp.JobDepartments.Count > 0 &&
                _proto.TryIndex(card.Comp.JobDepartments[0], out var deptProto))
            {
                deptName = Loc.GetString(deptProto.Name);
            }

            return (jobTitle, deptName);
        }

        return (Loc.GetString("duty-neuro-job-none"), Loc.GetString("duty-neuro-department-unknown"));
    }

    /// <summary>
    /// Здоровье в целых процентах: дробные значения менялись бы на каждом тике регенерации
    /// и заставляли бы рассылать состояние HUD всем соотрядцам без видимой разницы.
    /// </summary>
    private float GetHealthPercent(EntityUid uid)
    {
        if (_mobState.IsDead(uid))
            return 0f;

        if (!_damageableQuery.TryComp(uid, out var damageable))
            return 100f;

        var totalDamage = _damageable.GetTotalDamage((uid, damageable)).Float();
        if (!_mobThresholds.TryGetThresholdForState(uid, MobState.Critical, out var threshold) &&
            !_mobThresholds.TryGetThresholdForState(uid, MobState.SoftCritical, out threshold) &&
            !_mobThresholds.TryGetThresholdForState(uid, MobState.Dead, out threshold))
        {
            return MathF.Round(Math.Clamp(100f - totalDamage, 0f, 100f));
        }

        var max = threshold.Value.Float();
        if (max <= 0f)
            return 0f;

        return MathF.Round(Math.Clamp((1f - totalDamage / max) * 100f, 0f, 100f));
    }

    /// <summary>
    /// Трекер — это имплант-трекер или подкожный датчик костюма. Посмертное оповещение (RattleOnTrigger)
    /// местоположение не передаёт, поэтому трекером не считается.
    /// </summary>
    private bool HasTrackingImplant(EntityUid uid)
    {
        if (!_implantedQuery.TryComp(uid, out var implanted))
            return false;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (MetaData(implant).EntityPrototype?.ID == TrackingImplantPrototypeId)
                return true;

            if (_subdermalQuery.HasComp(implant) && _suitSensorQuery.HasComp(implant))
                return true;
        }

        return false;
    }

    #endregion
}
