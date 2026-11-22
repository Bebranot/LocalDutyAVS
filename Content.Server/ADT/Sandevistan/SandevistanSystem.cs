using Content.Shared.ADT.Abilities;
using Content.Shared.ADT.Trail;
using Content.Shared.ADT.Sandevistan;
using Content.Shared._Duty.Sandevistan; // _Duty
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Server.Emp;
using Content.Shared.Jittering;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Player; // _Duty
using Robust.Shared.Timing;
using System.Numerics;
using Content.Shared.Emp;

namespace Content.Server.ADT.Sandevistan;

public sealed class SandevistanSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedJitteringSystem _jittering = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private IGameTiming _timing = default!;

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
    }

    private void OnStartup(Entity<SandevistanUserComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.ActionUid = _actions.AddAction(ent, ent.Comp.ActionProto);
        ent.Comp.EmpLastPulse = _timing.CurTime - ent.Comp.EmpCooldown;
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

        ent.Comp.GracefulDeactivating = false;
        ent.Comp.DisableAt = null;
        ent.Comp.Active = EnsureComp<ActiveSandevistanUserComponent>(ent);
        ent.Comp.CurrentLoad = MathF.Max(0, ent.Comp.CurrentLoad + ent.Comp.LoadPerInactiveSecond * (float)(_timing.CurTime - ent.Comp.LastEnabled).TotalSeconds);
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
    }

    private void OnRefreshSpeed(Entity<SandevistanUserComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Active != null)
            args.ModifySpeed(ent.Comp.MovementSpeedModifier, ent.Comp.MovementSpeedModifier);
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
