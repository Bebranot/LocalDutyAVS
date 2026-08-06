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

    /// <summary>Длительность парирующего блока — доступен только под наказанием за удар в блок.</summary>
    private static readonly TimeSpan ParryDuration = TimeSpan.FromSeconds(0.2);

    /// <summary>
    /// Кулдаун парирующего блока. Он же служит замком «катсцену нельзя повторить»: нет парирования —
    /// нет и входа в дуэль, поэтому отдельный таймер на катсцену не нужен.
    /// </summary>
    private static readonly TimeSpan ParryCooldown = TimeSpan.FromSeconds(15);

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

        var blockCdQuery = EntityQueryEnumerator<TwoHandedBlockCooldownComponent>();
        while (blockCdQuery.MoveNext(out var uid, out var cooldown))
        {
            if (now >= cooldown.EndTime)
                RemComp<TwoHandedBlockCooldownComponent>(uid);
        }

        var parryCdQuery = EntityQueryEnumerator<TwoHandedParryCooldownComponent>();
        while (parryCdQuery.MoveNext(out var uid, out var cooldown))
        {
            if (now >= cooldown.EndTime)
                RemComp<TwoHandedParryCooldownComponent>(uid);
        }
    }

    // ── Активация ──────────────────────────────────────────────

    private void HandleBlockInput(ICommonSession? session)
    {
        if (session?.AttachedEntity is not { Valid: true } uid || !Exists(uid))
            return;

        if (HasComp<TwoHandedBlockComponent>(uid))
            return; // уже держит окно блока

        // Во время катсцены клавиша блока не работает — там свой набор клавиш.
        if (HasComp<QteInputLockComponent>(uid))
            return;

        // Право на парирование: маркер «только что ударил в чужой блок» ещё жив.
        var isParry = TryGetLiveMarker(uid, out _);

        if (isParry)
        {
            if (HasComp<TwoHandedParryCooldownComponent>(uid))
                return;
        }
        else
        {
            if (HasComp<TwoHandedBlockCooldownComponent>(uid))
                return;

            // Обычный блок требует дееспособности. Парирующий — намеренно нет: игрок в этот момент
            // под StunnedComponent, который иначе зарубил бы саму попытку парировать.
            if (!_actionBlocker.CanInteract(uid, null))
                return;
        }

        if (!TryGetEligibleWeapon(uid, out var weaponUid))
            return;

        OpenBlockWindow(uid, weaponUid, isParry);
    }

    /// <summary>
    /// Возвращает живой (не просроченный) маркер «только что ударил в чужой блок».
    /// Просроченный удаляется лениво прямо здесь — отдельный тик для этого не нужен.
    /// </summary>
    private bool TryGetLiveMarker(EntityUid uid, out JustBlockedAttackerComponent marker)
    {
        marker = default!;

        if (!TryComp<JustBlockedAttackerComponent>(uid, out var comp))
            return false;

        if (_timing.CurTime >= comp.ExpireAt)
        {
            RemCompDeferred<JustBlockedAttackerComponent>(uid);
            return false;
        }

        marker = comp;
        return true;
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

    private void OpenBlockWindow(EntityUid uid, EntityUid weaponUid, bool isParry)
    {
        var block = EnsureComp<TwoHandedBlockComponent>(uid);
        block.Weapon = weaponUid;
        block.EndTime = _timing.CurTime + (isParry ? ParryDuration : BlockDuration);
        block.IsParry = isParry;
        block.PendingAttacker = null;

        var weaponMarker = EnsureComp<TwoHandedBlockWeaponComponent>(weaponUid);
        weaponMarker.Blocker = uid;

        if (isParry)
        {
            // Кулдаун ставится сразу при активации, а не по итогу — попытка парировать «в пустоту»
            // тоже расходует способность, иначе её можно было бы спамить в ожидании удара.
            var parryCooldown = EnsureComp<TwoHandedParryCooldownComponent>(uid);
            parryCooldown.EndTime = _timing.CurTime + ParryCooldown;

            // Замедление не нужно: игрок и так полностью обездвижен StunnedComponent.
            // Эмоут тоже пропускаем — окно 0.2с, спам в чат ни к чему.
            return;
        }

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

        // Кулдаун парирующего блока уже поставлен при активации (см. OpenBlockWindow).
        if (isParry)
            return;

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

        // Триггер QTE-дуэли: удар держащему парирующий блок нанёс ИМЕННО тот, кто его перед этим
        // заблокировал. Урон этого удара гасится полностью, а стан/маркер намеренно НЕ выдаются —
        // иначе исход дуэли снова давал бы право на парирование и цепочка стала бы бесконечной.
        if (component.IsParry && TryGetLiveMarker(uid, out var marker) && marker.Blocker == attacker)
        {
            args.Damage = new DamageSpecifier();

            if (_netMan.IsClient)
                return;

            RemCompDeferred<JustBlockedAttackerComponent>(uid);

            var startEv = new QteDuelStartRequestEvent(attacker, uid);
            RaiseLocalEvent(ref startEv);
            return;
        }

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

        var punishMarker = EnsureComp<JustBlockedAttackerComponent>(attacker);
        punishMarker.Blocker = uid;
        punishMarker.ExpireAt = _timing.CurTime + PunishStunDuration;
    }
}
