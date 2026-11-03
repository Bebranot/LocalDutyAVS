// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Chat.Managers;
using Content.Shared._Duty.Slide;
using Content.Shared.Administration.Logs;
using Content.Shared.Chat;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Robust.Server.Player;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._Duty.Slide;

/// <summary>
/// _Duty: серверная часть подката — всё, что клиент не должен предсказывать: последствия тарана
/// (урон, стан и падение цели, её предмет), урон от стены, попапы, серое сообщение о перезарядке,
/// админ-лог.
/// </summary>
public sealed partial class DutySlideSystem : SharedDutySlideSystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ISharedAdminLogManager _adminLog = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StatusEffectsSystem _status = default!;

    private EntityQuery<DutySlideMasterComponent> _masterQuery;

    private readonly List<(EntityUid Uid, EntityUid Target)> _tackles = new();

    public override void Initialize()
    {
        base.Initialize();

        _masterQuery = GetEntityQuery<DutySlideMasterComponent>();
    }

    // ── Хуки ──────────────────────────────────────────────────────────────────

    protected override void OnSlideStarted(EntityUid uid)
    {
        _popup.PopupEntity(Loc.GetString("duty-slide-start-others", ("user", Identity.Entity(uid, EntityManager))),
            uid, Filter.PvsExcept(uid), true);
    }

    protected override void OnWallImpact(EntityUid uid, DutySlideMasterComponent master)
    {
        _damageable.TryChangeDamage(uid, master.WallDamage, origin: uid);

        // Себе — PopupEntity, а не PopupPredicted: удар мог случиться только на сервере, и тогда
        // игрок так и не узнал бы, почему упал (та же история, что с приземлением прыжка).
        _popup.PopupEntity(Loc.GetString("duty-slide-wall-self"), uid, uid, PopupType.MediumCaution);
        _popup.PopupEntity(Loc.GetString("duty-slide-wall-others", ("user", Identity.Entity(uid, EntityManager))),
            uid, Filter.PvsExcept(uid), true, PopupType.SmallCaution);
    }

    protected override void OnCooldown(EntityUid uid, int seconds)
    {
        if (!_player.TryGetSessionByEntity(uid, out var session))
            return;

        var message = Loc.GetString("duty-slide-cooldown", ("seconds", seconds));
        var wrapped = Loc.GetString("chat-manager-server-wrap-message", ("message", message));
        _chat.ChatMessageToOne(ChatChannel.Server, message, wrapped, default, false, session.Channel, Color.Gray);
    }

    // ── Таран ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Сам факт тарана решает shared-код внутри физ-контроллера (детерминированным броском, чтобы
    /// клиент его предсказал). Последствия откладываем до Update: менять стан и статусы чужих
    /// сущностей посреди шага физики не хочется.
    /// </summary>
    protected override void OnTackle(EntityUid uid, DutySlideMasterComponent master, EntityUid target)
    {
        _tackles.Add((uid, target));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var (uid, target) in _tackles)
        {
            if (TerminatingOrDeleted(uid) || TerminatingOrDeleted(target) || !_masterQuery.TryComp(uid, out var master))
                continue;

            Tackle(uid, master, target);
        }

        _tackles.Clear();
    }

    private void Tackle(EntityUid uid, DutySlideMasterComponent master, EntityUid target)
    {

        // ── Цель ──
        // Урон до нокдауна: урон по лежачему краулеру продлевает нокдаун до дефолтного.
        _damageable.TryChangeDamage(target, master.TackleTargetDamage, ignoreResistances: true, origin: uid);
        FlingActiveItem(target, master.TackleItemFlingDistance);

        // Стан — напрямую статус-эффектом: TryUpdateStunDuration/TryUpdateParalyzeDuration выбрасывают
        // из рук всё, а по задумке падает только предмет из активной руки. StunnedEvent поднимаем
        // сами — на него завязаны прицеливание, оптика и выкрики.
        if (_status.TryUpdateStatusEffectDuration(target, SharedStunSystem.StunId, master.TackleTargetParalyze))
        {
            var stunned = new StunnedEvent();
            RaiseLocalEvent(target, ref stunned);
        }

        _stun.TryKnockdown(target, master.TackleTargetParalyze, drop: false);

        // ── Подкатывающий ──
        // Фаза оглушения уже выставлена shared-кодом, поэтому этот стан контроллер за внешнее
        // прерывание не примет.
        _damageable.TryChangeDamage(uid, master.TackleSelfDamage, ignoreResistances: true, origin: target);
        _status.TryUpdateStatusEffectDuration(uid, SharedStunSystem.StunId, master.TackleSelfStun);

        var user = Identity.Entity(uid, EntityManager);
        var victim = Identity.Entity(target, EntityManager);
        _popup.PopupEntity(Loc.GetString("duty-slide-tackle-self", ("target", victim)), uid, uid, PopupType.Medium);
        _popup.PopupEntity(Loc.GetString("duty-slide-tackle-target", ("user", user)), target, target, PopupType.MediumCaution);
        _popup.PopupEntity(Loc.GetString("duty-slide-tackle-others", ("user", user), ("target", victim)),
            target, Filter.PvsExcept(uid).RemovePlayerByAttachedEntity(target), true, PopupType.SmallCaution);

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(uid):user} knocked down {ToPrettyString(target):target} with a slide tackle");
    }

    /// <summary>
    /// Предмет из активной руки отлетает на тайл в случайную сторону. Если там стена/окно —
    /// остаётся у ног. Не бросок: брошенный предмет ранил бы того, в кого попал.
    /// </summary>
    private void FlingActiveItem(EntityUid target, float distance)
    {
        if (!_hands.TryGetActiveItem(target, out var item))
            return;

        if (!_hands.TryDrop(target, item.Value, checkActionBlocker: false))
            return;

        var origin = _transform.GetMapCoordinates(target);
        var destination = origin.Offset(_random.NextAngle().ToVec() * distance);

        if (_interaction.InRangeUnobstructed(origin, destination, distance + 0.1f))
            _transform.SetMapCoordinates(item.Value, destination);
    }
}
