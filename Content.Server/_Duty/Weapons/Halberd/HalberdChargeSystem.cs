using Content.Server.Chat.Systems;
using Content.Shared._Duty.Weapons.Halberd;
using Content.Shared.Actions;
using Content.Shared.Chat;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;

namespace Content.Server._Duty.Weapons.Halberd;

/// <summary>
/// _Duty: серверная часть рывка алебардой — всё, что клиент не должен предсказывать: выдача
/// экшена при wield с сохранением кулдауна, урон цели, боевые кличи в чат, зацикленный звук рывка
/// и дизарм. Движение, исход и нокдаун — в <see cref="SharedHalberdChargeSystem"/>.
/// </summary>
public sealed partial class HalberdChargeSystem : SharedHalberdChargeSystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IRobustRandom _random = default!;

    private static readonly string[] WallCries = { "halberd-charge-cry-wall-1", "halberd-charge-cry-wall-2" };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HalberdWieldedComponent, DisarmedEvent>(OnDisarmed);
    }

    // ── Экшен ─────────────────────────────────────────────────

    protected override void OnHalberdWielded(Entity<HalberdChargeComponent> halberd, EntityUid user)
    {
        // После unwield ChargeActionEntity занулён — AddAction создаёт экшен с нуля, поэтому
        // недоистёкший кулдаун восстанавливаем вручную из ChargeCooldownEnd.
        _actions.AddAction(user, ref halberd.Comp.ChargeActionEntity, halberd.Comp.ChargeActionId);

        var curTime = Timing.CurTime;
        if (halberd.Comp.ChargeCooldownEnd > curTime && halberd.Comp.ChargeActionEntity is { } action)
            _actions.SetCooldown(action, curTime, halberd.Comp.ChargeCooldownEnd);
    }

    protected override void OnHalberdUnwielded(Entity<HalberdChargeComponent> halberd, EntityUid user)
    {
        // Конец кулдауна запоминаем до удаления экшена, иначе он потеряется при пересоздании.
        if (halberd.Comp.ChargeActionEntity is { } action
            && _actions.GetAction(action) is { } actionEnt
            && actionEnt.Comp.Cooldown is { } cooldown)
        {
            halberd.Comp.ChargeCooldownEnd = cooldown.End;
        }

        _actions.RemoveAction(halberd.Comp.ChargeActionEntity);
        halberd.Comp.ChargeActionEntity = null;
    }

    protected override void OnHalberdRemoved(Entity<HalberdChargeComponent> halberd)
    {
        _actions.RemoveAction(halberd.Comp.ChargeActionEntity);
        halberd.Comp.ChargeActionEntity = null;
        halberd.Comp.ChargeAudioStream = _audio.Stop(halberd.Comp.ChargeAudioStream);
    }

    // ── Рывок ─────────────────────────────────────────────────

    protected override void OnChargeStarted(EntityUid user, Entity<HalberdChargeComponent> halberd)
    {
        _chat.TrySendInGameICMessage(user, Loc.GetString("halberd-charge-cry-start"), InGameICChatType.Speak, false);

        halberd.Comp.ChargeAudioStream = _audio.Stop(halberd.Comp.ChargeAudioStream);
        halberd.Comp.ChargeAudioStream = _audio.PlayPvs(halberd.Comp.ChargeLoopSound, user)?.Entity;
    }

    protected override void OnChargeEnded(EntityUid user, EntityUid halberdUid, HalberdChargeComponent? halberd,
        HalberdChargeEndReason reason, EntityUid? target)
    {
        if (halberd == null)
            return;

        halberd.ChargeAudioStream = _audio.Stop(halberd.ChargeAudioStream);

        switch (reason)
        {
            case HalberdChargeEndReason.HitEntity when target is { } hit:
                var damage = new DamageSpecifier();
                damage.DamageDict["Slash"] = (FixedPoint2) halberd.ChargeDamage;
                _damageable.TryChangeDamage(hit, damage, origin: user);
                _chat.TrySendInGameICMessage(user, Loc.GetString("halberd-charge-cry-hit"), InGameICChatType.Speak, false);
                break;

            case HalberdChargeEndReason.Wall:
                _chat.TrySendInGameICMessage(user, Loc.GetString(_random.Pick(WallCries)), InGameICChatType.Speak, false);
                break;

            case HalberdChargeEndReason.KnockedDown:
                _chat.TrySendInGameICMessage(user, Loc.GetString("halberd-charge-cry-knockdown"), InGameICChatType.Speak, false);
                break;
        }

        // Кулдаун отсчитывается от конца рывка, а не от нажатия.
        if (halberd.ChargeActionEntity is { } action)
            _actions.StartUseDelay(action);
    }

    // ── Дизарм ────────────────────────────────────────────────

    private void OnDisarmed(EntityUid uid, HalberdWieldedComponent comp, ref DisarmedEvent args)
    {
        // Алебарда не выпадает ни от чего, кроме успешного дизарма — роняем её в обход DropAttempt.
        if (!args.IsStunned)
            return;

        if (_hands.IsHolding(args.Target, comp.Halberd, out _))
            _hands.TryDrop(args.Target, comp.Halberd, checkActionBlocker: false);
    }
}
