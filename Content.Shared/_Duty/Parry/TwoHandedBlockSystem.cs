using Content.Shared._Duty.Parry.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Input;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Input.Binding;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Parry;

/// <summary>
/// Двуручный блок (Фаза 1): держащий двуручное оружие может на короткое окно поставить блок,
/// который либо полностью гасит удар более слабого оружия (наказывая атакующего оглушением),
/// либо пропускает часть урона удара более сильного оружия. См. план в
/// C:\Users\BebraYep\.claude\plans\mellow-puzzling-pebble.md.
///
/// Парирование и QTE-катсцена (Фаза 2) добавляются позже поверх этой системы —
/// <see cref="JustBlockedAttackerComponent"/> уже выдаётся здесь, но пока никем не читается.
/// </summary>
public sealed class TwoHandedBlockSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _netMan = default!;
    [Dependency] private readonly SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly MovementModStatusSystem _movementMod = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly SharedChatSystem _chat = default!;

    /// <summary>Длительность обычного окна блока.</summary>
    private static readonly TimeSpan BlockDuration = TimeSpan.FromSeconds(0.3);

    /// <summary>Оглушение атакующего, ударившего в активный блок.</summary>
    private static readonly TimeSpan PunishStunDuration = TimeSpan.FromSeconds(0.8);

    /// <summary>Доля фактического урона удара, проходящая блокирующему, если атакующее оружие сильнее.</summary>
    private const float StrongHitLeakFraction = 0.2f;

    /// <summary>Кулдаун блока на полном ХП.</summary>
    private static readonly TimeSpan MinCooldown = TimeSpan.FromSeconds(0.8);

    /// <summary>Кулдаун блока на грани крита.</summary>
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromSeconds(3.5);

    /// <summary>Кулдаун-фолбэк для мобов без порога Critical (нет от чего скейлить).</summary>
    private static readonly TimeSpan DefaultCooldownFallback = TimeSpan.FromSeconds(1.5);

    private static readonly EntProtoId BlockSlowdownEffect = "DutyTwoHandedBlockSlowdownStatusEffect";

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.Block, InputCmdHandler.FromDelegate(HandleBlockInput, handle: false))
            .Register<TwoHandedBlockSystem>();

        SubscribeLocalEvent<TwoHandedBlockWeaponComponent, ItemUnwieldedEvent>(OnItemUnwielded);
        SubscribeLocalEvent<TwoHandedBlockComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<TwoHandedBlockComponent, AttackedEvent>(OnAttacked);
        SubscribeLocalEvent<TwoHandedBlockComponent, DamageModifyEvent>(OnDamageModify);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<TwoHandedBlockSystem>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        var blockQuery = EntityQueryEnumerator<TwoHandedBlockComponent>();
        while (blockQuery.MoveNext(out var uid, out var block))
        {
            if (now >= block.EndTime)
                CloseBlockWindow(uid, block);
        }

        // Ленивая уборка просроченных маркеров "только что ударил в блок" — на случай,
        // если атакующий так и не воспользовался (и не проверил) право на парирование.
        var markerQuery = EntityQueryEnumerator<JustBlockedAttackerComponent>();
        while (markerQuery.MoveNext(out var uid, out var marker))
        {
            if (now >= marker.ExpireAt)
                RemComp<JustBlockedAttackerComponent>(uid);
        }
    }

    // ── Активация ──────────────────────────────────────────────

    private void HandleBlockInput(ICommonSession? session)
    {
        if (session?.AttachedEntity is not { Valid: true } uid || !Exists(uid))
            return;

        if (HasComp<TwoHandedBlockComponent>(uid))
            return; // уже держит окно блока

        if (HasComp<TwoHandedBlockCooldownComponent>(uid))
            return; // ещё на кулдауне

        // Парирование (Фаза 2) сюда подключается отдельной веткой поверх JustBlockedAttackerComponent —
        // в Фазе 1 клавиша всегда открывает обычное окно блока.

        if (!_actionBlocker.CanInteract(uid, null))
            return;

        if (!TryGetEligibleWeapon(uid, out var weaponUid))
            return;

        OpenBlockWindow(uid, weaponUid);
    }

    /// <summary>Двуручное оружие = MeleeWeaponComponent + WieldableComponent.Wielded одновременно.</summary>
    private bool TryGetEligibleWeapon(EntityUid uid, out EntityUid weaponUid)
    {
        weaponUid = default;

        if (!_melee.TryGetWeapon(uid, out var candidate, out _))
            return false;

        if (!TryComp<WieldableComponent>(candidate, out var wieldable) || !wieldable.Wielded)
            return false;

        weaponUid = candidate;
        return true;
    }

    private void OpenBlockWindow(EntityUid uid, EntityUid weaponUid)
    {
        var block = EnsureComp<TwoHandedBlockComponent>(uid);
        block.Weapon = weaponUid;
        block.EndTime = _timing.CurTime + BlockDuration;
        block.IsParry = false;
        block.PendingAttacker = null;

        var weaponMarker = EnsureComp<TwoHandedBlockWeaponComponent>(weaponUid);
        weaponMarker.Blocker = uid;

        _movementMod.TryAddMovementSpeedModDuration(uid, BlockSlowdownEffect, BlockDuration, 0.5f);

        // Чат/эмоут — только на сервере, чтобы не задваивать сообщение с клиентским предиктом того же обработчика.
        // forceEmote: true — эмоут намеренно available: false (не выбирается вручную/не спамится через чат),
        // но должен безусловно срабатывать при вызове из этой системы.
        if (!_netMan.IsClient)
            _chat.TryEmoteWithChat(uid, "DutyTwoHandedBlock", ignoreActionBlocker: true, forceEmote: true);
    }

    private void CloseBlockWindow(EntityUid uid, TwoHandedBlockComponent block)
    {
        var isParry = block.IsParry;
        var weaponUid = block.Weapon;
        RemCompDeferred<TwoHandedBlockComponent>(uid);
        RemCompDeferred<TwoHandedBlockWeaponComponent>(weaponUid);

        // Розыгрыш кулдауна — серверное решение (влияет на доступность способности для обоих клиентов).
        if (_netMan.IsClient)
            return;

        if (isParry)
            return; // парирующий блок и его 15с кулдаун — Фаза 2

        var cooldown = RollCooldown(uid);
        var comp = EnsureComp<TwoHandedBlockCooldownComponent>(uid);
        comp.EndTime = _timing.CurTime + cooldown;
    }

    /// <summary>Отменяет блок досрочно (оружие разоружили/выронили) — без штрафного кулдауна.</summary>
    private void CancelBlock(EntityUid blocker, EntityUid weaponUid)
    {
        RemCompDeferred<TwoHandedBlockComponent>(blocker);
        RemCompDeferred<TwoHandedBlockWeaponComponent>(weaponUid);
    }

    private TimeSpan RollCooldown(EntityUid uid)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable) ||
            !_mobThreshold.TryGetIncapPercentage(uid, damageable.TotalDamage, out var percentage))
        {
            return DefaultCooldownFallback;
        }

        var t = (float) percentage.Value;
        return MinCooldown + (MaxCooldown - MinCooldown) * t;
    }

    // ── Отмена при разоружении ────────────────────────────────

    private void OnItemUnwielded(EntityUid uid, TwoHandedBlockWeaponComponent component, ItemUnwieldedEvent args)
    {
        CancelBlock(component.Blocker, uid);
    }

    // ── Нельзя атаковать во время блока ───────────────────────

    private void OnAttackAttempt(EntityUid uid, TwoHandedBlockComponent component, AttackAttemptEvent args)
    {
        args.Cancel();
    }

    // ── Разрешение удара ───────────────────────────────────────

    /// <summary>
    /// Летит на жертву непосредственно перед ChangeDamage — используем как маркер "вот этот
    /// конкретный атакующий сейчас ударит", чтобы синхронно следующий за ним DamageModifyEvent
    /// мог сопоставить урон именно с этим ударом (см. OnDamageModify).
    /// </summary>
    private void OnAttacked(EntityUid uid, TwoHandedBlockComponent component, AttackedEvent args)
    {
        component.PendingAttacker = args.User;
    }

    private void OnDamageModify(EntityUid uid, TwoHandedBlockComponent component, DamageModifyEvent args)
    {
        if (args.Origin is not { } attacker || component.PendingAttacker != attacker)
            return; // не наш случай (не ближний бой оружием, либо не синхронизировано с AttackedEvent)

        component.PendingAttacker = null;

        if (!TryComp<MeleeWeaponComponent>(component.Weapon, out var blockerWeapon))
            return; // оружие блокирующего пропало — защитная проверка, штатно не должно происходить

        var attackDamage = args.OriginalDamage.GetTotal();
        var blockerWeaponDamage = blockerWeapon.Damage.GetTotal();

        if (attackDamage <= blockerWeaponDamage)
        {
            // Атакующее оружие слабее или равно — блок держит полностью.
            args.Damage = new DamageSpecifier();
        }
        else
        {
            // Атакующее оружие сильнее — 20% фактического урона удара проходит блокирующему.
            args.Damage = args.OriginalDamage * StrongHitLeakFraction;
        }

        // Оружие блокирующего не изнашивается — Damageable этого оружия мы нигде не трогаем.

        // Оглушение атакующего и выдача маркера "только что ударил в блок" — серверное решение,
        // не должно применяться из клиентского предикта чужой атаки.
        if (_netMan.IsClient)
            return;

        _stun.TryUpdateStunDuration(attacker, PunishStunDuration);

        var marker = EnsureComp<JustBlockedAttackerComponent>(attacker);
        marker.Blocker = uid;
        marker.ExpireAt = _timing.CurTime + PunishStunDuration;
    }
}
