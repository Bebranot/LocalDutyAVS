// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Alert;
using Content.Shared.CCVar;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Input;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Input.Binding;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Movement;

/// <summary>
/// _Duty: спринт (третья ступень: шаг → бег → спринт), за основу взят held-спринт Goob.
/// Зажатие клавиши C даёт +30% к бегу и тратит отдельный пул выносливости
/// (<see cref="DutyStaminaComponent"/>). Скорость спринта режут ХП, занятые слоты и оружие
/// в руках; на нуле выносливости спринт становится медленнее обычного бега. Обычный бег и шаг
/// не трогаем. Эмоция над головой на нажатие/отпускание C, отдышка >15с — клиентский звук.
/// </summary>
public sealed class DutySprintSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private bool _enabled = true;

    /// <summary>Слоты, занятость которых даёт крошечный штраф к спринту.</summary>
    private static readonly (SlotFlags Flag, string Name)[] EncumberingSlots =
    {
        (SlotFlags.OUTERCLOTHING, "outerClothing"),
        (SlotFlags.HEAD, "head"),
        (SlotFlags.SUITSTORAGE, "suitstorage"),
        (SlotFlags.BACK, "back"),
    };

    private const float PerSlotPenalty = 0.015f;  // −1.5% за занятый слот
    private const float MinSlotFactor = 0.9f;     // кап штрафа за слоты
    private const float HpMinFactor = 0.65f;      // множитель у самого крита
    private const float OneHandedWeaponFactor = 0.95f;
    private const float TwoHandedWeaponFactor = 0.85f;
    private const float SprintFloor = 0.5f;       // абсолютный пол множителя
    private const float RefreshEpsilon = 0.01f;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_config, DutyCCVars.SprintEnabled, OnEnabledChanged, true);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.Sprint,
                InputCmdHandler.FromDelegate(OnSprintDown, OnSprintUp, handle: false, outsidePrediction: false))
            .Register<DutySprintSystem>();

        SubscribeLocalEvent<DutyStaminaComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSprint);

        // Пересчёт спринта при смене снаряжения/ранении (важно только пока зажат C).
        SubscribeLocalEvent<DutyStaminaComponent, DidEquipEvent>((u, _, _) => Refresh(u));
        SubscribeLocalEvent<DutyStaminaComponent, DidUnequipEvent>((u, _, _) => Refresh(u));
        SubscribeLocalEvent<DutyStaminaComponent, DidEquipHandEvent>((u, _, _) => Refresh(u));
        SubscribeLocalEvent<DutyStaminaComponent, DidUnequipHandEvent>((u, _, _) => Refresh(u));
        SubscribeLocalEvent<DutyStaminaComponent, DamageChangedEvent>((u, _, _) => Refresh(u));
        // NB: на вилдинг не подписываемся — пара (WieldableComponent, ItemWieldedEvent) уже занята
        // SharedWieldableSystem, а движок допускает лишь одну подписку на (компонент, событие).
        // Фактор «оружие в руках» и так пересчитывается каждый тик во время спринта.
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<DutySprintSystem>();
    }

    private void OnEnabledChanged(bool value)
    {
        _enabled = value;
        var query = EntityQueryEnumerator<DutyStaminaComponent>();
        while (query.MoveNext(out var uid, out _))
            _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }

    private void Refresh(EntityUid uid) => _movementSpeed.RefreshMovementSpeedModifiers(uid);

    // ── Ввод (клавиша C) ──────────────────────────────────────────────────────

    private void OnSprintDown(ICommonSession? session) => SetWantsSprint(session, true);

    private void OnSprintUp(ICommonSession? session) => SetWantsSprint(session, false);

    private void SetWantsSprint(ICommonSession? session, bool wants)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (session?.AttachedEntity is not { Valid: true } uid
            || !TryComp<DutyStaminaComponent>(uid, out var comp)
            || comp.WantsSprint == wants)
            return;

        comp.WantsSprint = wants;
        Dirty(uid, comp);

        if (wants)
            EnsureComp<ActiveDutyStaminaComponent>(uid);

        _movementSpeed.RefreshMovementSpeedModifiers(uid);

        // Эмоция над головой (не в чат) — только если жив; шлёт сервер на всю PVS.
        if (_net.IsServer && !_mobState.IsIncapacitated(uid))
        {
            var msg = Loc.GetString(wants ? "duty-sprint-emote-start" : "duty-sprint-emote-stop");
            _popup.PopupEntity(msg, uid, Filter.Pvs(uid, entityManager: EntityManager), true);
        }
    }

    // ── Скорость ──────────────────────────────────────────────────────────────

    private void OnRefreshSprint(Entity<DutyStaminaComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var mod = GetSprintModifier(ent);
        ent.Comp.LastSprintModifier = mod;
        args.ModifySpeed(1f, mod);
    }

    /// <summary>Эффективный множитель скорости бега/спринта (1.0 = обычный бег без изменений).</summary>
    public float GetSprintModifier(Entity<DutyStaminaComponent> ent)
    {
        if (!_enabled)
            return 1f;

        // Не спринтуем: обычный бег штрафуется при низкой выносливости.
        if (!ent.Comp.WantsSprint)
        {
            if (ent.Comp.Max > 0f && ent.Comp.Current < ent.Comp.LowRunThreshold * ent.Comp.Max)
                return ent.Comp.LowRunPenalty;
            return 1f;
        }

        var total = ent.Comp.SprintBonus
                    * GetHpFactor(ent)
                    * GetEnduranceFactor(ent.Comp)
                    * GetHandFactor(ent)
                    * GetSlotFactor(ent);

        return MathF.Max(SprintFloor, total);
    }

    private float GetEnduranceFactor(DutyStaminaComponent comp)
    {
        if (comp.Max <= 0f)
            return 1f;

        var frac = comp.Current / comp.Max;
        if (frac >= comp.WindedFraction)
            return 1f;

        var t = comp.WindedFraction <= 0f ? 1f : frac / comp.WindedFraction;
        return float.Lerp(comp.MinEnduranceFactor, 1f, t);
    }

    private float GetHpFactor(EntityUid uid)
    {
        if (!TryComp<DamageableComponent>(uid, out var dmg) || _damageable.GetTotalDamage((uid, dmg)) <= 0)
            return 1f;

        if (!_mobThreshold.TryGetThresholdForState(uid, MobState.Critical, out var threshold) || threshold <= 0)
            return 1f;

        var frac = Math.Clamp((_damageable.GetTotalDamage((uid, dmg)) / threshold.Value).Float(), 0f, 1f);
        return float.Lerp(1f, HpMinFactor, frac);
    }

    private float GetHandFactor(EntityUid uid)
    {
        var factor = 1f;
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            float f;
            if (TryComp<WieldableComponent>(held, out var wield) && wield.Wielded)
                f = TwoHandedWeaponFactor;
            else if (HasComp<GunComponent>(held) || HasComp<MeleeWeaponComponent>(held))
                f = OneHandedWeaponFactor;
            else
                continue;

            factor = MathF.Min(factor, f);
        }

        return factor;
    }

    private float GetSlotFactor(EntityUid uid)
    {
        var occupied = 0;
        foreach (var (_, name) in EncumberingSlots)
        {
            if (_inventory.TryGetSlotEntity(uid, name, out _))
                occupied++;
        }

        return MathF.Max(MinSlotFactor, 1f - occupied * PerSlotPenalty);
    }

    private float GetDrainPenaltyFraction(EntityUid uid)
    {
        var penalty = (1f - GetHpFactor(uid)) + (1f - GetSlotFactor(uid));
        return Math.Clamp(penalty, 0f, 1f);
    }

    private bool IsSprinting(EntityUid uid, DutyStaminaComponent comp)
    {
        return comp.WantsSprint
               && TryComp<InputMoverComponent>(uid, out var mover)
               && mover.Sprinting
               && mover.HasDirectionalMovement;
    }

    // ── Тик: расход / восстановление / отдышка ────────────────────────────────

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ActiveDutyStaminaComponent, DutyStaminaComponent>();
        while (query.MoveNext(out var uid, out _, out var comp))
        {
            var sprinting = IsSprinting(uid, comp);
            var old = comp.Current;
            var oldBreathing = comp.Breathing;

            if (sprinting)
            {
                comp.SprintElapsed += frameTime;

                if (comp.Current > 0f)
                {
                    var drainMult = float.Lerp(1f, comp.MaxDrainPenalty, GetDrainPenaltyFraction(uid));
                    comp.Current = MathF.Max(0f, comp.Current - comp.DrainPerSecond * drainMult * frameTime);

                    if (comp.Current <= 0f)
                    {
                        comp.Exhausted = true;
                        comp.NextRegen = now + TimeSpan.FromSeconds(comp.ExhaustRegenDelay);
                    }
                    else
                    {
                        comp.NextRegen = now + TimeSpan.FromSeconds(comp.PartialRegenDelay);
                    }
                }
            }
            else
            {
                comp.SprintElapsed = 0f;

                if (now >= comp.NextRegen && comp.Current < comp.Max)
                {
                    var rate = comp.Exhausted ? comp.ExhaustRegenRate : comp.PartialRegenRate;
                    comp.Current = MathF.Min(comp.Max, comp.Current + rate * frameTime);

                    if (comp.Current >= comp.Max)
                        comp.Exhausted = false;
                }
            }

            // Отдышка: старт после N секунд непрерывного спринта, держится до восстановления
            // до BreathingStopFraction (даже стоя на месте). Считаем ТОЛЬКО на сервере —
            // иначе клиентское предсказание выносливости гасит флаг раньше и звук обрывается.
            if (_net.IsServer)
            {
                if (sprinting && comp.SprintElapsed >= comp.BreathingStartSeconds)
                    comp.Breathing = true;
                else if (comp.Breathing && comp.Current >= comp.Max * comp.BreathingStopFraction)
                    comp.Breathing = false;
            }

            var staminaChanged = !MathHelper.CloseTo(old, comp.Current);

            if (staminaChanged)
            {
                UpdateAlert(uid, comp);

                if (MathF.Abs(GetSprintModifier((uid, comp)) - comp.LastSprintModifier) > RefreshEpsilon)
                    _movementSpeed.RefreshMovementSpeedModifiers(uid);
            }

            if (staminaChanged || comp.Breathing != oldBreathing)
                Dirty(uid, comp);

            if (!sprinting && comp.Current >= comp.Max && !comp.Breathing)
                RemComp<ActiveDutyStaminaComponent>(uid);
        }
    }

    private void UpdateAlert(EntityUid uid, DutyStaminaComponent comp)
    {
        if (comp.Current >= comp.Max)
        {
            _alerts.ClearAlert(uid, comp.EnduranceAlert);
            return;
        }

        // 4 ступени: <20% → анимированная stamina4 (severity 0), затем 3 уровня запаса.
        var frac = comp.Max <= 0f ? 0f : comp.Current / comp.Max;
        short severity = frac switch
        {
            < 0.20f => 0,
            < 0.45f => 1,
            < 0.70f => 2,
            _ => 3,
        };

        _alerts.ShowAlert(uid, comp.EnduranceAlert, severity);
    }
}
