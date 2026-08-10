using Content.Shared._Duty.Block.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Input;
using Content.Shared.Interaction.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Standing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Input.Binding;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Block;

/// <summary>
/// Активный блок ближнего удара: игрок жмёт клавишу — открывается окно 0.5с. Двуручное (wielded)
/// оружие/огнестрел даёт полный уровень (негация/протечка удара + оглушение атакующего),
/// одноручное/голые руки — ослабленный (плоские -40%, без наказания). Дизайн полностью зафиксирован
/// через grill-me интервью — см. план C:\Users\BebraYep\.claude\plans\moonlit-percolating-kitten.md.
/// Написано с нуля, старая (реализованная и откаченная 6 августа) система блока/парирования/QTE
/// сознательно не переиспользуется ни кодом, ни концепцией.
/// </summary>
public sealed class BlockSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _netMan = default!;
    [Dependency] private readonly SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly MovementModStatusSystem _movementMod = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly SharedChatSystem _chat = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly BlockPunishStunSystem _punishStun = default!;
    [Dependency] private readonly BlockGunLockSystem _gunLock = default!;

    /// <summary>Длительность окна блока — гасит все попадания за это время, не только первое.</summary>
    private static readonly TimeSpan BlockWindowDuration = TimeSpan.FromSeconds(0.5);

    /// <summary>Лок атаки/стрельбы сразу после закрытия окна — при любом закрытии, любой исход.</summary>
    private static readonly TimeSpan PostBlockAttackLock = TimeSpan.FromSeconds(0.2);

    /// <summary>Наказание атакующего, ударившего в полный уровень блока.</summary>
    private static readonly TimeSpan PunishStunDuration = TimeSpan.FromSeconds(1);

    /// <summary>Кулдаун блока, если за окно хоть раз попали (любой уровень).</summary>
    private static readonly TimeSpan CooldownHit = TimeSpan.FromSeconds(3.5);

    /// <summary>Кулдаун блока, если за всё окно никто не попал.</summary>
    private static readonly TimeSpan CooldownMiss = TimeSpan.FromSeconds(1);

    /// <summary>Штраф за блок огнестрелом — нельзя стрелять/менять оружие.</summary>
    private static readonly TimeSpan GunLockDuration = TimeSpan.FromSeconds(3);

    /// <summary>Множитель скорости во время окна блока (-80%).</summary>
    private const float MovementSlowdownMultiplier = 0.2f;

    /// <summary>Доля факт. урона, проходящая блокирующему на полном уровне, если оружие атакующего сильнее.</summary>
    private const float FullTierLeakFraction = 0.2f;

    /// <summary>Множитель урона на ослабленном уровне (-40%).</summary>
    private const float ReducedTierMultiplier = 0.6f;

    /// <summary>Не чаще раза в этот интервал — иначе строка в чат спамит на зажатой клавише.</summary>
    private static readonly TimeSpan CooldownNoticeDebounce = TimeSpan.FromSeconds(1);

    private static readonly EntProtoId MovementSlowdownEffect = "DutyBlockSlowdownStatusEffect";
    private const string EmoteBlockActivate = "DutyBlockActivate";
    private const string EmoteBlockSuccess = "DutyBlockSuccess";
    private const string EmotePunishStun = "DutyBlockPunishStun";
    private const string NoticeCooldown = "duty-block-notice-cooldown";

    /// <summary>Коллекции, а не одиночные файлы — варианты добавляются правкой YAML, без кода.</summary>
    private static readonly SoundSpecifier ActivateSound = new SoundCollectionSpecifier("DutyBlockActivate");
    private static readonly SoundSpecifier HitSound = new SoundCollectionSpecifier("DutyBlockHit");

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.Block, InputCmdHandler.FromDelegate(HandleBlockInput, handle: false))
            .Register<BlockSystem>();

        SubscribeLocalEvent<BlockWeaponComponent, ItemUnwieldedEvent>(OnItemUnwielded);
        SubscribeLocalEvent<BlockComponent, DownedEvent>(OnDowned);

        SubscribeLocalEvent<BlockComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<BlockAttackLockComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<AttemptShootEvent>(OnShootAttempt);

        SubscribeLocalEvent<BlockComponent, AttackedEvent>(OnAttacked);
        SubscribeLocalEvent<BlockComponent, DamageModifyEvent>(OnDamageModify);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<BlockSystem>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        var blockQuery = EntityQueryEnumerator<BlockComponent>();
        while (blockQuery.MoveNext(out var uid, out var block))
        {
            if (now >= block.EndTime)
                CloseBlockWindow(uid, block, earlyCancel: false);
        }

        var lockQuery = EntityQueryEnumerator<BlockAttackLockComponent>();
        while (lockQuery.MoveNext(out var uid, out var lockComp))
        {
            if (now >= lockComp.EndTime)
                RemCompDeferred<BlockAttackLockComponent>(uid);
        }

        // Розыгрыш/снятие кулдауна — серверное решение, влияет на доступность способности.
        if (_netMan.IsClient)
            return;

        var cdQuery = EntityQueryEnumerator<BlockCooldownComponent>();
        while (cdQuery.MoveNext(out var uid, out var cd))
        {
            if (now >= cd.EndTime)
                RemCompDeferred<BlockCooldownComponent>(uid);
        }
    }

    // ── Активация ──────────────────────────────────────────────

    private void HandleBlockInput(ICommonSession? session)
    {
        if (session?.AttachedEntity is not { Valid: true } uid || !Exists(uid))
            return;

        if (HasComp<BlockComponent>(uid))
            return; // уже держит окно блока

        if (TryComp<BlockCooldownComponent>(uid, out var cooldown))
        {
            NotifyOnCooldown(uid, cooldown);
            return;
        }

        if (!_actionBlocker.CanInteract(uid, null))
            return;

        if (_standing.IsDown(uid))
            return; // нельзя блокировать лёжа

        if (!TryResolveBlock(uid, out var weaponUid, out var fullTier))
            return;

        OpenBlockWindow(uid, weaponUid, fullTier);
    }

    /// <summary>
    /// Резолвит оружие в активной руке (и голые руки — через тот же TryGetWeapon) в уровень блока.
    /// Двуручное (Wieldable), но не сжатое двумя руками (Wielded == false) — блок недоступен вообще.
    /// </summary>
    private bool TryResolveBlock(EntityUid uid, out EntityUid weaponUid, out bool fullTier)
    {
        weaponUid = default;
        fullTier = false;

        if (!_melee.TryGetWeapon(uid, out var candidate, out _))
            return false;

        if (TryComp<WieldableComponent>(candidate, out var wieldable))
        {
            if (!wieldable.Wielded)
                return false;

            weaponUid = candidate;
            fullTier = true;
            return true;
        }

        weaponUid = candidate;
        fullTier = false;
        return true;
    }

    private void OpenBlockWindow(EntityUid uid, EntityUid weaponUid, bool fullTier)
    {
        var block = EnsureComp<BlockComponent>(uid);
        block.Weapon = weaponUid;
        block.EndTime = _timing.CurTime + BlockWindowDuration;
        block.FullTier = fullTier;
        block.HitLanded = false;
        block.PendingAttacker = null;

        var weaponMarker = EnsureComp<BlockWeaponComponent>(weaponUid);
        weaponMarker.Blocker = uid;

        _movementMod.TryAddMovementSpeedModDuration(uid, MovementSlowdownEffect, BlockWindowDuration, MovementSlowdownMultiplier);

        // Предиктом — чтобы звук шёл сразу по нажатию, без сетевой задержки.
        _audio.PlayPredicted(ActivateSound, uid, uid);

        // Чат/огнестрел-штраф — только на сервере, чтобы не задваивать с клиентским предиктом
        // того же обработчика (CommandBinds грузятся и на клиенте, и на сервере).
        if (_netMan.IsClient)
            return;

        _chat.TryEmoteWithChat(uid, EmoteBlockActivate, ignoreActionBlocker: true, forceEmote: true);

        if (fullTier && HasComp<GunComponent>(weaponUid))
            _gunLock.ApplyGunLock(uid, GunLockDuration);
    }

    private void CloseBlockWindow(EntityUid uid, BlockComponent block, bool earlyCancel)
    {
        var weaponUid = block.Weapon;
        var hitLanded = block.HitLanded;

        RemCompDeferred<BlockComponent>(uid);
        if (Exists(weaponUid))
            RemCompDeferred<BlockWeaponComponent>(weaponUid);

        // Пост-блок лок атаки/стрельбы — всегда, независимо от исхода и способа закрытия.
        var lockComp = EnsureComp<BlockAttackLockComponent>(uid);
        lockComp.EndTime = _timing.CurTime + PostBlockAttackLock;

        // Розыгрыш кулдауна — серверное решение.
        if (_netMan.IsClient)
            return;

        if (earlyCancel)
            return; // потеря оружия/сбитие с ног — без какого-либо кулдауна вообще

        var cd = EnsureComp<BlockCooldownComponent>(uid);
        cd.EndTime = _timing.CurTime + (hitLanded ? CooldownHit : CooldownMiss);
    }

    /// <summary>
    /// Серая строка "блок ещё не восстановился" — только серверу и не чаще раза в секунду,
    /// иначе зажатая клавиша забьёт чат.
    /// </summary>
    private void NotifyOnCooldown(EntityUid uid, BlockCooldownComponent cooldown)
    {
        if (_netMan.IsClient)
            return;

        var now = _timing.CurTime;
        if (now - cooldown.LastNoticeTime < CooldownNoticeDebounce)
            return;

        cooldown.LastNoticeTime = now;

        var ev = new BlockNoticeEvent(uid, NoticeCooldown);
        RaiseLocalEvent(ref ev);
    }

    // ── Отмена при разоружении/сбитии с ног ───────────────────

    private void OnItemUnwielded(Entity<BlockWeaponComponent> ent, ref ItemUnwieldedEvent args)
    {
        if (!TryComp<BlockComponent>(ent.Comp.Blocker, out var block))
            return;

        CloseBlockWindow(ent.Comp.Blocker, block, earlyCancel: true);
    }

    private void OnDowned(Entity<BlockComponent> ent, ref DownedEvent args)
    {
        CloseBlockWindow(ent, ent.Comp, earlyCancel: true);
    }

    // ── Нельзя атаковать/стрелять во время блока и +0.2с после ───

    private void OnAttackAttempt(EntityUid uid, BlockComponent component, AttackAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnAttackAttempt(EntityUid uid, BlockAttackLockComponent component, AttackAttemptEvent args)
    {
        args.Cancel();
    }

    /// <summary>
    /// AttemptShootEvent летит directed на само оружие (не на пользователя), поэтому подписка
    /// широковещательная — фильтруем по <see cref="AttemptShootEvent.User"/> вручную.
    /// </summary>
    private void OnShootAttempt(ref AttemptShootEvent args)
    {
        if (args.Cancelled)
            return;

        if (HasComp<BlockComponent>(args.User) || HasComp<BlockAttackLockComponent>(args.User))
            args.Cancelled = true;
    }

    // ── Разрешение удара ───────────────────────────────────────

    /// <summary>
    /// Летит на жертву непосредственно перед ChangeDamage — используем как маркер "вот этот
    /// конкретный атакующий сейчас ударит", чтобы синхронно следующий за ним DamageModifyEvent
    /// мог сопоставить урон именно с этим ударом.
    /// </summary>
    private void OnAttacked(EntityUid uid, BlockComponent component, AttackedEvent args)
    {
        component.PendingAttacker = args.User;
    }

    private void OnDamageModify(EntityUid uid, BlockComponent component, DamageModifyEvent args)
    {
        if (args.Origin is not { } attacker || component.PendingAttacker != attacker)
            return; // не наш случай — не ближний бой оружием, либо не синхронизировано с AttackedEvent

        component.PendingAttacker = null;
        component.HitLanded = true;

        if (component.FullTier)
        {
            if (!TryComp<MeleeWeaponComponent>(component.Weapon, out var blockerWeapon))
                return; // оружие блокирующего пропало — защитная проверка, штатно не должно происходить

            // Сравниваем именно исходный урон удара с базовым уроном оружия блокирующего —
            // это сопоставление силы оружия, оно не должно зависеть от брони и прочих модификаторов.
            var attackDamage = args.OriginalDamage.GetTotal();
            var blockerWeaponDamage = blockerWeapon.Damage.GetTotal();

            args.Damage = attackDamage <= blockerWeaponDamage
                ? new DamageSpecifier()
                : args.Damage * FullTierLeakFraction;
        }
        else
        {
            args.Damage = args.Damage * ReducedTierMultiplier;
        }

        // Оружие блокирующего никогда не изнашивается — Damageable этого оружия мы нигде не трогаем.

        // Чат/оглушение — серверное решение, не должно применяться из клиентского предикта чужой атаки.
        if (_netMan.IsClient)
            return;

        _chat.TryEmoteWithChat(uid, EmoteBlockSuccess, ignoreActionBlocker: true, forceEmote: true);
        _audio.PlayPvs(HitSound, uid);

        if (component.FullTier)
        {
            _punishStun.ApplyPunishStun(attacker, PunishStunDuration);
            _chat.TryEmoteWithChat(attacker, EmotePunishStun, ignoreActionBlocker: true, forceEmote: true);
        }
    }
}
