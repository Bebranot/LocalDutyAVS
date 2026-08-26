// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.AlertLevel;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.NukeOps;
using Content.Shared._Duty.CodeAlpha;
using Content.Shared.Administration;
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
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Duty.CodeAlpha;

/// <summary>
/// _Duty: протокол «Альфа» — ответ станции на объявление войны ядерными оперативниками.
///
/// Порядок работы: объявлена война, админам уходит окно подтверждения, через минуту (или сразу
/// после ответа, но не раньше чем через <see cref="DutyCodeAlphaVisuals.AnnounceDelay"/> после
/// сирены самого объявления войны) станция получает кроваво-красный уровень тревоги, все на её
/// карте, кроме оперативников, получают полный доступ, а на экраны приходит отсчёт до реального
/// разблокирования шаттла ЯО.
///
/// Кнопка админа — право ВЕТО, а не условие запуска: если админов нет в игре или они молчат,
/// протокол включается сам. Иначе фича была бы мёртвой в половине раундов, а станция молча теряла
/// бы минуты подготовки, потому что ванильный локаут шаттла уже идёт.
/// </summary>
public sealed class DutyCodeAlphaSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AlertLevelSystem _alertLevel = default!;
    [Dependency] private readonly SharedStationSystem _station = default!;

    private static readonly Color NoticeColor = Color.Gray;

    /// <summary>
    /// Право, дающее окно вето. Раньше «хостом» считался один аккаунт из
    /// <c>console.login_host_user</c> — то есть на сервере, где этот CVar не выставлен или дежурный
    /// зашёл под другим именем, окно не видел никто и протокол молча ждал минуту таймаута.
    /// Право нельзя «забыть настроить», поэтому спрашиваем именно его. Флаг тот же, что и у
    /// команды <c>dutycodealpha</c>: кто может включить руками, тот может и отменить.
    /// </summary>
    private const AdminFlags HostFlag = AdminFlags.Fun;

    /// <summary>Запрос, ожидающий ответа хоста. Одновременно может быть только один.</summary>
    private PendingAlpha? _pending;

    private EntityUid? _activeStation;

    /// <summary>
    /// Протокол уже отрабатывал в этом раунде (включался или получил вето). Без защёлки
    /// оперативники могли бы жать пульт повторно: WarDeclaratorSystem поднимает событие на
    /// каждое нажатие, а NukeopsRuleSystem после первого раза статус уже не меняет — значит
    /// приходил бы всё тот же WarReady, и хосту сыпались бы новые окна, а снятый зелёным
    /// код можно было бы включить заново.
    /// </summary>
    private bool _firedThisRound;
    private MapId _activeMap = MapId.Nullspace;
    private bool _rpEndSent;

    private TimeSpan _nextTick;
    private TimeSpan _nextGrant;

    public bool IsActive => _activeStation != null;

    /// <summary>Станция, на которой сейчас действует протокол, если он действует.</summary>
    public EntityUid? ActiveStation => _activeStation;

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
        // Каждая из трёх причин отказа означает, что протокол не включится вообще, а игрок
        // не увидит ни строчки. Один раз за объявление войны — не та частота, ради которой
        // стоит экономить на логе.
        if (ev.Status != WarConditionStatus.WarReady)
        {
            Log.Info($"Code Alpha: объявление войны со статусом {ev.Status}, протокол не запускается.");
            return;
        }

        if (_pending != null || _activeStation != null || _firedThisRound)
        {
            Log.Info($"Code Alpha: повторный запуск пропущен (ожидает ответа: {_pending != null}, "
                     + $"уже активен: {_activeStation != null}, отрабатывал в раунде: {_firedThisRound}).");
            return;
        }

        if (!TryGetWarDeadline(out var station, out var deadline, out var declaredAt))
        {
            Log.Error("Code Alpha: война объявлена, но не найдено активное правило ЯО с временем объявления либо станция. Протокол не запустится.");
            return;
        }

        var now = _timing.CurTime;
        _pending = new PendingAlpha
        {
            Station = station,
            Deadline = deadline,
            EarliestAnnounce = declaredAt + DutyCodeAlphaVisuals.AnnounceDelay,
            ExpiresAt = now + DutyCodeAlphaVisuals.ConfirmTimeout,
        };

        var hosts = GetHosts();
        if (hosts.Count == 0)
        {
            _chat.SendAdminAnnouncement(Loc.GetString("duty-code-alpha-admin-no-host"));
            return;
        }

        RaiseNetworkEvent(
            new DutyCodeAlphaPromptEvent(
                Loc.GetString("duty-code-alpha-prompt-body"),
                _pending.Value.ExpiresAt),
            Filter.Empty().AddPlayers(hosts));

        _chat.SendAdminAnnouncement(Loc.GetString("duty-code-alpha-admin-prompt-sent", ("count", hosts.Count)));
    }

    /// <summary>
    /// Админы с правом вето, которые сейчас в игре. Окно уходит всем сразу: первый ответивший
    /// решает, остальные окна закроются сами по таймеру.
    /// </summary>
    private List<ICommonSession> GetHosts()
    {
        var hosts = new List<ICommonSession>();
        foreach (var admin in _admin.ActiveAdmins)
        {
            if (_admin.GetAdminData(admin)?.HasFlag(HostFlag) == true)
                hosts.Add(admin);
        }

        return hosts;
    }

    private void OnHostReply(DutyCodeAlphaReplyEvent ev, EntitySessionEventArgs args)
    {
        if (_pending is not { } pending)
            return;

        // Ответ принимается только от того, у кого есть само право вето: клиент шлёт это событие
        // сам, и без проверки любой игрок мог бы отменить протокол подделанным пакетом.
        if (_admin.GetAdminData(args.SenderSession)?.HasFlag(HostFlag) != true)
            return;

        if (!ev.Confirmed)
        {
            _pending = null;
            _firedThisRound = true;
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
        _firedThisRound = false;
        _nextTick = TimeSpan.Zero;
        _nextGrant = TimeSpan.Zero;
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

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

        // Защёлка ставится именно здесь, на пути объявления войны: повторное нажатие пульта
        // приходит с тем же WarReady, и без неё снятый зелёным код включался бы заново.
        _firedThisRound = true;

        if (!Activate(pending.Station, pending.Deadline))
            Log.Error($"Code Alpha: подтверждение получено, но включить протокол на {ToPrettyString(pending.Station)} не удалось.");
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
    ///
    /// Защёлку «отработал в этом раунде» здесь НЕ ставит: она защищает от повторного объявления
    /// войны и принадлежит именно тому пути. Если ставить её тут, то ручной прогон командой
    /// навсегда выключал бы автоматический триггер до конца раунда — а это ровно то, что делает
    /// админ, когда проверяет фичу перед раундом.
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
        // Гриды станции могут появиться позже её сущности, поэтому карту доразрешаем здесь,
        // иначе одна неудачная попытка в момент включения навсегда оставила бы всех без доступа.
        if (_activeMap == MapId.Nullspace && _activeStation is { } stationUid)
            _activeMap = GetStationMap(stationUid);

        if (_activeMap == MapId.Nullspace)
            return;

        var query = EntityQueryEnumerator<MindContainerComponent, TransformComponent>();
        var granted = new List<EntityUid>();

        while (query.MoveNext(out var uid, out var mind, out var xform))
        {
            // HasMind, а не просто наличие компонента: MindContainerComponent висит и на пустых
            // телах, и на животных, которыми можно управлять. Доступ нужен людям, а не обезьянам.
            if (!mind.HasMind || xform.MapID != _activeMap)
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
    /// правила, а если его цель не задана — первая настоящая станция с уровнем тревоги.
    /// </summary>
    private bool TryGetWarDeadline(
        out EntityUid station,
        out TimeSpan deadline,
        out TimeSpan declaredAt)
    {
        station = default;
        deadline = default;
        declaredAt = default;

        var found = false;

        var query = EntityQueryEnumerator<ActiveGameRuleComponent, NukeopsRuleComponent>();
        while (query.MoveNext(out _, out _, out var nukeops))
        {
            if (nukeops.WarDeclaredTime is not { } declared)
                continue;

            declaredAt = declared;
            deadline = declared + nukeops.WarNukieArriveDelay;
            found = true;

            if (nukeops.TargetStation is { } target && Exists(target))
            {
                station = target;
                return true;
            }

            break;
        }

        if (!found)
            return false;

        // Фолбэка «станция под пультом» здесь быть не может: пульт — стартовый предмет
        // оперативника, а NukeopsRuleSystem принимает объявление только с карты аванпоста.
        // То есть под пультом либо ничего, либо чужой грид. Берём первую настоящую станцию.
        foreach (var candidate in _station.GetStations())
        {
            if (!HasComp<AlertLevelComponent>(candidate) || !HasComp<StationDataComponent>(candidate))
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
