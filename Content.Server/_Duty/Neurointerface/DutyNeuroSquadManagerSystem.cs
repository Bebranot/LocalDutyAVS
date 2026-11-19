// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics.CodeAnalysis;
using Content.Server.GameTicking;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Duty.Neurointerface;
using Content.Shared.GameTicking;
using Content.Shared.Radio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Neurointerface;

/// <summary>
/// _Duty: серверная система управления отрядами, приказами, таймерами, задачами и радиосигналами нейроинтерфейса СБ.
/// </summary>
public sealed partial class DutyNeuroSquadManagerSystem : EntitySystem
{
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private RadioSystem _radio = default!;

    public const string DefaultSquadCallsign = "KILO-9";
    public const int MaxCallsignLength = 16;

    /// <summary>
    /// Лимит активных приказов на один отряд: больше меток на карте и в HUD уже не читается.
    /// </summary>
    public const int MaxActiveOrders = 6;

    public const int MaxZoneNameLength = 32;

    private static readonly ProtoId<RadioChannelPrototype> SecurityChannel = "Security";
    private static readonly TimeSpan TrackerAlertCooldown = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Общий кулдаун радиосигналов на отправителя (callout-ы, сигналы приказов Retreat/SOS):
    /// без него планшетом или хоткеем можно заспамить канал СБ.
    /// </summary>
    public static readonly TimeSpan CalloutCooldown = TimeSpan.FromSeconds(3);

    private readonly Dictionary<string, SquadState> _squads = new();
    private readonly Dictionary<EntityUid, string> _memberToSquad = new();
    private readonly List<DutyNeuroOrderEntry> _orders = new();
    private readonly List<DutyNeuroTimerEntry> _timers = new();
    private readonly List<DutyNeuroTaskEntry> _tasks = new();
    private readonly Dictionary<EntityUid, TimeSpan> _lastTrackerAlertTime = new();
    private readonly Dictionary<EntityUid, TimeSpan> _lastCalloutTime = new();

    // Переиспользуемый буфер для чистки словарей без аллокаций каждые полсекунды.
    private readonly List<EntityUid> _pruneBuffer = new();

    private int _nextSquadNumericId = 1;
    private int _nextOrderId = 1;
    private int _nextTimerId = 1;
    private int _nextTaskId = 1;

    /// <summary>
    /// Внутреннее состояние отряда на текущий раунд.
    /// </summary>
    public sealed class SquadState
    {
        public string SquadId { get; }
        public string Callsign { get; set; }
        public EntityUid? Leader { get; set; }
        public List<EntityUid> Members { get; } = new();

        public SquadState(string squadId, string callsign)
        {
            SquadId = squadId;
            Callsign = callsign;
        }
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateTimers();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _squads.Clear();
        _memberToSquad.Clear();
        _orders.Clear();
        _timers.Clear();
        _tasks.Clear();
        _lastTrackerAlertTime.Clear();
        _lastCalloutTime.Clear();
        _nextSquadNumericId = 1;
        _nextOrderId = 1;
        _nextTimerId = 1;
        _nextTaskId = 1;
    }

    #region Squads

    /// <summary>
    /// Гарантирует существование хотя бы одного отряда (по умолчанию KILO-9).
    /// </summary>
    public SquadState EnsureDefaultSquad()
    {
        if (_squads.TryGetValue(DefaultSquadCallsign, out var existing))
            return existing;

        foreach (var squad in _squads.Values)
            return squad;

        var created = new SquadState(DefaultSquadCallsign, DefaultSquadCallsign);
        _squads[created.SquadId] = created;
        return created;
    }

    /// <summary>
    /// Возвращает текущий отряд бойца или добавляет его в отряд по умолчанию.
    /// Вызывать только в момент надевания маски: периодический вызов возвращал бы бойца,
    /// которого командир только что убрал из отряда.
    /// </summary>
    public SquadState GetOrJoinDefaultSquad(EntityUid member)
    {
        if (TryGetMemberSquad(member, out var current))
            return current;

        var fallback = EnsureDefaultSquad();
        InviteMember(fallback.SquadId, member);
        return fallback;
    }

    public bool TryGetMemberSquad(EntityUid member, [NotNullWhen(true)] out SquadState? squad)
    {
        squad = null;
        if (!_memberToSquad.TryGetValue(member, out var squadId))
            return false;

        if (_squads.TryGetValue(squadId, out squad))
            return true;

        _memberToSquad.Remove(member);
        return false;
    }

