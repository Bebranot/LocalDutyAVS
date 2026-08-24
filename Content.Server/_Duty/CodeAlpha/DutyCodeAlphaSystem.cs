// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Administration.Logs;
using Content.Server.AlertLevel;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.NukeOps;
using Content.Shared._Duty.CodeAlpha;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.NukeOps;
using Content.Shared.Station;
using Content.Shared.Station.Components;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Duty.CodeAlpha;

/// <summary>
/// _Duty: протокол «Альфа» — ответ станции на объявление войны ядерными оперативниками.
///
/// Порядок работы: объявлена война, хосту уходит окно подтверждения, через минуту (или сразу
/// после ответа, но не раньше чем через <see cref="DutyCodeAlphaVisuals.AnnounceDelay"/> после
/// сирены самого объявления войны) станция получает кроваво-красный уровень тревоги, все на её
/// карте, кроме оперативников, получают полный доступ, а на экраны приходит отсчёт до реального
/// разблокирования шаттла ЯО.
///
/// Кнопка хоста — право ВЕТО, а не условие запуска: если хост не в игре или молчит, протокол
/// включается сам. Иначе фича была бы мёртвой в половине раундов, а станция молча теряла бы
/// минуты подготовки, потому что ванильный локаут шаттла уже идёт.
/// </summary>
public sealed class DutyCodeAlphaSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AlertLevelSystem _alertLevel = default!;
    [Dependency] private readonly SharedStationSystem _station = default!;

    private static readonly Color NoticeColor = Color.Gray;

    /// <summary>Запрос, ожидающий ответа хоста. Одновременно может быть только один.</summary>
    private PendingAlpha? _pending;

    private EntityUid? _activeStation;
    private MapId _activeMap = MapId.Nullspace;
    private bool _rpEndSent;

    private TimeSpan _nextTick;
    private TimeSpan _nextGrant;

    public bool IsActive => _activeStation != null;

    public override void Initialize()
    {
        base.Initialize();

        // after: NukeopsRuleSystem — именно он проставляет WarDeclaredTime, без которого
        // не посчитать дедлайн прилёта.
        SubscribeLocalEvent<WarDeclaredEvent>(OnWarDeclared, after: [typeof(NukeopsRuleSystem)]);
        SubscribeLocalEvent<AlertLevelChangedEvent>(OnAlertLevelChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        SubscribeNetworkEvent<DutyCodeAlphaReplyEvent>(OnHostReply);
    }

    #region Event handlers

    private void OnWarDeclared(ref WarDeclaredEvent ev)
    {
        if (ev.Status != WarConditionStatus.WarReady)
            return;

        if (_pending != null || _activeStation != null)
            return;

        if (!TryGetWarDeadline(ev.DeclaratorEntity, out var station, out var deadline, out var declaredAt))
            return;

        var now = _timing.CurTime;
        _pending = new PendingAlpha
        {
            Station = station,
            Deadline = deadline,
            EarliestAnnounce = declaredAt + DutyCodeAlphaVisuals.AnnounceDelay,
            ExpiresAt = now + DutyCodeAlphaVisuals.ConfirmTimeout,
        };

        var hostName = _cfg.GetCVar(CCVars.ConsoleLoginHostUser);
        if (!string.IsNullOrWhiteSpace(hostName)
            && _player.TryGetSessionByUsername(hostName, out var host))
        {
            RaiseNetworkEvent(
                new DutyCodeAlphaPromptEvent(
                    Loc.GetString("duty-code-alpha-prompt-body"),
                    _pending.Value.ExpiresAt),
                host);

            _chat.SendAdminAnnouncement(Loc.GetString("duty-code-alpha-admin-prompt-sent", ("host", hostName)));
        }
        else
        {
            _chat.SendAdminAnnouncement(Loc.GetString("duty-code-alpha-admin-no-host"));
        }
    }

    private void OnHostReply(DutyCodeAlphaReplyEvent ev, EntitySessionEventArgs args)
    {
        if (_pending is not { } pending)
            return;

        // Ответ принимается только от того, кому окно и уходило.
        var hostName = _cfg.GetCVar(CCVars.ConsoleLoginHostUser);
        if (string.IsNullOrWhiteSpace(hostName) || args.SenderSession.Name != hostName)
            return;

        if (!ev.Confirmed)
        {
            _pending = null;
            _chat.SendAdminAnnouncement(Loc.GetString("duty-code-alpha-admin-vetoed"));
            return;
        }

        pending.Confirmed = true;
        _pending = pending;
    }

    private void OnAlertLevelChanged(AlertLevelChangedEvent args)
    {
        if (_activeStation is not { } station || args.Station != station)
            return;

        if (args.AlertLevel != DutyCodeAlphaVisuals.GreenLevel)
            return;

        Deactivate();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _pending = null;
        _activeStation = null;
        _activeMap = MapId.Nullspace;
        _rpEndSent = false;
        _nextTick = TimeSpan.Zero;
        _nextGrant = TimeSpan.Zero;
    }

    #endregion

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (now < _nextTick)
            return;

        _nextTick = now + TimeSpan.FromSeconds(1);

        UpdatePending(now);
        UpdateActive(now);
    }

    private void UpdatePending(TimeSpan now)
    {
        if (_pending is not { } pending)
            return;

        // Ждём либо явного «да», либо истечения минуты — но в обоих случаях не перебиваем
        // сирену объявления войны.
        var decided = pending.Confirmed || now >= pending.ExpiresAt;
        if (!decided || now < pending.EarliestAnnounce)
            return;

        _pending = null;
        Activate(pending.Station, pending.Deadline);
    }

    private void UpdateActive(TimeSpan now)
    {
        if (_activeStation is not { } station)
            return;

        if (Deleted(station))
        {
            Deactivate();
            return;
        }

        if (now >= _nextGrant)
        {
            _nextGrant = now + DutyCodeAlphaVisuals.GrantInterval;
            GrantPass();
        }

        if (_rpEndSent || !TryComp<DutyCodeAlphaComponent>(station, out var alpha))
            return;

        if (alpha.Deadline - now > DutyCodeAlphaVisuals.RpEndThreshold)
            return;

        _rpEndSent = true;
        BroadcastRpPhrase("duty-code-alpha-rp-end");
    }

    #region Activation

    /// <summary>
    /// Включает протокол на указанной станции. Публичный — этим же пользуется команда
    /// <c>dutycodealpha</c>.
    /// </summary>
    public bool Activate(EntityUid station, TimeSpan? deadline = null)
    {
        if (_activeStation != null || !Exists(station))
            return false;

        var now = _timing.CurTime;

        _activeStation = station;
        _activeMap = GetStationMap(station);
        _rpEndSent = false;
        _nextGrant = TimeSpan.Zero;

        var alpha = EnsureComp<DutyCodeAlphaComponent>(station);
        alpha.Deadline = deadline ?? now + TimeSpan.FromMinutes(15);
        alpha.ActivatedAt = now;
        Dirty(station, alpha);

        _alertLevel.SetLevel(
            station,
            DutyCodeAlphaVisuals.AlertLevel,
            playSound: true,
            announce: true,
            force: true,
            locked: true);

        GrantPass();

        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"Code Alpha activated on {ToPrettyString(station)}, nukie arrival at {alpha.Deadline}");

        return true;
    }

    /// <summary>
    /// Снимает протокол: отбирает доступы и убирает панель. Уровень тревоги здесь НЕ трогается —
    /// его меняет тот, кто снял код (админ через <c>setalertlevel green</c>), иначе получилась бы
    /// рекурсия через <see cref="AlertLevelChangedEvent"/>.
    /// </summary>
    public void Deactivate()
    {
        if (_activeStation is not { } station)
            return;

        _activeStation = null;
        _activeMap = MapId.Nullspace;
        _rpEndSent = false;

        if (!Deleted(station))
            RemComp<DutyCodeAlphaComponent>(station);

        foreach (var uid in CollectAccessHolders())
        {
            RemComp<DutyCodeAlphaAccessComponent>(uid);
            Notice(uid, "duty-code-alpha-access-revoked");
        }

        _adminLogger.Add(LogType.Action, LogImpact.High, $"Code Alpha deactivated");
    }

    /// <summary>
    /// Догоняет доступами всех, кто сейчас на карте станции: лейтджойнов, ОБР, воскрешённых.
    /// Оперативники и призраки исключены — первым это дало бы бесплатный проход в оружейку,
    /// вторым просто не нужно.
    /// </summary>
    private void GrantPass()
    {
        if (_activeMap == MapId.Nullspace)
            return;

        var query = EntityQueryEnumerator<MindContainerComponent, TransformComponent>();
        var granted = new List<EntityUid>();

        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != _activeMap)
                continue;

            if (HasComp<DutyCodeAlphaAccessComponent>(uid)
                || HasComp<NukeOperativeComponent>(uid)
                || HasComp<GhostComponent>(uid))
            {
                continue;
            }

            granted.Add(uid);
        }

        foreach (var uid in granted)
        {
            AddComp<DutyCodeAlphaAccessComponent>(uid);
            Notice(uid, "duty-code-alpha-access-granted");
            Notice(uid, RandomRpKey("duty-code-alpha-rp-start"));
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Находит активное правило ЯО и вычисляет момент реального прилёта. Станция берётся из
    /// правила; если его цель не задана — по гриду, с которого объявили войну, а в крайнем случае
    /// по первой станции с уровнем тревоги.
    /// </summary>
    private bool TryGetWarDeadline(
        EntityUid declarator,
        out EntityUid station,
        out TimeSpan deadline,
        out TimeSpan declaredAt)
    {
        station = default;
        deadline = default;
        declaredAt = default;

        var query = EntityQueryEnumerator<ActiveGameRuleComponent, NukeopsRuleComponent>();
        while (query.MoveNext(out _, out _, out var nukeops))
        {
            if (nukeops.WarDeclaredTime is not { } declared)
                continue;

            declaredAt = declared;
            deadline = declared + nukeops.WarNukieArriveDelay;

            if (nukeops.TargetStation is { } target && Exists(target))
            {
                station = target;
                return true;
            }

            break;
        }

        if (deadline == default)
            return false;

        if (_station.GetOwningStation(declarator) is { } owning)
        {
            station = owning;
            return true;
        }

        foreach (var candidate in _station.GetStations())
        {
            if (!HasComp<AlertLevelComponent>(candidate))
                continue;

            station = candidate;
            return true;
        }

        return false;
    }

    /// <summary>Карта, на которой физически находится станция (по её первому гриду).</summary>
    private MapId GetStationMap(EntityUid station)
    {
        if (!TryComp<StationDataComponent>(station, out var data))
            return MapId.Nullspace;

        foreach (var grid in data.Grids)
        {
            if (!Exists(grid))
                continue;

            return Transform(grid).MapID;
        }

        return MapId.Nullspace;
    }

    private List<EntityUid> CollectAccessHolders()
    {
        var query = EntityQueryEnumerator<DutyCodeAlphaAccessComponent>();
        var holders = new List<EntityUid>();
        while (query.MoveNext(out var uid, out _))
        {
            holders.Add(uid);
        }

        return holders;
    }

    private void BroadcastRpPhrase(string prefix)
    {
        foreach (var uid in CollectAccessHolders())
        {
            Notice(uid, RandomRpKey(prefix));
        }
    }

    private string RandomRpKey(string prefix)
    {
        return $"{prefix}-{_random.Next(1, DutyCodeAlphaVisuals.RpPhraseCount + 1)}";
    }

    /// <summary>Серая системная строка в чат конкретному игроку.</summary>
    private void Notice(EntityUid target, string locId)
    {
        if (!_player.TryGetSessionByEntity(target, out var session))
            return;

        var message = Loc.GetString(locId);
        var wrapped = Loc.GetString("chat-manager-server-wrap-message", ("message", message));

        _chat.ChatMessageToOne(ChatChannel.Server, message, wrapped, default, false, session.Channel, NoticeColor);
    }

    #endregion

    private struct PendingAlpha
    {
        public EntityUid Station;
        public TimeSpan Deadline;
        public TimeSpan EarliestAnnounce;
        public TimeSpan ExpiresAt;
        public bool Confirmed;
    }
}
