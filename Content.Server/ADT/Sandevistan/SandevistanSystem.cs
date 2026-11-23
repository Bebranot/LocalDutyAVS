using Content.Server.Chat.Managers; // _Duty
using Content.Shared.ADT.Abilities;
using Content.Shared.ADT.Trail;
using Content.Shared.ADT.Sandevistan;
using Content.Shared._Duty.Cyberware; // _Duty
using Content.Shared._Duty.HealthPhrases; // _Duty
using Content.Shared._Duty.Sandevistan; // _Duty
using Content.Shared.Actions;
using Content.Shared.Body.Systems; // _Duty
using Content.Shared.Chat; // _Duty
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Server.Emp;
using Content.Shared.FixedPoint; // _Duty
using Content.Shared.Jittering;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffectNew; // _Duty
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Server.Player; // _Duty
using Robust.Shared.Player; // _Duty
using Robust.Shared.Prototypes; // _Duty
using Robust.Shared.Random; // _Duty
using Robust.Shared.Timing;
using Robust.Shared.Utility; // _Duty
using System.Numerics;
using Content.Shared.Emp;

namespace Content.Server.ADT.Sandevistan;

public sealed class SandevistanSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!; // _Duty
    [Dependency] private IChatManager _chat = default!; // _Duty
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedJitteringSystem _jittering = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IPlayerManager _player = default!; // _Duty
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!; // _Duty
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!; // _Duty
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private IGameTiming _timing = default!;

    // _Duty: иммуноблокатор и красные предупреждения в чат
    private static readonly EntProtoId ImmunoblockerEffectProto = "StatusEffectDutyImmunoblocker";
    private static readonly Color OverloadChatColor = Color.FromHex(DutyHealthPhrasesVisuals.ScreamColorHex);
    private static readonly string[] OveruseChatKeys =
    {
        "sandevistan-overuse-chat-1",
        "sandevistan-overuse-chat-2",
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SandevistanUserComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SandevistanUserComponent, ToggleSandevistanEvent>(OnToggle);
        SubscribeLocalEvent<SandevistanUserComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<SandevistanUserComponent, MeleeAttackEvent>(OnMeleeAttack);
        SubscribeLocalEvent<SandevistanUserComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<SandevistanUserComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SandevistanUserComponent, EmpPulseEvent>(OnEmpPulse);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ActiveSandevistanUserComponent, SandevistanUserComponent>();
        while (query.MoveNext(out var uid, out _, out var comp))
        {
            if (comp.DisableAt != null && _timing.CurTime > comp.DisableAt)
            {
                // _Duty: при штатном завершении не шлём StopImmediate, чтобы доиграл exit-звук
                Disable(uid, comp, immediate: !comp.GracefulDeactivating);
                continue;
            }

            if (comp.Trail != null)
            {
                comp.Trail.Color = Color.FromHsv(new Vector4(comp.ColorAccumulator % 100f / 100f, 1, 1, 1));
                comp.ColorAccumulator++;
                Dirty(uid, comp.Trail);
            }

            comp.CurrentLoad += comp.LoadPerActiveSecond * frameTime;

            var stateActions = new Dictionary<int, Action>
            {
                { 1, () => _jittering.DoJitter(uid, comp.StatusEffectTime, true)},
                { 2, () => _stamina.TakeStaminaDamage(uid, comp.StaminaDamage * frameTime)},
                { 3, () => _damageable.TryChangeDamage(uid, comp.Damage * frameTime, ignoreResistances: true)},
                { 4, () => _stun.TryKnockdown(uid, comp.StatusEffectTime, true)},
                { 5, () => Disable(uid, comp)},
                { 6, () => _damageable.TryChangeDamage(uid, comp.Damage * frameTime, ignoreResistances: true)},
            };

            var filteredStates = new List<int>();
            foreach (var stateThreshold in comp.Thresholds)
                if (comp.CurrentLoad >= stateThreshold.Value)
                    filteredStates.Add((int)stateThreshold.Key);

            filteredStates.Sort((a, b) => b.CompareTo(a));
            foreach (var state in filteredStates)
                if (stateActions.TryGetValue(state, out var action))
                    action();

            if (comp.NextPopupTime > _timing.CurTime)
                continue;

            var popup = -1;
            foreach (var state in filteredStates)
                if (state > popup && state < 4) // Goida
                    popup = state;

            if (popup == -1)
                continue;

            _popup.PopupCursor(Loc.GetString("sandevistan-overload-" + popup), uid, PopupType.LargeCaution);
            comp.NextPopupTime = _timing.CurTime + comp.PopupDelay;
        }

        // _Duty: обработка пост-эффекта отката, спада счётчика UsesCount и синхронизации HUD
        var inactiveQuery = EntityQueryEnumerator<SandevistanUserComponent>();
        while (inactiveQuery.MoveNext(out var uid, out var comp))
        {
            if (comp.Active != null)
            {
                SyncCyberHud(uid, comp);
                continue;
            }

            if (comp.CrashUntil is { } crashUntil)
            {
                if (_timing.CurTime < crashUntil)
                {
                    if (_mobState.IsAlive(uid))
                        _stamina.TakeStaminaDamage(uid, comp.CrashStaminaRate * frameTime);
                }
                else
                {
                    comp.CrashUntil = null;
                    _speed.RefreshMovementSpeedModifiers(uid);
                }
            }

            if (comp.UsesCount > 0)
            {
                var hasImmuno = _statusEffects.HasStatusEffect(uid, ImmunoblockerEffectProto);
                comp.UseDecayElapsed += frameTime * (hasImmuno ? 2f : 1f);
                if (comp.UseDecayElapsed >= comp.UseDecayIntervalSeconds)
                {
                    comp.UsesCount--;
                    comp.UseDecayElapsed -= comp.UseDecayIntervalSeconds;
                }
            }

            SyncCyberHud(uid, comp);
        }
    }

    private void OnStartup(Entity<SandevistanUserComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.ActionUid = _actions.AddAction(ent, ent.Comp.ActionProto);
        ent.Comp.EmpLastPulse = _timing.CurTime - ent.Comp.EmpCooldown;
        SyncCyberHud(ent, ent.Comp); // _Duty
    }

    private void OnToggle(Entity<SandevistanUserComponent> ent, ref ToggleSandevistanEvent args)
    {
        if (_timing.CurTime < ent.Comp.EmpLastPulse + ent.Comp.EmpCooldown)
        {
            args.Handled = true;
            return;
        }

        // _Duty: нельзя активировать в крите или мёртвым
        if (ent.Comp.Active == null && !_mobState.IsAlive(ent))
        {
            args.Handled = true;
            return;
        }

        args.Handled = true;

        if (ent.Comp.Active != null)
        {
            // _Duty: повторное нажатие во время 0.6s хвоста не дублирует Deactivate
            if (ent.Comp.GracefulDeactivating)
                return;

            ent.Comp.GracefulDeactivating = true;
            ent.Comp.DisableAt = _timing.CurTime + TimeSpan.FromSeconds(0.6);
            RaiseNetworkEvent(new SandevistanAudioEvent(GetNetEntity(ent), SandevistanAudioPhase.Deactivate), Filter.Pvs(ent));
            return;
        }

        // _Duty: обязательный кулдаун после выключения (защита от спама)
        if (ent.Comp.CooldownUntil is { } cdUntil && _timing.CurTime < cdUntil)
        {
            var remaining = Math.Max(1, (int) Math.Ceiling((cdUntil - _timing.CurTime).TotalSeconds));
            _popup.PopupEntity(Loc.GetString("sandevistan-cooldown-popup", ("seconds", remaining)), ent, ent, PopupType.SmallCaution);
            args.Handled = true;
            return;
        }

        ent.Comp.GracefulDeactivating = false;
        ent.Comp.DisableAt = null;
        ent.Comp.CrashUntil = null; // _Duty
        ent.Comp.ActivatedAt = _timing.CurTime; // _Duty

        var elapsedInactive = MathF.Max(0f, (float) (_timing.CurTime - ent.Comp.LastEnabled).TotalSeconds);
        ent.Comp.CurrentLoad = MathF.Max(0f, ent.Comp.CurrentLoad + ent.Comp.LoadPerInactiveSecond * elapsedInactive);
        // _Duty: мгновенный скачок нагрузки при запуске (против 1-секундного мерцания)
        ent.Comp.CurrentLoad += ent.Comp.ActivationLoadSpike;
        ent.Comp.UsesCount++;
        ent.Comp.UseDecayElapsed = 0f;

        // _Duty: проверка безопасного лимита запусков и синергии с иммуноблокаторами
        var safeLimit = GetSafeLimit(ent, ent.Comp);
        if (ent.Comp.UsesCount >= safeLimit + 3)
        {
            SendOveruseChatWarning(ent, "sandevistan-overuse-chat-severe");
            _damageable.TryChangeDamage(ent.Owner, ent.Comp.OveruseBurstDamage, ignoreResistances: true);
            _bloodstream.TryModifyBloodLevel(ent.Owner, FixedPoint2.New(-12));
            _bloodstream.TryModifyBleedAmount(ent.Owner, 2.0f);
            _jittering.DoJitter(ent.Owner, ent.Comp.SevereConvulsionDuration, true, 12f, 6f);
            _stun.TryKnockdown(ent.Owner, ent.Comp.SevereConvulsionDuration, true);
            _popup.PopupEntity(Loc.GetString("sandevistan-severe-overuse-popup"), ent, ent, PopupType.LargeCaution);
            ent.Comp.CooldownUntil = _timing.CurTime + ent.Comp.SevereLockoutCooldown;
            ent.Comp.LastEnabled = _timing.CurTime;
            SyncCyberHud(ent, ent.Comp);
            return;
        }

        if (ent.Comp.UsesCount > safeLimit)
        {
            SendOveruseChatWarning(ent, _random.Pick(OveruseChatKeys));
            _damageable.TryChangeDamage(ent.Owner, ent.Comp.OveruseBurstDamage, ignoreResistances: true);
            _bloodstream.TryModifyBloodLevel(ent.Owner, FixedPoint2.New(-6));
            _bloodstream.TryModifyBleedAmount(ent.Owner, 1.0f);
            _jittering.DoJitter(ent.Owner, TimeSpan.FromSeconds(3), true, 8f, 4f);
        }

        ent.Comp.Active = EnsureComp<ActiveSandevistanUserComponent>(ent);
        _speed.RefreshMovementSpeedModifiers(ent.Owner);

        if (!HasComp<TrailComponent>(ent))
        {
            var trail = AddComp<TrailComponent>(ent);
            trail.RenderedEntity = ent;
            trail.LerpTime = 0.05f;
            trail.LerpDelay = TimeSpan.FromSeconds(1);
            trail.Lifetime = 0.5f;
            trail.Frequency = 0.06f;
            trail.AlphaLerpAmount = 0.3f;
            trail.MaxParticleAmount = 15;
            ent.Comp.Trail = trail;
        }

        if (!HasComp<SandevistanVisionComponent>(ent))
            ent.Comp.Overlay = AddComp<SandevistanVisionComponent>(ent);

        // _Duty: 3-фазное аудио на клиенте (activation -> 3.07s -> idle loop)
        RaiseNetworkEvent(new SandevistanAudioEvent(GetNetEntity(ent), SandevistanAudioPhase.Activate), Filter.Pvs(ent));
        SyncCyberHud(ent, ent.Comp); // _Duty
    }

    private void OnRefreshSpeed(Entity<SandevistanUserComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Active != null)
            args.ModifySpeed(ent.Comp.MovementSpeedModifier, ent.Comp.MovementSpeedModifier);
        // _Duty: замедление в фазе пост-отката (Post-Use Crash)
        else if (ent.Comp.CrashUntil is { } crashUntil && _timing.CurTime < crashUntil)
            args.ModifySpeed(ent.Comp.CrashSpeedModifier, ent.Comp.CrashSpeedModifier);
    }

    private void OnMeleeAttack(Entity<SandevistanUserComponent> ent, ref MeleeAttackEvent args)
    {
        if (ent.Comp.Active == null
            || !TryComp<MeleeWeaponComponent>(args.Weapon, out var weapon))
            return;

        var rate = weapon.NextAttack - _timing.CurTime; //weapon.AttackRate; breaks things when multiple systems modify NextAttack
        weapon.NextAttack -= rate - rate / ent.Comp.AttackSpeedModifier;
    }

    private void OnMobStateChanged(Entity<SandevistanUserComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            Disable(ent, ent.Comp, immediate: true);
    }

    private void OnShutdown(Entity<SandevistanUserComponent> ent, ref ComponentShutdown args)
    {
        Disable(ent, ent.Comp, immediate: true);
        Del(ent.Comp.ActionUid);

        // _Duty: сбрасываем индикатор Сандевистана в HUD при удалении компонента
        if (TryComp<DutyCyberHostComponent>(ent, out var host))
        {
            host.SandevistanPresent = false;
            host.SandevistanStatus = DutySandevistanHudStatus.Ready;
            host.SandevistanCooldownEnd = null;
            Dirty(ent, host);
        }
    }

    private void Disable(EntityUid uid, SandevistanUserComponent comp, bool immediate = true)
    {
        if (comp.Active == null)
            return;

        RemComp<ActiveSandevistanUserComponent>(uid);
        comp.Active = null;
        comp.DisableAt = null;
        comp.GracefulDeactivating = false;
        comp.LastEnabled = _timing.CurTime;
        comp.ColorAccumulator = 0;

        // _Duty: обязательный кулдаун после выключения = 12s + activeDuration * 1.5s, в пределах 15..45s
        var activeDuration = Math.Max(0f, (float) (_timing.CurTime - comp.ActivatedAt).TotalSeconds);
        var cdSeconds = Math.Clamp(
            (float) comp.CooldownBase.TotalSeconds + activeDuration * comp.CooldownDurationScale,
            (float) comp.CooldownMin.TotalSeconds,
            (float) comp.CooldownMax.TotalSeconds);
        comp.CooldownUntil = _timing.CurTime + TimeSpan.FromSeconds(cdSeconds);

        // _Duty: Post-Use Crash при выключении сверх безопасного лимита (смягчается иммуноблокаторами)
        var safeLimit = GetSafeLimit(uid, comp);
        if (comp.UsesCount > safeLimit && _mobState.IsAlive(uid))
        {
            var hasImmuno = _statusEffects.HasStatusEffect(uid, ImmunoblockerEffectProto);
            var crashDur = hasImmuno ? comp.CrashDuration * 0.5 : comp.CrashDuration;
            comp.CrashUntil = _timing.CurTime + crashDur;
            comp.CrashStaminaRate = hasImmuno ? comp.CrashStaminaDrainPerSecond * 0.5f : comp.CrashStaminaDrainPerSecond;

            if (!hasImmuno)
            {
                _jittering.DoJitter(uid, crashDur, true, 9f, 5f);
                _popup.PopupEntity(Loc.GetString("sandevistan-crash-popup"), uid, uid, PopupType.MediumCaution);
            }
            else
            {
                _jittering.DoJitter(uid, crashDur, true, 4f, 3f);
            }
        }

        _speed.RefreshMovementSpeedModifiers(uid);

        if (comp.Overlay != null)
        {
            RemComp(uid, comp.Overlay);
            comp.Overlay = null;
        }

        if (comp.Trail != null)
        {
            RemComp(uid, comp.Trail);
            comp.Trail = null;
        }

        // _Duty: при экстренной остановке (ЭМИ, смерть, снятие импланта) глушим звук немедленно
        if (immediate)
            RaiseNetworkEvent(new SandevistanAudioEvent(GetNetEntity(uid), SandevistanAudioPhase.StopImmediate), Filter.Pvs(uid));

        SyncCyberHud(uid, comp); // _Duty
    }

    // _Duty: безопасный лимит запусков с учётом иммуноблокаторов и перегруза киберсистемы
    private int GetSafeLimit(EntityUid uid, SandevistanUserComponent comp)
    {
        var limit = comp.BaseSafeLimit;
        var hasImmuno = _statusEffects.HasStatusEffect(uid, ImmunoblockerEffectProto);
        if (hasImmuno)
            limit += comp.ImmunoblockerSafeLimitBonus;
        else if (TryComp<DutyCyberHostComponent>(uid, out var host) && host.Load > host.LoadLimit)
            limit = Math.Max(1, limit - comp.OverloadSafeLimitPenalty);

        return limit;
    }

    // _Duty: синхронизация состояния Сандевистана и иммуноблокаторов в сетевой HUD киберсистемы
    private void SyncCyberHud(EntityUid uid, SandevistanUserComponent comp)
    {
        if (!TryComp<DutyCyberHostComponent>(uid, out var host))
            return;

        var safeLimit = GetSafeLimit(uid, comp);
        var hasImmuno = _statusEffects.HasStatusEffect(uid, ImmunoblockerEffectProto);
        var curTime = _timing.CurTime;

        DutySandevistanHudStatus status;
        if (comp.Active != null)
            status = DutySandevistanHudStatus.Active;
        else if (comp.CrashUntil is { } crash && crash > curTime)
            status = DutySandevistanHudStatus.Crash;
        else if (comp.UsesCount >= safeLimit + 3 && comp.CooldownUntil is { } lockCd && lockCd > curTime)
            status = DutySandevistanHudStatus.Overload;
        else if (comp.CooldownUntil is { } cd && cd > curTime)
            status = DutySandevistanHudStatus.Cooldown;
        else if (comp.UsesCount > safeLimit)
            status = DutySandevistanHudStatus.Overload;
        else
            status = DutySandevistanHudStatus.Ready;

        if (host.SandevistanPresent
            && host.SandevistanUses == comp.UsesCount
            && host.SandevistanSafeLimit == safeLimit
            && host.SandevistanStatus == status
            && host.SandevistanCooldownEnd == comp.CooldownUntil
            && host.ImmunoblockerActive == hasImmuno)
            return;

        host.SandevistanPresent = true;
        host.SandevistanUses = comp.UsesCount;
        host.SandevistanSafeLimit = safeLimit;
        host.SandevistanStatus = status;
        host.SandevistanCooldownEnd = comp.CooldownUntil;
        host.ImmunoblockerActive = hasImmuno;
        Dirty(uid, host);
    }

    // _Duty: красное предупреждение шрифтом Underdog (#FF0000) в чат при перегрузе Сандевистана
    private void SendOveruseChatWarning(EntityUid uid, string locKey)
    {
        if (!_player.TryGetSessionByEntity(uid, out var session))
            return;

        var message = Loc.GetString(locKey);
        var escaped = FormattedMessage.EscapeText(message);
        var wrapped = $"[color={DutyHealthPhrasesVisuals.ScreamColorHex}][tfont=\"{DutyHealthPhrasesVisuals.FontPrototypeId}\" size={DutyHealthPhrasesVisuals.WhisperFontSize} defaultFont=NotoSansDisplayItalic defaultSize=11]{escaped}[/tfont][/color]";

        _chat.ChatMessageToOne(ChatChannel.Local, message, wrapped, uid, false, session.Channel, OverloadChatColor);
    }

    private void OnEmpPulse(Entity<SandevistanUserComponent> ent, ref EmpPulseEvent args)
    {
        ent.Comp.EmpLastPulse = _timing.CurTime;
        var uid = ent.Owner;

        if (ent.Comp.Active != null)
        {
            ent.Comp.CurrentLoad += ent.Comp.EmpOverload;
            _damageable.TryChangeDamage(uid, ent.Comp.EmpDamage, ignoreResistances: true);
            _jittering.DoJitter(uid, TimeSpan.FromSeconds(30f), true);
            _stun.TryAddParalyzeDuration(uid, TimeSpan.FromSeconds(6f));
            Spawn("EffectSparks", Transform(uid).Coordinates);
            Disable(ent, ent.Comp, immediate: true);
        }
    }
}
