// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Access.Systems;
using Content.Server.Administration.Logs;
using Content.Server.AlertLevel;
using Content.Server.Chat.Managers;
using Content.Shared._Duty.CodeAlpha;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mind.Components;
using Content.Shared.NukeOps;
using Content.Shared.Station.Components;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Duty.CodeAlpha;

/// <summary>
/// _Duty: протокол «Альфа» — режим, в котором станция признана зоной боевых действий.
///
/// Включается вручную командой <c>dutycodealpha on</c>: станция получает кроваво-красный уровень
/// тревоги, ID-карты всех, кто на её карте (кроме оперативников), переводятся в аварийный режим и
/// открывают любую дверь, а на экраны приходит отсчёт. Снимается возвратом на зелёный код.
///
/// Автозапуска по объявлению войны нет намеренно: решение «сейчас начинается тот самый раунд» —
/// админское, а не механическое. Отсчёт идёт от самого объявления кода: пятнадцать минут с этого
/// момента и есть срок, по истечении которого оперативники вылетают.
/// </summary>
public sealed class DutyCodeAlphaSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AlertLevelSystem _alertLevel = default!;
    [Dependency] private readonly IdCardSystem _idCard = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    private static readonly Color NoticeColor = Color.Gray;

    /// <summary>Длина отсчёта. Отмеряется от объявления кода — это и есть срок до вылета ЯО.</summary>
    private static readonly TimeSpan Countdown = TimeSpan.FromMinutes(15);

    private EntityUid? _activeStation;
    private MapId _activeMap = MapId.Nullspace;
    private bool _rpEndSent;

    /// <summary>
    /// Кому уже сказали, что доступ выдан. Нужен отдельно от списка помеченных карт: доступ
    /// живёт на карте, а сообщения читают люди. По нему же уходят реплики последней минуты и
    /// строка об отзыве.
    /// </summary>
    private readonly HashSet<EntityUid> _granted = new();

    /// <summary>Кому уже сказали, что карты при себе нет. Чтобы не повторять это каждые две секунды.</summary>
    private readonly HashSet<EntityUid> _warnedNoCard = new();

    private TimeSpan _nextTick;
    private TimeSpan _nextGrant;

    public bool IsActive => _activeStation != null;

    /// <summary>Станция, на которой сейчас действует протокол, если он действует.</summary>
    public EntityUid? ActiveStation => _activeStation;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AlertLevelChangedEvent>(OnAlertLevelChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    #region Event handlers

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
        _activeStation = null;
        _activeMap = MapId.Nullspace;
        _rpEndSent = false;
        _granted.Clear();
        _warnedNoCard.Clear();
        _nextTick = TimeSpan.Zero;
        _nextGrant = TimeSpan.Zero;
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_activeStation == null)
            return;

        var now = _timing.CurTime;
        if (now < _nextTick)
            return;

        _nextTick = now + TimeSpan.FromSeconds(1);

        UpdateActive(now);
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
    /// Включает протокол на указанной станции. Публичный — этим пользуется команда
    /// <c>dutycodealpha</c>.
    /// </summary>
    public bool Activate(EntityUid station)
    {
        if (_activeStation != null || !Exists(station))
            return false;

        var now = _timing.CurTime;

        _activeStation = station;
        _activeMap = GetStationMap(station);
        _rpEndSent = false;
        _nextTick = TimeSpan.Zero;
        _nextGrant = TimeSpan.Zero;
        _granted.Clear();
        _warnedNoCard.Clear();

        var alpha = EnsureComp<DutyCodeAlphaComponent>(station);

        // Отсчёт всегда идёт от объявления кода. Ванильный WarDeclaredTime сюда не подмешивается
        // намеренно: код — это и есть тот момент, от которого считается вылет оперативников.
        alpha.Deadline = now + Countdown;
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
            $"Code Alpha activated on {ToPrettyString(station)}, countdown ends at {alpha.Deadline}");

        return true;
    }

    /// <summary>
    /// Снимает протокол: гасит аварийный режим у всех помеченных карт и убирает панель. Уровень
    /// тревоги здесь НЕ трогается — его меняет тот, кто снял код, иначе получилась бы рекурсия
    /// через <see cref="AlertLevelChangedEvent"/>.
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

        foreach (var card in CollectMarkedCards())
        {
            RemComp<DutyCodeAlphaAccessComponent>(card);
        }

        foreach (var mob in _granted)
        {
            Notice(mob, "duty-code-alpha-access-revoked");
        }

        _granted.Clear();
        _warnedNoCard.Clear();

        _adminLogger.Add(LogType.Action, LogImpact.High, $"Code Alpha deactivated");
    }

    /// <summary>
    /// Переводит в аварийный режим ID-карты всех, кто сейчас на карте станции: лейтджойнов, ОБР,
    /// воскрешённых, а также тех, кто добыл карту уже во время кода. Оперативники и призраки
    /// исключены — первым это дало бы бесплатный проход в оружейку, вторым просто не нужно.
    ///
    /// Помечается именно карта, а не человек: тогда доступ можно потерять вместе с картой, отдать
    /// её другому и снять с трупа — то есть он ведёт себя как обычный доступ, а не как невидимое
    /// свойство тела.
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
        var candidates = new List<EntityUid>();

        while (query.MoveNext(out var uid, out var mind, out var xform))
        {
            // HasMind, а не просто наличие компонента: MindContainerComponent висит и на пустых
            // телах, и на животных, которыми можно управлять. Доступ нужен людям, а не обезьянам.
            if (!mind.HasMind || xform.MapID != _activeMap)
                continue;

            if (HasComp<NukeOperativeComponent>(uid) || HasComp<GhostComponent>(uid))
                continue;

            candidates.Add(uid);
        }

        foreach (var uid in candidates)
        {
            if (!TryFindCard(uid, out var card))
            {
                if (_warnedNoCard.Add(uid))
                    Notice(uid, "duty-code-alpha-access-no-card");

                continue;
            }

            if (!HasComp<DutyCodeAlphaAccessComponent>(card))
                AddComp<DutyCodeAlphaAccessComponent>(card);

            // Второй раз человека не дёргаем: карту могли потерять и напечатать новую, а
            // сообщение об этом ничего не добавляет.
            if (!_granted.Add(uid))
                continue;

            _warnedNoCard.Remove(uid);
            Notice(uid, "duty-code-alpha-access-granted");
            Notice(uid, RandomRpKey("duty-code-alpha-rp-start"));
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// ID-карта человека: сначала слот «id» (сама карта или КПК с ней), потом руки.
    ///
    /// Не <c>IdCardSystem.TryFindIdCard</c>: тот начинает с активной руки, и Альфа пометила бы
    /// чужую карту, которую человек в этот момент просто держит, — например снятую с трупа или
    /// только что напечатанную для кого-то другого.
    /// </summary>
    private bool TryFindCard(EntityUid uid, out EntityUid card)
    {
        card = default;

        if (_inventory.TryGetSlotEntity(uid, "id", out var slot)
            && _idCard.TryGetIdCard(slot.Value, out var inSlot))
        {
            card = inSlot;
            return true;
        }

        // Тем, у кого нет слота под ID, остаются руки.
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (!_idCard.TryGetIdCard(held, out var inHands))
                continue;

            card = inHands;
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

    private List<EntityUid> CollectMarkedCards()
    {
        var query = EntityQueryEnumerator<DutyCodeAlphaAccessComponent>();
        var cards = new List<EntityUid>();
        while (query.MoveNext(out var uid, out _))
        {
            cards.Add(uid);
        }

        return cards;
    }

    private void BroadcastRpPhrase(string prefix)
    {
        foreach (var uid in _granted)
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
}
