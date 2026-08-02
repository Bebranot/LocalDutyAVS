// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma;
using Content.Shared._Duty.Trauma.Components;
using Content.Shared.Alert;
using Content.Shared.CCVar;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Gravity;
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
using Content.Shared.Standing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio.Systems;
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
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly SharedGravitySystem _gravity = default!;

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

    // _Duty Trauma: доп. расход выносливости при переломе торса (сложнее дышать на бегу).
    private const float TorsoFractureCrackDrainPenalty = 0.1f;
    private const float TorsoFractureFullDrainPenalty = 0.3f;
    private const float TorsoFractureOpenDrainPenalty = 0.5f;

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

        // Клавиша held: если игрок отсоединился или слёг с зажатой C, key-up до тела уже не доедет
        // и WantsSprint залип бы навсегда — тело осталось бы в «вечном спринте». Гасим флаг сами.
        SubscribeLocalEvent<DutyStaminaComponent, PlayerDetachedEvent>((u, c, _) => ClearWantsSprint(u, c));
        SubscribeLocalEvent<DutyStaminaComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(Entity<DutyStaminaComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            ClearWantsSprint(ent.Owner, ent.Comp);
    }

    /// <summary>Принудительно снимает намерение спринтовать (отсоединение, крит, смерть).</summary>
    private void ClearWantsSprint(EntityUid uid, DutyStaminaComponent comp)
    {
        if (!comp.WantsSprint)
            return;

        comp.WantsSprint = false;
        comp.SprintElapsed = 0f;
        // Пауза как при обычном отпускании клавиши — чтобы её нельзя было обойти смертью или
        // переподключением с зажатой C.
        comp.NextSprintAllowed = _timing.CurTime + TimeSpan.FromSeconds(comp.SprintCooldown);
        Dirty(uid, comp);
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
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
        while (query.MoveNext(out var uid, out var comp))
        {
            // Выключили систему на ходу — Update больше не крутится, и недокрученная полоска
            // выносливости висела бы на экране навсегда. Гасим её здесь.
            if (!value)
                _alerts.ClearAlert(uid, comp.EnduranceAlert);

            _movementSpeed.RefreshMovementSpeedModifiers(uid);
        }
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

        // Нажатие при запрещающем состоянии — объясняем причину и не даём взвести флаг. Отпускание
        // пропускаем всегда, иначе флаг залипнет, если состояние изменилось с зажатой клавишей.
        if (wants)
        {
            if (GetSprintBlocker(uid) is { } blocker)
            {
                _popup.PopupClient(Loc.GetString(blocker), uid, uid);
                return;
            }

            if (_timing.CurTime < comp.NextSprintAllowed)
            {
                _popup.PopupClient(Loc.GetString("duty-sprint-blocked-cooldown"), uid, uid);
                return;
            }
        }
        else
        {
            // Отпустили клавишу — заводим паузу до следующего рывка.
            comp.NextSprintAllowed = _timing.CurTime + TimeSpan.FromSeconds(comp.SprintCooldown);
        }

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
        var penalty = (1f - GetHpFactor(uid)) + (1f - GetSlotFactor(uid)) + GetTorsoFracturePenalty(uid);
        return Math.Clamp(penalty, 0f, 1f);
    }

    /// <summary>_Duty: перелом рёбер (торса) — тем сложнее дышать при спринте, чем тяжелее тир.</summary>
    private float GetTorsoFracturePenalty(EntityUid uid)
    {
        if (!TryComp<FractureComponent>(uid, out var fracture)
            || !fracture.Zones.TryGetValue(BodyZone.Torso, out var state)
            || state.GetEffectiveTier() is not { } tier)
        {
            return 0f;
        }

        return tier switch
        {
            FractureTier.Crack => TorsoFractureCrackDrainPenalty,
            FractureTier.Full => TorsoFractureFullDrainPenalty,
            FractureTier.Open => TorsoFractureOpenDrainPenalty,
            _ => 0f,
        };
    }

    /// <summary>
    /// Реально ли существо сейчас спринтует: клавиша зажата, состояние тела позволяет, ходьба не
    /// включена и есть ввод направления. Публично — этим же предикатом клиент решает, пора ли
    /// сыпать пыль под ноги (<c>DutySprintVisualsSystem</c>), чтобы визуал не разъезжался
    /// с механикой.
    /// </summary>
    public bool IsSprinting(EntityUid uid, DutyStaminaComponent comp)
    {
        return comp.WantsSprint
               && CanSprint(uid)
               && TryComp<InputMoverComponent>(uid, out var mover)
               && mover.Sprinting
               && mover.HasDirectionalMovement;
    }

    /// <summary>
    /// Позволяет ли текущее состояние тела разгоняться. Проверяется не только при нажатии, но и
    /// каждый тик в <see cref="IsSprinting"/>: сбили с ног или заковали посреди забега — спринт
    /// обязан оборваться сам, а не доработать до отпускания клавиши.
    /// </summary>
    public bool CanSprint(EntityUid uid) => GetSprintBlocker(uid) is null;

    /// <summary>Ключ локали причины, по которой спринт запрещён, или null если разрешён.</summary>
    private string? GetSprintBlocker(EntityUid uid)
    {
        if (_standing.IsDown(uid))
            return "duty-sprint-blocked-lying";

        // Скованные руки — бежать можно, рвануть нельзя (как у Goob).
        if (TryComp<CuffableComponent>(uid, out var cuffs) && !cuffs.CanStillInteract)
            return "duty-sprint-blocked-restrained";

        // В невесомости отталкиваться не от чего.
        if (_gravity.IsWeightless(uid))
            return "duty-sprint-blocked-weightless";

        return null;
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

            // Звук рывка на фронте «побежал». Фронт держим только в первом предсказанном проходе:
            // клиентский Update за тик вызывается многократно при ре-предсказании, иначе фронт
            // съел бы непредсказанный проход и звук не сыграл бы вовсе.
            if (_timing.IsFirstTimePredicted)
            {
                if (sprinting && !comp.WasSprinting)
                    _audio.PlayPredicted(comp.SprintStartSound, uid, uid);

                comp.WasSprinting = sprinting;
            }

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

            // Пока клавиша зажата, маркер НЕ снимаем, даже если запас полон. Иначе: держим C,
            // останавливаемся, выносливость дотикивает до максимума, маркер уходит — и дальше
            // бежать можно бесплатно, потому что SetWantsSprint на зажатой клавише повторно не
            // вызовется (ранний выход по comp.WantsSprint == wants) и маркер уже не вернётся.
            if (!sprinting && !comp.WantsSprint && comp.Current >= comp.Max && !comp.Breathing)
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