    public bool IsSquadMember(EntityUid member)
    {
        return _memberToSquad.ContainsKey(member);
    }

    public bool TryGetSquad(string squadId, [NotNullWhen(true)] out SquadState? squad)
    {
        return _squads.TryGetValue(squadId, out squad);
    }

    public IReadOnlyCollection<SquadState> GetAllSquads()
    {
        return _squads.Values;
    }

    public string GetFallbackSquadId()
    {
        return EnsureDefaultSquad().SquadId;
    }

    public SquadState? CreateSquad(string callsign)
    {
        var clean = SanitizeCallsign(callsign);
        if (string.IsNullOrEmpty(clean))
            return null;

        var squadId = !_squads.ContainsKey(clean) ? clean : $"SQUAD-{_nextSquadNumericId++}";
        while (_squads.ContainsKey(squadId))
        {
            squadId = $"SQUAD-{_nextSquadNumericId++}";
        }

        var squad = new SquadState(squadId, clean);
        _squads[squadId] = squad;
        return squad;
    }

    public bool RenameSquad(string squadId, string newCallsign)
    {
        if (!_squads.TryGetValue(squadId, out var squad))
            return false;

        var clean = SanitizeCallsign(newCallsign);
        if (string.IsNullOrEmpty(clean))
            return false;

        squad.Callsign = clean;
        return true;
    }

    public bool DisbandSquad(string squadId)
    {
        // Запрещаем удалять последний отряд, чтобы нейроинтерфейс всегда имел целевой отряд.
        if (_squads.Count <= 1 || !_squads.TryGetValue(squadId, out var squad))
            return false;

        _squads.Remove(squadId);

        // Приказы привязаны к отряду: без отряда их некому показывать, а при переносе бойцов
        // в другой отряд чужие метки только запутают.
        _orders.RemoveAll(o => o.SquadId == squadId);

        SquadState? fallback = null;
        foreach (var other in _squads.Values)
        {
            fallback = other;
            break;
        }

        if (fallback == null)
            return true;

        foreach (var member in squad.Members)
        {
            _memberToSquad.Remove(member);
            if (!Exists(member) || TerminatingOrDeleted(member))
                continue;

            if (!fallback.Members.Contains(member))
                fallback.Members.Add(member);

            _memberToSquad[member] = fallback.SquadId;
            fallback.Leader ??= member;
        }

        squad.Members.Clear();
        return true;
    }

    public bool InviteMember(string squadId, EntityUid target)
    {
        if (!Exists(target) || TerminatingOrDeleted(target))
            return false;

        if (!_squads.TryGetValue(squadId, out var targetSquad))
            return false;

        if (_memberToSquad.TryGetValue(target, out var oldSquadId))
        {
            if (oldSquadId == squadId && targetSquad.Members.Contains(target))
                return true;

            RemoveMember(oldSquadId, target);
        }

        if (!targetSquad.Members.Contains(target))
            targetSquad.Members.Add(target);

        _memberToSquad[target] = squadId;
        targetSquad.Leader ??= target;
        return true;
    }

    public bool RemoveMember(string squadId, EntityUid target)
    {
        if (!_squads.TryGetValue(squadId, out var squad))
            return false;

        var removed = squad.Members.Remove(target);
        if (_memberToSquad.TryGetValue(target, out var mapped) && mapped == squadId)
            _memberToSquad.Remove(target);

        if (squad.Leader == target)
        {
            squad.Leader = null;
            foreach (var member in squad.Members)
            {
                if (!Exists(member) || TerminatingOrDeleted(member))
                    continue;

                squad.Leader = member;
                break;
            }
        }

        return removed;
    }

    public bool SetLeader(string squadId, EntityUid target)
    {
        if (!Exists(target) || TerminatingOrDeleted(target))
            return false;

        if (!_squads.TryGetValue(squadId, out var squad))
            return false;

        if (!squad.Members.Contains(target))
            InviteMember(squadId, target);

        squad.Leader = target;
        return true;
    }

