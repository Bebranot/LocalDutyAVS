// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Cyberware.Neuro;
using Content.Shared._Duty.Neurointerface;
using Content.Shared.GameTicking;
using Content.Shared.Implants.Components;
using Content.Shared.Pinpointer;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Neurointerface;

/// <summary>
/// _Duty: публичное API нейроинтерфейса для киберимплантов (Content.*/_Duty/Cyberware/Neuro):
/// источники (маска/имплант), сети NT-SEC/Горлекс, ЭМИ-сбой, «Червь ICE», метки всей сети, «Призрак в сети», трекер v2.
/// </summary>
public sealed partial class DutyNeurointerfaceSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    /// <summary>Сколько общих меток сети (тревожная кнопка) висит одновременно: дальше HUD уже не читается.</summary>
    private const int MaxNetworkAlerts = 3;

    private static readonly SoundSpecifier IntrusionSound = new SoundCollectionSpecifier("DutyNeuroIntrusion");

    private static readonly DutyNeuroOrderType[] FakeOrderTypes =
    {
        DutyNeuroOrderType.Retreat,
        DutyNeuroOrderType.Hold,
        DutyNeuroOrderType.Move,
    };

    private EntityQuery<DutyNeuroGhostComponent> _ghostQuery;
    private EntityQuery<DutyTrackerV2Component> _trackerV2Query;
    private EntityQuery<DutyTrackerV2ImplantComponent> _trackerV2ImplantQuery;

    private sealed record NetworkAlert(DutyNeuroOrderEntry Entry, TimeSpan Expires, DutyNeuroNetwork Network);

    private readonly List<NetworkAlert> _networkAlerts = new();

    /// <summary>Трекеры v2 в крите — общие для всей сети NT-SEC, считаются раз на пересчёт.</summary>
    private readonly List<DutyNeuroTrackerEntry> _networkTrackers = new();

    // Буферы сборки списков носителя; в компонент уходит копия, так что переиспользовать безопасно.
    private readonly List<DutyNeuroTrackerEntry> _trackersBuffer = new();
    private readonly List<DutyNeuroOrderEntry> _ordersBuffer = new();
    private readonly List<SharedNavMapSystem.NavMapBeacon> _beaconBuffer = new();

    /// <summary>Id «синтетических» меток (тревога, ложная метка) — отрицательные, чтобы не пересечься с приказами отрядов.</summary>
    private int _nextSyntheticOrderId = -1;

    private string NoSignalText => Loc.GetString("duty-neuro-no-signal");

    private void InitializeCyber()
    {
        _ghostQuery = GetEntityQuery<DutyNeuroGhostComponent>();
        _trackerV2Query = GetEntityQuery<DutyTrackerV2Component>();
        _trackerV2ImplantQuery = GetEntityQuery<DutyTrackerV2ImplantComponent>();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCyberRoundRestart);
    }

    private void OnCyberRoundRestart(RoundRestartCleanupEvent ev)
    {
        _networkAlerts.Clear();
        _networkTrackers.Clear();
        _nextSyntheticOrderId = -1;
    }

    #region Sources

    /// <summary>
    /// Подключает носителя к нейроинтерфейсу через источник (маска или имплант).
    /// Отряд по умолчанию выдаётся только здесь, в момент подключения, — «убрать из отряда» продолжает работать.
    /// </summary>
    public void AddSource(EntityUid uid, EntityUid source, DutyNeuroNetwork network)
    {
        if (TerminatingOrDeleted(uid) || !_mobStateQuery.HasComp(uid))
            return;

        var wearer = EnsureComp<DutyNeurointerfaceWearerComponent>(uid);
        wearer.Sources[source] = network;
        wearer.Network = ResolveNetwork(wearer);
        wearer.Active = true;

        _squads.GetOrJoinDefaultSquad(uid, wearer.Network);

        Dirty(uid, wearer);
        RequestRefresh(candidatesChanged: true);
    }

    /// <summary>
    /// Отключает источник. Последний источник снимает компонент целиком (как раньше снятие маски).
    /// </summary>
    public void RemoveSource(EntityUid uid, EntityUid source)
    {
        if (TerminatingOrDeleted(uid) || !_wearerQuery.TryComp(uid, out var wearer))
            return;

        wearer.Sources.Remove(source);
        if (wearer.Sources.Count == 0)
        {
            // Отряд Горлекса собирается автоматически по импланту — без импланта боец (и тем более командир)
            // не должен в нём висеть. У NT-SEC членство переживает снятие маски, как и раньше.
            if (wearer.Network == DutyNeuroNetwork.Gorlex && _squads.TryGetMemberSquad(uid, out var squad))
                _squads.RemoveMember(squad.SquadId, uid);

            RemComp<DutyNeurointerfaceWearerComponent>(uid);
            RequestRefresh(candidatesChanged: true);
            return;
        }

        var network = ResolveNetwork(wearer);
        if (network != wearer.Network)
        {
            wearer.Network = network;
            _squads.GetOrJoinDefaultSquad(uid, network);
        }

        Dirty(uid, wearer);
        RequestRefresh(candidatesChanged: true);
    }

    /// <summary>Имплант в черепе важнее маски: оперативник Горлекса в трофейной маске остаётся в своей сети.</summary>
    private DutyNeuroNetwork ResolveNetwork(DutyNeurointerfaceWearerComponent wearer)
    {
        DutyNeuroNetwork? fallback = null;
        foreach (var (source, network) in wearer.Sources)
        {
            if (_subdermalQuery.HasComp(source))
                return network;

            fallback ??= network;
        }

        return fallback ?? DutyNeuroNetwork.NtSec;
    }

    /// <summary>Сеть носителя; без нейроинтерфейса — NT-SEC (как и было до появления сетей).</summary>
    public DutyNeuroNetwork GetNetwork(EntityUid uid)
    {
        return _wearerQuery.TryComp(uid, out var wearer) ? wearer.Network : DutyNeuroNetwork.NtSec;
    }

    /// <summary>ЭМИ-сбой: панель гаснет, но отряд, приказы и flatline сохраняются.</summary>
    public void SetJammed(EntityUid uid, bool jammed)
    {
        if (!_wearerQuery.TryComp(uid, out var wearer) || wearer.Jammed == jammed)
            return;

        wearer.Jammed = jammed;
        Dirty(uid, wearer);
        RequestRefresh();
    }

    /// <summary>Зона (ближайший маяк) для текстов имплантов — та же, что пишет HUD.</summary>
    public string GetZoneName(EntityUid uid)
    {
        return GetLocationName(uid);
    }

    public string GetZoneName(EntityCoordinates coords)
    {
        return GetLocationName(coords);
    }

    /// <summary>Имя для эфира и меток: по ID-карте, иначе имя сущности.</summary>
    public string GetDisplayName(EntityUid uid)
    {
        return GetMemberDisplayName(uid);
    }

    #endregion

    #region Ghost & Tracker v2

    /// <summary>Носитель «Призрака в сети», маскировка которого сейчас не сбита ЭМИ.</summary>
    public bool IsNeuroGhost(EntityUid uid)
    {
        return _ghostQuery.TryComp(uid, out var ghost) && ghost.Hidden;
    }

    private void RebuildNetworkTrackers()
    {
        _networkTrackers.Clear();

        var query = EntityQueryEnumerator<DutyTrackerV2Component>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!_mobState.IsCritical(uid) || IsNeuroGhost(uid))
                continue;

            if (_memberEntries.TryGetValue(uid, out var entry))
                _networkTrackers.Add(new DutyNeuroTrackerEntry(entry.Entity, entry.Name, entry.Location));
            else
                _networkTrackers.Add(new DutyNeuroTrackerEntry(GetNetEntity(uid), GetMemberDisplayName(uid), GetLocationName(uid)));
        }
    }

    private List<DutyNeuroTrackerEntry> ComposeTrackers(DutyNeurointerfaceWearerComponent wearer, List<DutyNeuroTrackerEntry> squadTrackers)
    {
        if (wearer.Network != DutyNeuroNetwork.NtSec || _networkTrackers.Count == 0)
            return squadTrackers;

        _trackersBuffer.Clear();
        _trackersBuffer.AddRange(squadTrackers);
        foreach (var tracker in _networkTrackers)
        {
            var duplicate = false;
            foreach (var existing in squadTrackers)
            {
                if (existing.Entity != tracker.Entity)
                    continue;

                duplicate = true;
                break;
            }

            if (!duplicate)
                _trackersBuffer.Add(tracker);
        }

        return _trackersBuffer;
    }

    /// <summary>Смерть носителя трекера v2: имплант сам выходит в эфир (как трекер СБ в видео-референсе).</summary>
    private void SendTrackerV2DeathAlert(EntityUid target)
    {
        var source = target;
        if (_implantedQuery.TryComp(target, out var implanted))
        {
            foreach (var implant in implanted.ImplantContainer.ContainedEntities)
            {
                if (!_trackerV2ImplantQuery.HasComp(implant))
                    continue;

                source = implant;
                break;
            }
        }

        var msg = Loc.GetString("duty-neuro-tracker-v2-dead",
            ("name", GetMemberDisplayName(target)),
            ("zone", GetLocationName(target)));
        _squads.SendNetworkRadio(source, msg);
    }

    #endregion

    #region Network alerts (тревожная кнопка)

    /// <summary>
    /// Метка, которую видит вся сеть (в HUD и в мире), а не один отряд. Висит <paramref name="duration"/>.
    /// </summary>
    public void AddNetworkAlert(DutyNeuroNetwork network, DutyNeuroOrderType type, EntityCoordinates coords, string zone, string author, TimeSpan duration)
    {
        var entry = new DutyNeuroOrderEntry(
            _nextSyntheticOrderId--,
            type,
            zone,
            GetNetCoordinates(coords),
            author,
            _gameTicker.RoundDuration());

        _networkAlerts.Add(new NetworkAlert(entry, _timing.CurTime + duration, network));

        var count = 0;
        for (var i = _networkAlerts.Count - 1; i >= 0; i--)
        {
            if (_networkAlerts[i].Network != network)
                continue;

            if (++count > MaxNetworkAlerts)
                _networkAlerts.RemoveAt(i);
        }

        RequestRefresh();
    }

    private void PruneNetworkAlerts()
    {
        if (_networkAlerts.Count == 0)
            return;

        var now = _timing.CurTime;
        for (var i = _networkAlerts.Count - 1; i >= 0; i--)
        {
            if (_networkAlerts[i].Expires <= now)
                _networkAlerts.RemoveAt(i);
        }
    }

    private List<DutyNeuroOrderEntry> ComposeOrders(DutyNeurointerfaceWearerComponent wearer, List<DutyNeuroOrderEntry> squadOrders)
    {
        var fake = wearer.IntrusionUntil != null ? wearer.FakeOrder : null;

        var anyAlert = false;
        foreach (var alert in _networkAlerts)
        {
            if (alert.Network != wearer.Network)
                continue;

            anyAlert = true;
            break;
        }

        if (!anyAlert && fake == null)
            return squadOrders;

        _ordersBuffer.Clear();
        _ordersBuffer.AddRange(squadOrders);
        foreach (var alert in _networkAlerts)
        {
            if (alert.Network == wearer.Network)
                _ordersBuffer.Add(alert.Entry);
        }

        // Ложная метка — последней: HUD показывает три самых новых, она должна попасть на глаза.
        if (fake != null)
            _ordersBuffer.Add(fake);

        return _ordersBuffer;
    }

    #endregion

    #region Leader orders (тактическая сеть Горлекса)

    /// <summary>
    /// Приказ командира без планшета (действие импланта): метка в точке для своего отряда.
    /// </summary>
    public bool TryCreateLeaderOrder(EntityUid leader, DutyNeuroOrderType type, EntityCoordinates coords)
    {
        if (!_wearerQuery.TryComp(leader, out var wearer))
            return false;

        if (wearer.Jammed)
        {
            _popup.PopupEntity(Loc.GetString("duty-neuro-jammed-popup"), leader, leader);
            return false;
        }

        if (!_squads.TryGetMemberSquad(leader, out var squad) || squad.Network != wearer.Network || squad.Leader != leader)
        {
            _popup.PopupEntity(Loc.GetString("duty-neuro-order-not-leader"), leader, leader);
            return false;
        }

        if (!coords.IsValid(EntityManager))
            return false;

        var zone = GetLocationName(coords);
        if (_squads.CreateOrder(squad.SquadId, type, GetNetCoordinates(coords), zone, leader, GetMemberDisplayName(leader)) == null)
            return false;

        RequestRefresh();
        return true;
    }

    #endregion

    #region Intrusion («Червь ICE»)

    /// <summary>
    /// Взлом нейроинтерфейса NT-SEC: на <paramref name="duration"/> скрамбл HUD, ложная метка и «ВТОРЖЕНИЕ».
    /// Горлекс неуязвим: червь написан под протокол NT.
    /// </summary>
    public bool TryStartIntrusion(EntityUid target, TimeSpan duration)
    {
        if (!_wearerQuery.TryComp(target, out var wearer) ||
            wearer.Network != DutyNeuroNetwork.NtSec ||
            wearer.Sources.Count == 0)
        {
            return false;
        }

        wearer.IntrusionUntil = _timing.CurTime + duration;
        wearer.FakeOrder = BuildFakeOrder(target);
        Dirty(target, wearer);

        _popup.PopupEntity(Loc.GetString("duty-neuro-intrusion-popup"), target, target, PopupType.LargeCaution);
        _audio.PlayEntity(IntrusionSound, Filter.Entities(target), target, false);

        RequestRefresh();
        return true;
    }

    public void ClearIntrusion(EntityUid target)
    {
        if (!_wearerQuery.TryComp(target, out var wearer) || wearer.IntrusionUntil == null)
            return;

        wearer.IntrusionUntil = null;
        wearer.FakeOrder = null;
        Dirty(target, wearer);
        RequestRefresh();
    }

    /// <summary>Снимает вторжение со всех бойцов отряда. Возвращает, сколько было взломано.</summary>
    public int RebootSquadNetwork(string squadId)
    {
        if (!_squads.TryGetSquad(squadId, out var squad))
            return 0;

        var cleared = 0;
        foreach (var member in squad.Members)
        {
            if (!_wearerQuery.TryComp(member, out var wearer) || wearer.IntrusionUntil == null)
                continue;

            ClearIntrusion(member);
            _popup.PopupEntity(Loc.GetString("duty-neuro-reboot-member-popup"), member, member, PopupType.Medium);
            cleared++;
        }

        return cleared;
    }

    private bool IsSquadUnderIntrusion(string squadId)
    {
        if (!_squads.TryGetSquad(squadId, out var squad))
            return false;

        var now = _timing.CurTime;
        foreach (var member in squad.Members)
        {
            if (_wearerQuery.TryComp(member, out var wearer) && wearer.IntrusionUntil is { } until && until > now)
                return true;
        }

        return false;
    }

    /// <summary>Истёкшее вторжение снимается на плановом пересчёте; true — состояние поменялось.</summary>
    private bool ExpireIntrusion(DutyNeurointerfaceWearerComponent wearer)
    {
        if (wearer.IntrusionUntil is not { } until || until > _timing.CurTime)
            return false;

        wearer.IntrusionUntil = null;
        wearer.FakeOrder = null;
        return true;
    }

    /// <summary>
    /// Ложная метка в случайном помещении станции жертвы (маяк навкарты) — «Отступить · Карго»,
    /// чтобы увести бойца, а не выдать того, кто взломал.
    /// </summary>
    private DutyNeuroOrderEntry BuildFakeOrder(EntityUid target)
    {
        var type = _random.Pick(FakeOrderTypes);
        var xform = Transform(target);

        EntityCoordinates coords;
        string zone;

        _beaconBuffer.Clear();
        if (xform.GridUid is { } grid && TryComp<NavMapComponent>(grid, out var navMap))
        {
            foreach (var beacon in navMap.Beacons.Values)
            {
                if (!string.IsNullOrWhiteSpace(beacon.Text))
                    _beaconBuffer.Add(beacon);
            }
        }

        if (_beaconBuffer.Count > 0 && xform.GridUid is { } beaconGrid)
        {
            var beacon = _random.Pick(_beaconBuffer);
            coords = new EntityCoordinates(beaconGrid, beacon.Position);
            zone = CleanBeaconName(beacon.Text);
        }
        else
        {
            var offset = _random.NextAngle().ToVec() * _random.NextFloat(8f, 14f);
            coords = xform.Coordinates.Offset(offset);
            zone = GetLocationName(coords);
        }

        _beaconBuffer.Clear();

        return new DutyNeuroOrderEntry(
            _nextSyntheticOrderId--,
            type,
            zone,
            GetNetCoordinates(coords),
            Loc.GetString("duty-neuro-order-default-author"),
            _gameTicker.RoundDuration());
    }

    private void OnRebootNetwork(Entity<DutyNeurointerfaceTabletComponent> ent, ref DutyNeuroRebootNetworkMessage msg)
    {
        if (!CheckManageAccess(ent.Owner, msg.Actor))
            return;

        var squadId = EnsureActiveSquad(ent);

        // Перезагружает командир отряда; офицер вне отрядов (ГСБ с планшетом) — тоже может.
        if (_squads.TryGetSquad(squadId, out var squad) &&
            squad.Leader != msg.Actor &&
            _squads.IsSquadMember(msg.Actor))
        {
            _popup.PopupEntity(Loc.GetString("duty-neuro-reboot-not-leader"), ent.Owner, msg.Actor);
            return;
        }

        var cleared = RebootSquadNetwork(squadId);
        _popup.PopupEntity(
            Loc.GetString(cleared > 0 ? "duty-neuro-reboot-done" : "duty-neuro-reboot-clean", ("count", cleared)),
            ent.Owner,
            msg.Actor);

        RequestRefresh();
    }

    #endregion
}
