// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Concussion;
using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Gorlex;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Implants;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Cyberware.Gorlex;

/// <summary>
/// _Duty: «Кровавый контур». Один раз при входе в крит носитель на несколько секунд поднимается
/// (лечение до порога, как у Лазаря), затем жёсткий откат: глубокий крит, максимальная контузия и нокдаун.
/// Успокоительное в крови гасит контур заранее — модуль выгорает впустую.
/// </summary>
public sealed partial class DutyBloodLoopSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedConcussionSystem _concussion = default!;
    [Dependency] private SharedStunSystem _stun = default!;

    private const float UpdateInterval = 0.25f;
    private float _accumulator;

    private static readonly TimeSpan PendingDelay = TimeSpan.FromSeconds(0.5);
    private static readonly SoundSpecifier SurgeSound = new SoundCollectionSpecifier("DutyGorlexBloodLoop");

    private EntityQuery<DutyBloodLoopComponent> _loopQuery;

    public override void Initialize()
    {
        base.Initialize();

        _loopQuery = GetEntityQuery<DutyBloodLoopComponent>();

        SubscribeLocalEvent<DutyBloodLoopComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<DutyBloodLoopComponent, ImplantRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<DutyBloodLoopHostComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnImplanted(Entity<DutyBloodLoopComponent> ent, ref ImplantImplantedEvent args)
    {
        var host = EnsureComp<DutyBloodLoopHostComponent>(args.Implanted);
        // Второй контур не должен перебить уже сработавший: ссылку меняем, только если первый выгорел или пуст.
        if (host.Implant.IsValid() && _loopQuery.HasComp(host.Implant) && _cyber.IsModuleOnline(host.Implant))
            return;

        host.Implant = ent;
        host.Phase = DutyBloodLoopPhase.Armed;
    }

    private void OnRemoved(Entity<DutyBloodLoopComponent> ent, ref ImplantRemovedEvent args)
    {
        if (!TryComp<DutyBloodLoopHostComponent>(args.Implanted, out var host) || host.Implant != ent.Owner)
            return;

        if (_cyber.FindImplant<DutyBloodLoopComponent>(args.Implanted) is { } other)
        {
            host.Implant = other;
            host.Phase = DutyBloodLoopPhase.Armed;
            return;
        }

        RemComp<DutyBloodLoopHostComponent>(args.Implanted);
    }

    private void OnMobStateChanged(Entity<DutyBloodLoopHostComponent> ent, ref MobStateChangedEvent args)
    {
        if (ent.Comp.Phase != DutyBloodLoopPhase.Armed || args.NewMobState != MobState.Critical)
            return;

        if (!_loopQuery.TryComp(ent.Comp.Implant, out var loop) || !_cyber.IsModuleOnline(ent.Comp.Implant))
            return;

        if (IsSuppressed(ent, loop))
        {
            Quench(ent, "duty-gorlex-bloodloop-quenched");
            return;
        }

        // Лечить прямо внутри события смены состояния нельзя — состояние пересчитается вложенно; ждём тик.
        ent.Comp.Phase = DutyBloodLoopPhase.Pending;
        ent.Comp.PhaseUntil = _timing.CurTime + PendingDelay;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<DutyBloodLoopHostComponent>();
        while (query.MoveNext(out var uid, out var host))
        {
            if (host.Phase is DutyBloodLoopPhase.Armed or DutyBloodLoopPhase.Done)
                continue;

            if (!_loopQuery.TryComp(host.Implant, out var loop))
                continue;

            var ent = new Entity<DutyBloodLoopHostComponent>(uid, host);

            if (_mobState.IsDead(uid))
            {
                Finish(ent, burned: true);
                continue;
            }

            if (host.Phase == DutyBloodLoopPhase.Pending)
            {
                if (now < host.PhaseUntil)
                    continue;

                if (!_mobState.IsCritical(uid))
                {
                    // Медик или удача успели раньше — контур остаётся заряженным.
                    host.Phase = DutyBloodLoopPhase.Armed;
                    continue;
                }

                Rise(ent, loop);
                continue;
            }

            // Active
            if (IsSuppressed(ent, loop))
            {
                Quench(ent, "duty-gorlex-bloodloop-quenched");
                continue;
            }

            if (_mobState.IsCritical(uid))
                HealToCritFraction(uid, loop.HealToCritFraction);

            if (now >= host.PhaseUntil)
                Rollback(ent, loop);
        }
    }

    private void Rise(Entity<DutyBloodLoopHostComponent> ent, DutyBloodLoopComponent loop)
    {
        HealToCritFraction(ent, loop.HealToCritFraction);

        ent.Comp.Phase = DutyBloodLoopPhase.Active;
        ent.Comp.PhaseUntil = _timing.CurTime + loop.ActiveDuration;
        _cyber.SetModuleState(ent.Comp.Implant, DutyCyberModuleState.Active, ent.Comp.PhaseUntil);

        _audio.PlayPvs(SurgeSound, ent);
        _popup.PopupEntity(Loc.GetString("duty-gorlex-bloodloop-rise-self"), ent, ent, PopupType.LargeCaution);
        _popup.PopupEntity(Loc.GetString("duty-gorlex-bloodloop-rise-others", ("target", Identity.Entity(ent, EntityManager))),
            ent, Filter.PvsExcept(ent), true, PopupType.MediumCaution);
    }

    private void Rollback(Entity<DutyBloodLoopHostComponent> ent, DutyBloodLoopComponent loop)
    {
        var uid = ent.Owner;

        if (_thresholds.TryGetThresholdForState(uid, MobState.Critical, out var crit) &&
            _thresholds.TryGetThresholdForState(uid, MobState.Dead, out var dead) &&
            TryComp<DamageableComponent>(uid, out var damageable))
        {
            // Глубокий крит: доля пути от порога крита к порогу смерти. Урон идёт в обход сопротивлений.
            var target = crit.Value + (dead.Value - crit.Value) * loop.RollbackDepth;
            var current = _damageable.GetTotalDamage((uid, damageable));
            if (current < target)
            {
                var spec = new DamageSpecifier();
                spec.DamageDict["Blunt"] = target - current;
                _damageable.TryChangeDamage((uid, damageable), spec, ignoreResistances: true, interruptsDoAfters: false);
            }
        }

        // Контузия — прямым AddRaw: откат контур не гасится ни шлемом, ни барабанным демпфером.
        if (TryComp<ConcussionComponent>(uid, out var concussion))
            _concussion.AddRaw(uid, concussion.MaxLevel, concussion);

        _stun.TryAddParalyzeDuration(uid, loop.RollbackStun);

        _popup.PopupEntity(Loc.GetString("duty-gorlex-bloodloop-rollback"), uid, uid, PopupType.LargeCaution);
        Finish(ent, burned: true);
    }

    private void Quench(Entity<DutyBloodLoopHostComponent> ent, string popup)
    {
        _popup.PopupEntity(Loc.GetString(popup), ent, ent, PopupType.MediumCaution);
        Finish(ent, burned: true);
    }

    private void Finish(Entity<DutyBloodLoopHostComponent> ent, bool burned)
    {
        ent.Comp.Phase = DutyBloodLoopPhase.Done;
        if (burned)
            _cyber.SetModuleState(ent.Comp.Implant, DutyCyberModuleState.Burned);
    }

    private bool IsSuppressed(EntityUid host, DutyBloodLoopComponent loop)
    {
        if (!_solutions.TryGetSolution(host, BloodstreamComponent.DefaultBloodSolutionName, out _, out var solution))
            return false;

        foreach (var (reagent, amount) in loop.Suppressors)
        {
            if (solution.GetTotalPrototypeQuantity(reagent) >= amount)
                return true;
        }

        return false;
    }

    private void HealToCritFraction(EntityUid uid, float fraction)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable) ||
            !_thresholds.TryGetThresholdForState(uid, MobState.Critical, out var crit))
            return;

        var target = crit.Value * fraction;
        var current = _damageable.GetTotalDamage((uid, damageable));
        if (current <= target || current <= 0)
            return;

        var factor = target.Float() / current.Float();
        _damageable.SetDamage((uid, damageable), _damageable.GetAllDamage((uid, damageable)) * factor);
    }
}