    /// <summary>
    /// Удаляет несуществующие сущности из отрядов и протухшие записи кулдаунов.
    /// </summary>
    public void CleanupDeletedEntities()
    {
        foreach (var squad in _squads.Values)
        {
            for (var i = squad.Members.Count - 1; i >= 0; i--)
            {
                var member = squad.Members[i];
                if (Exists(member) && !TerminatingOrDeleted(member))
                    continue;

                squad.Members.RemoveAt(i);
                _memberToSquad.Remove(member);
            }

            if (squad.Leader != null && (!Exists(squad.Leader.Value) || TerminatingOrDeleted(squad.Leader.Value) || !squad.Members.Contains(squad.Leader.Value)))
            {
                squad.Leader = squad.Members.Count > 0 ? squad.Members[0] : null;
            }
        }

        // Кулдауны держим только пока они действуют: иначе словари копят всех, кто хоть раз кричал в рацию.
        var now = _timing.CurTime;
        PruneExpired(_lastTrackerAlertTime, now, TrackerAlertCooldown);
        PruneExpired(_lastCalloutTime, now, CalloutCooldown);
    }

    private void PruneExpired(Dictionary<EntityUid, TimeSpan> dict, TimeSpan now, TimeSpan cooldown)
    {
        if (dict.Count == 0)
            return;

        _pruneBuffer.Clear();
        foreach (var (uid, time) in dict)
        {
            if (now - time >= cooldown || !Exists(uid))
                _pruneBuffer.Add(uid);
        }

        foreach (var uid in _pruneBuffer)
            dict.Remove(uid);

        _pruneBuffer.Clear();
    }

    #endregion

    #region Orders

    /// <summary>
    /// Создаёт приказ для отряда. Лимит и правило Retreat действуют в пределах отряда,
    /// чтобы один командир не стирал метки соседнего отряда.
    /// </summary>
    public DutyNeuroOrderEntry? CreateOrder(
        string squadId,
        DutyNeuroOrderType orderType,
        NetCoordinates coordinates,
        string zoneName,
        EntityUid author,
        string authorName)
    {
        if (!_squads.ContainsKey(squadId))
            return null;

        var cleanZone = string.IsNullOrWhiteSpace(zoneName)
            ? Loc.GetString("duty-neuro-zone-unknown")
            : zoneName.Trim();

        // Приказ Retreat очищает предыдущие не-Retreat приказы отряда (заменяет Hold и остальные метки) и даёт сигнал в рацию.
        if (orderType == DutyNeuroOrderType.Retreat)
        {
            _orders.RemoveAll(o => o.SquadId == squadId && o.OrderType != DutyNeuroOrderType.Retreat);
            SendSecurityCallout(author, DutyNeuroCalloutType.Retreat, cleanZone);
        }
        else if (orderType == DutyNeuroOrderType.Sos)
        {
            SendSecurityCallout(author, DutyNeuroCalloutType.Sos, cleanZone);
        }

        var entry = new DutyNeuroOrderEntry(
            _nextOrderId++,
            orderType,
            cleanZone,
            coordinates,
            string.IsNullOrWhiteSpace(authorName) ? Loc.GetString("duty-neuro-order-default-author") : authorName.Trim(),
            _gameTicker.RoundDuration(),
            squadId);

        _orders.Add(entry);

        // Список упорядочен по времени создания, поэтому первый найденный приказ отряда — самый старый.
        var count = CountSquadOrders(squadId);
        for (var i = 0; i < _orders.Count && count > MaxActiveOrders;)
        {
            if (_orders[i].SquadId == squadId)
            {
                _orders.RemoveAt(i);
                count--;
                continue;
            }

            i++;
        }

        return entry;
    }

