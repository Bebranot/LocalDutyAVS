using Content.Shared._Duty.ShieldBash;
using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Camera;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Duty.ShieldBash;

/// <summary>
/// _Duty: авторитетная логика способности «Удар по щиту» (см. <see cref="ShieldBashComponent"/>).
/// Отслеживает гейт «щит в одной руке + оружие в другой» по любому изменению содержимого рук,
/// выдаёт/снимает Action, обрабатывает активацию (звук, попап, толчок камеры, бафф), динамически
/// перевешивает бонус ближнего боя на текущее оружие в свободной руке и снимает бафф по таймеру
/// или при потере последнего щита. Предсказываемая часть баффа (резист, скорость) — в
/// <see cref="SharedShieldBashSystem"/>.
/// </summary>
public sealed class ShieldBashSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedShieldBashSystem _shieldBash = default!;

    private const float KickStrength = 0.4f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShieldBashComponent, GotEquippedHandEvent>(OnShieldEquipped);
        SubscribeLocalEvent<ShieldBashComponent, GotUnequippedHandEvent>(OnShieldUnequipped);
        SubscribeLocalEvent<ShieldBashComponent, ComponentShutdown>(OnShieldShutdown);

        SubscribeLocalEvent<ShieldBasherComponent, DidEquipHandEvent>(OnWielderHandEquipped);
        SubscribeLocalEvent<ShieldBasherComponent, DidUnequipHandEvent>(OnWielderHandUnequipped);
        SubscribeLocalEvent<ShieldBasherComponent, ComponentShutdown>(OnBasherShutdown);

        SubscribeLocalEvent<ShieldBashActionEvent>(OnBashAction);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ShieldBashBuffComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now >= comp.EndTime)
                RemCompDeferred<ShieldBashBuffComponent>(uid);
        }
    }

    // ── Гейт: щит в одной руке, оружие в другой ─────────────────

    private void OnShieldEquipped(Entity<ShieldBashComponent> ent, ref GotEquippedHandEvent args)
    {
        RecomputeGate(args.User);
    }

    private void OnShieldUnequipped(Entity<ShieldBashComponent> ent, ref GotUnequippedHandEvent args)
    {
        RecomputeGate(args.User);
    }

    private void OnShieldShutdown(Entity<ShieldBashComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }

    private void OnWielderHandEquipped(Entity<ShieldBasherComponent> ent, ref DidEquipHandEvent args)
    {
        OnWielderHandChanged(ent);
    }

    private void OnWielderHandUnequipped(Entity<ShieldBasherComponent> ent, ref DidUnequipHandEvent args)
    {
        OnWielderHandChanged(ent);
    }

    private void OnWielderHandChanged(Entity<ShieldBasherComponent> ent)
    {
        RecomputeGate(ent.Owner);

        if (TryComp<ShieldBashBuffComponent>(ent.Owner, out var buff))
            RefreshMeleeBonus((ent.Owner, buff));
    }

    private void OnBasherShutdown(Entity<ShieldBasherComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.GrantingShield is { } shieldUid && TryComp<ShieldBashComponent>(shieldUid, out var shieldComp))
        {
            _actions.RemoveAction(shieldComp.ActionEntity);
            shieldComp.ActionEntity = null;
        }
    }

    /// <summary>
    /// Пересчитывает, есть ли у владельца щит в руке и оружие в другой, и выдаёт/снимает Action.
    /// Личный кулдаун (<see cref="ShieldBasherComponent.NextBashTime"/>) и сам активный бафф не
    /// трогает, кроме случая, когда из рук уходит ПОСЛЕДНИЙ щит — тогда снимается и трекер, и бафф
    /// (см. решение «баф пропадает, если убрать щит из рук»).
    /// </summary>
    private void RecomputeGate(EntityUid user)
    {
        if (!TryGetHeldShield(user, out var shieldUid, out var shieldHand))
        {
            if (TryComp<ShieldBasherComponent>(user, out var basher))
            {
                RevokeAction(basher);
                RemComp<ShieldBasherComponent>(user);
            }

            // Последний щит ушёл из рук — бафф пропадает немедленно, даже если время не вышло.
            RemComp<ShieldBashBuffComponent>(user);
            return;
        }

        var basherComp = EnsureComp<ShieldBasherComponent>(user);

        // Щит, через который был выдан Action, сменился — снимаем старый грант.
        if (basherComp.GrantingShield is { } oldShield
            && oldShield != shieldUid
            && TryComp<ShieldBashComponent>(oldShield, out var oldShieldComp))
        {
            _actions.RemoveAction(oldShieldComp.ActionEntity);
            oldShieldComp.ActionEntity = null;
        }

        basherComp.GrantingShield = shieldUid;

        var shieldComp = Comp<ShieldBashComponent>(shieldUid);
        var hasWeapon = HasQualifyingWeaponOtherThan(user, shieldHand, shieldComp.MinMeleeDamage);

        if (hasWeapon)
            GrantAction(user, shieldUid, shieldComp, basherComp);
        else
            RevokeAction(basherComp);
    }

    private void GrantAction(EntityUid user, EntityUid shieldUid, ShieldBashComponent shieldComp, ShieldBasherComponent basherComp)
    {
        if (shieldComp.ActionEntity != null)
            return;

        _actions.AddAction(user, ref shieldComp.ActionEntity, shieldComp.ActionId, shieldUid);

        // Восстанавливаем личный кулдаун, если он ещё не истёк (Action пересоздаётся заново).
        var curTime = _timing.CurTime;
        if (basherComp.NextBashTime > curTime && shieldComp.ActionEntity is { } actionEnt)
            _actions.SetCooldown(actionEnt, curTime, basherComp.NextBashTime);
    }

    private void RevokeAction(ShieldBasherComponent basherComp)
    {
        if (basherComp.GrantingShield is not { } shieldUid || !TryComp<ShieldBashComponent>(shieldUid, out var shieldComp))
            return;

        _actions.RemoveAction(shieldComp.ActionEntity);
        shieldComp.ActionEntity = null;
    }

    // ── Активация ─────────────────────────────────────────────

    private void OnBashAction(ShieldBashActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;

        if (!TryComp<ShieldBasherComponent>(user, out var basher)
            || basher.GrantingShield is not { } shieldUid
            || !TryComp<ShieldBashComponent>(shieldUid, out var shieldComp))
            return;

        var now = _timing.CurTime;
        if (now < basher.NextBashTime)
            return; // экшен уже должен быть недоступен по кулдауну — доп. защита от гонки

        // Гейт мог перестать выполняться между появлением кнопки в хотбаре и кликом (оружие выбросили).
        if (!TryGetHeldShield(user, out var curShield, out var curHand) || curShield != shieldUid)
            return;
        if (!HasQualifyingWeaponOtherThan(user, curHand, shieldComp.MinMeleeDamage))
            return;

        args.Handled = true;

        // ── Бафф ──
        var durationSeconds = _random.NextFloat((float) shieldComp.MinBuffDuration.TotalSeconds, (float) shieldComp.MaxBuffDuration.TotalSeconds);
        ApplyBuff(user, shieldComp, TimeSpan.FromSeconds(durationSeconds));

        // ── Кулдаун (личный, переживает смену щита) ──
        basher.NextBashTime = now + shieldComp.Cooldown;
        if (shieldComp.ActionEntity is { } actionEnt)
            _actions.SetCooldown(actionEnt, now, basher.NextBashTime);

        // ── Атмосфера: звук, попап, толчок камеры ──
        _audio.PlayPvs(shieldComp.Sound, user);
        _popup.PopupEntity(Loc.GetString("shield-bash-popup-self"), user, user, PopupType.MediumCaution);
        _popup.PopupEntity(Loc.GetString("shield-bash-popup-others", ("user", Name(user))), user,
            Filter.PvsExcept(user), true, PopupType.SmallCaution);
        _recoil.KickCamera(user, _random.NextAngle().ToVec() * KickStrength);
    }

    private void ApplyBuff(EntityUid user, ShieldBashComponent shieldComp, TimeSpan duration)
    {
        var buff = EnsureComp<ShieldBashBuffComponent>(user);

        buff.DamageResist = shieldComp.DamageResist;
        buff.SpeedModifier = shieldComp.SpeedModifier;
        buff.MeleeDamageMultiplier = shieldComp.MeleeDamageMultiplier;
        buff.MeleeAttackRateMultiplier = shieldComp.MeleeAttackRateMultiplier;
        buff.Alert = shieldComp.Alert;
        buff.EndTime = _timing.CurTime + duration;
        Dirty(user, buff);

        RefreshMeleeBonus((user, buff));

        _alerts.ShowAlert(user, buff.Alert, cooldown: (_timing.CurTime, buff.EndTime));
    }

    // ── Динамический бонус ближнего боя ─────────────────────────

    /// <summary>
    /// Перевешивает бонус урона/скорости атаки на текущий предмет в свободной руке (не той, где щит).
    /// Вызывается при каждой смене содержимого рук, пока бафф активен — бонус «следует» за оружием,
    /// а не фиксируется на предмете, который был в руке в момент активации. Коэффициенты урона
    /// собираются по фактическим типам урона оружия, чтобы бонус работал для любого набора типов.
    /// </summary>
    private void RefreshMeleeBonus(Entity<ShieldBashBuffComponent> ent)
    {
        var user = ent.Owner;
        var buff = ent.Comp;

        EntityUid? freeHandWeapon = null;
        if (TryGetHeldShield(user, out _, out var shieldHand))
        {
            foreach (var handId in _hands.EnumerateHands(user))
            {
                if (handId == shieldHand)
                    continue;

                if (!_hands.TryGetHeldItem(user, handId, out var item) || item is not { } itemUid)
                    continue;

                if (HasComp<MeleeWeaponComponent>(itemUid))
                {
                    freeHandWeapon = itemUid;
                    break;
                }
            }
        }

        if (buff.BonusedWeapon == freeHandWeapon)
            return;

        if (buff.BonusedWeapon is { } oldWeapon && Exists(oldWeapon))
            RemComp<ShieldBashMeleeBonusComponent>(oldWeapon);

        buff.BonusedWeapon = freeHandWeapon;

        if (freeHandWeapon is { } newWeapon)
        {
            var bonus = EnsureComp<ShieldBashMeleeBonusComponent>(newWeapon);
            bonus.DamageMultiplier = buff.MeleeDamageMultiplier;
            bonus.AttackRateMultiplier = buff.MeleeAttackRateMultiplier;
            Dirty(newWeapon, bonus);
        }
    }

    // ── Хелперы ───────────────────────────────────────────────

    /// <summary>Первый найденный в руках щит с <see cref="ShieldBashComponent"/> и рука, в которой он лежит.</summary>
    private bool TryGetHeldShield(EntityUid user, out EntityUid shield, out string hand)
    {
        shield = default;
        hand = string.Empty;

        foreach (var handId in _hands.EnumerateHands(user))
        {
            if (!_hands.TryGetHeldItem(user, handId, out var item) || item is not { } itemUid)
                continue;

            if (HasComp<ShieldBashComponent>(itemUid))
            {
                shield = itemUid;
                hand = handId;
                return true;
            }
        }

        return false;
    }

    /// <summary>Есть ли в руке, ОТЛИЧНОЙ от <paramref name="excludeHand"/>, предмет-«настоящее оружие».</summary>
    private bool HasQualifyingWeaponOtherThan(EntityUid user, string excludeHand, int minMeleeDamage)
    {
        foreach (var handId in _hands.EnumerateHands(user))
        {
            if (handId == excludeHand)
                continue;

            if (!_hands.TryGetHeldItem(user, handId, out var item) || item is not { } itemUid)
                continue;

            if (_shieldBash.IsQualifyingWeapon(itemUid, minMeleeDamage))
                return true;
        }

        return false;
    }
}