    private int CountSquadOrders(string squadId)
    {
        var count = 0;
        foreach (var order in _orders)
        {
            if (order.SquadId == squadId)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Удаляет приказ, только если он принадлежит указанному отряду: планшет управляет лишь активным отрядом.
    /// </summary>
    public bool RemoveOrder(string squadId, int orderId)
    {
        var idx = _orders.FindIndex(o => o.OrderId == orderId && o.SquadId == squadId);
        if (idx < 0)
            return false;

        _orders.RemoveAt(idx);
        return true;
    }

    public bool ClearOrders(string squadId)
    {
        return _orders.RemoveAll(o => o.SquadId == squadId) > 0;
    }

    /// <summary>
    /// Дописывает приказы отряда в переданный список (без собственной аллокации — вызывается на каждом пересчёте).
    /// </summary>
    public void CollectOrders(string squadId, List<DutyNeuroOrderEntry> into)
    {
        foreach (var order in _orders)
        {
            if (order.SquadId == squadId)
                into.Add(order);
        }
    }

    public List<DutyNeuroOrderEntry> GetOrders(string squadId)
    {
        var list = new List<DutyNeuroOrderEntry>();
        CollectOrders(squadId, list);
        return list;
    }

    #endregion

    #region Timers

    public DutyNeuroTimerEntry CreateTimer(string title, int seconds)
    {
        var cleanTitle = string.IsNullOrWhiteSpace(title) ? Loc.GetString("duty-neuro-timer-default-title") : title.Trim();
        if (cleanTitle.Length > 32)
            cleanTitle = cleanTitle[..32];

        var clampedSeconds = Math.Clamp(seconds, 5, 3600);
        var duration = TimeSpan.FromSeconds(clampedSeconds);
        var entry = new DutyNeuroTimerEntry(
            _nextTimerId++,
            cleanTitle,
            duration,
            _timing.CurTime + duration,
            null,
            DutyNeuroTimerStatus.Running);

        _timers.Add(entry);
        return entry;
    }

    public bool HandleTimerAction(int timerId, DutyNeuroTimerAction action)
    {
        var idx = _timers.FindIndex(t => t.TimerId == timerId);
        if (idx < 0)
            return false;

        var timer = _timers[idx];
        switch (action)
        {
            case DutyNeuroTimerAction.Start:
            {
                var remaining = timer.Status == DutyNeuroTimerStatus.Paused
                    ? (timer.RemainingWhenPaused ?? timer.Duration)
                    : timer.Duration;

                if (remaining <= TimeSpan.Zero)
                    remaining = timer.Duration;

                _timers[idx] = timer with
                {
                    Status = DutyNeuroTimerStatus.Running,
                    EndTime = _timing.CurTime + remaining,
                    RemainingWhenPaused = null
                };
                return true;
            }
            case DutyNeuroTimerAction.Pause:
            {
                if (timer.Status != DutyNeuroTimerStatus.Running || timer.EndTime == null)
                    return false;

                var remaining = timer.EndTime.Value - _timing.CurTime;
                if (remaining < TimeSpan.Zero)
                    remaining = TimeSpan.Zero;

                _timers[idx] = timer with
                {
                    Status = DutyNeuroTimerStatus.Paused,
                    EndTime = null,
                    RemainingWhenPaused = remaining
                };
                return true;
            }
            case DutyNeuroTimerAction.Reset:
            {
                _timers[idx] = timer with
                {
                    Status = DutyNeuroTimerStatus.Stopped,
                    EndTime = null,
                    RemainingWhenPaused = timer.Duration
                };
                return true;
            }
            case DutyNeuroTimerAction.Delete:
            {
                _timers.RemoveAt(idx);
                return true;
            }
            default:
                return false;
        }
    }

    public List<DutyNeuroTimerEntry> GetTimers()
    {
        return new List<DutyNeuroTimerEntry>(_timers);
    }

    /// <summary>
    /// Переводит истёкшие таймеры в Stopped. Планшеты увидят это на ближайшем плановом пересчёте (≤0.5 с):
    /// обратный отсчёт клиент рисует сам по EndTime, так что мгновенная пересылка не нужна.
    /// </summary>
    private void UpdateTimers()
    {
        if (_timers.Count == 0)
            return;

        var now = _timing.CurTime;
        for (var i = 0; i < _timers.Count; i++)
        {
            var timer = _timers[i];
            if (timer.Status != DutyNeuroTimerStatus.Running || timer.EndTime == null)
                continue;

            if (timer.EndTime.Value > now)
                continue;

            _timers[i] = timer with
            {
                Status = DutyNeuroTimerStatus.Stopped,
                EndTime = null,
                RemainingWhenPaused = TimeSpan.Zero
            };
        }
    }

    #endregion

    #region Tasks

    public DutyNeuroTaskEntry? CreateTask(
        string title,
        string description,
        string assigneeName,
        DutyNeuroTaskPriority priority)
    {
        var cleanTitle = title.Trim();
        if (string.IsNullOrEmpty(cleanTitle))
            return null;

        if (cleanTitle.Length > 48)
            cleanTitle = cleanTitle[..48];

        var cleanDesc = description.Trim();
        if (cleanDesc.Length > 120)
            cleanDesc = cleanDesc[..120];

        var cleanAssignee = string.IsNullOrWhiteSpace(assigneeName)
            ? Loc.GetString("duty-neuro-task-default-assignee")
            : assigneeName.Trim();
        if (cleanAssignee.Length > 32)
            cleanAssignee = cleanAssignee[..32];

        var entry = new DutyNeuroTaskEntry(
            _nextTaskId++,
            cleanTitle,
            cleanDesc,
            cleanAssignee,
            priority,
            false);

        _tasks.Add(entry);
        return entry;
    }

    public bool ToggleTask(int taskId)
    {
        var idx = _tasks.FindIndex(t => t.TaskId == taskId);
        if (idx < 0)
            return false;

        var task = _tasks[idx];
        _tasks[idx] = task with { Completed = !task.Completed };
        return true;
    }

    public bool DeleteTask(int taskId)
    {
        var idx = _tasks.FindIndex(t => t.TaskId == taskId);
        if (idx < 0)
            return false;

        _tasks.RemoveAt(idx);
        return true;
    }

    public List<DutyNeuroTaskEntry> GetTasks()
    {
        return new List<DutyNeuroTaskEntry>(_tasks);
    }

    #endregion

    #region Radio

    public bool IsCalloutOnCooldown(EntityUid sender)
    {
        return _lastCalloutTime.TryGetValue(sender, out var last) &&
               _timing.CurTime - last < CalloutCooldown;
    }

    /// <summary>
    /// Отправляет тактический радиосигнал от лица указанной сущности на канал СБ.
    /// Зона должна приходить уже очищенной сервером — сырой клиентский текст сюда не передавать.
    /// </summary>
    public bool SendSecurityCallout(EntityUid sender, DutyNeuroCalloutType calloutType, string? zoneName = null)
    {
        if (!Exists(sender) || TerminatingOrDeleted(sender))
            return false;

        if (IsCalloutOnCooldown(sender))
            return false;

        if (!_proto.TryIndex(SecurityChannel, out var channel))
            return false;

        var zone = string.IsNullOrWhiteSpace(zoneName) ? Loc.GetString("duty-neuro-zone-unknown") : zoneName.Trim();
        var msg = calloutType switch
        {
            DutyNeuroCalloutType.Acknowledge => Loc.GetString("duty-neuro-callout-ack"),
            DutyNeuroCalloutType.Retreat => Loc.GetString("duty-neuro-callout-retreat"),
            DutyNeuroCalloutType.Sos => Loc.GetString("duty-neuro-callout-sos", ("zone", zone)),
            DutyNeuroCalloutType.Medic => Loc.GetString("duty-neuro-callout-medic"),
            _ => Loc.GetString("duty-neuro-callout-ack"),
        };

        _lastCalloutTime[sender] = _timing.CurTime;
        _radio.SendRadioMessage(sender, msg, channel, sender);
        return true;
    }

    /// <summary>
    /// Отправляет автоматическое радиооповещение трекера о критическом состоянии бойца с кулдауном 20 секунд.
    /// Кулдаун отдельный от callout-ов: оповещение автоматическое и не должно глушиться ручными сигналами.
    /// </summary>
    public bool TrySendCriticalTrackerAlert(EntityUid target, string targetName, string zoneName)
    {
        if (!Exists(target) || TerminatingOrDeleted(target))
            return false;

        var now = _timing.CurTime;
        if (_lastTrackerAlertTime.TryGetValue(target, out var lastTime) &&
            now - lastTime < TrackerAlertCooldown)
        {
            return false;
        }

        if (!_proto.TryIndex(SecurityChannel, out var channel))
            return false;

        _lastTrackerAlertTime[target] = now;
        var cleanZone = string.IsNullOrWhiteSpace(zoneName) ? Loc.GetString("duty-neuro-zone-unknown") : zoneName.Trim();
        var msg = Loc.GetString("duty-neuro-callout-tracker-crit", ("name", targetName), ("zone", cleanZone));
        _radio.SendRadioMessage(target, msg, channel, target);
        return true;
    }

    #endregion

    private static string SanitizeCallsign(string raw)
    {
        var trimmed = raw.Trim().ToUpperInvariant();
        if (trimmed.Length > MaxCallsignLength)
            trimmed = trimmed[..MaxCallsignLength].TrimEnd();

        return trimmed;
    }
}
