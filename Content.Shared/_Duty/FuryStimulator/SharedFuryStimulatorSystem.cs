using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Maths;

namespace Content.Shared._Duty.FuryStimulator;

/// <summary>
/// _Duty: общая математика и предсказываемые эффекты Fury-16.
/// Модель таймерная (4 фазы). Здесь — таблицы силы баффов по фазам и предсказываемые эффекты
/// (скорость движения, скорость ближней атаки, разброс/скорострельность). Авторитетная логика
/// (таймер фаз, урон, боль, музыка, передоз) — в серверном <c>FuryStimulatorSystem</c>.
/// </summary>
public abstract class SharedFuryStimulatorSystem : EntitySystem
{
    // ── Сила баффов/дебаффов (на пике; для расчёта множителей) ──

    public const float MoveSpeedBonus = 2.08f;   // +208% на пике (итоговая скорость ×3.08 = ×1.10 к прежней ×2.80)
    public const float DamageResist = 0.50f;     // -50% урона на пике
    public const float MeleeRateBonus = 0.35f;   // +35% скорости атаки на пике

    /// <summary>Скорострельность делится на (1 + FireRatePenalty·gunFactor). На пике ×0.2 (÷5).</summary>
    public const float FireRatePenalty = 4f;

    public const float SpreadMaxDegrees = 90f;
    public const float SpreadMinDegrees = 70f;
    public const float SpreadIncreaseDegrees = 30f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FuryStimulatorComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<FuryMeleeBonusComponent, GetMeleeAttackRateEvent>(OnGetMeleeRate);
        SubscribeLocalEvent<FuryGunPenaltyComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);
    }

    // ── Таблицы силы по фазам ─────────────────────────────────

    /// <summary>
    /// Общий множитель баффов (скорость/резист/ближний бой): ввод 0, разгон ⅓, пик 1, спад ½.
    /// </summary>
    public static float BuffFactor(FuryStage stage) => stage switch
    {
        FuryStage.RampUp => 1f / 3f,
        FuryStage.Peak => 1f,
        FuryStage.Decline => 0.5f,
        _ => 0f,
    };

    /// <summary>
    /// Сила дебаффа огнестрела: только пик (1) и спад (½). На вводе и разгоне — нет.
    /// </summary>
    public static float GunFactor(FuryStage stage) => stage switch
    {
        FuryStage.Peak => 1f,
        FuryStage.Decline => 0.5f,
        _ => 0f,
    };

    /// <summary>Неуязвимость к боли: только пик и спад.</summary>
    public static bool IsPainImmune(FuryStage stage) => stage is FuryStage.Peak or FuryStage.Decline;

    /// <summary>Множитель силы визуала (тряска/виньетка) по фазам.</summary>
    public static float VisualIntensity(FuryStage stage) => stage switch
    {
        FuryStage.Intro => 0.35f,
        FuryStage.RampUp => 0.55f,
        FuryStage.Peak => 1f,
        FuryStage.Decline => 0.5f,
        _ => 0f,
    };

    // ── Предсказываемые эффекты ───────────────────────────────

    private void OnRefreshMovementSpeed(EntityUid uid, FuryStimulatorComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        var factor = BuffFactor(comp.Stage);
        if (factor <= 0f)
            return;

        args.ModifySpeed(1f + MoveSpeedBonus * factor);
    }

    private void OnGetMeleeRate(Entity<FuryMeleeBonusComponent> ent, ref GetMeleeAttackRateEvent args)
    {
        args.Multipliers *= 1f + MeleeRateBonus * ent.Comp.Factor;
    }

    private void OnGunRefreshModifiers(Entity<FuryGunPenaltyComponent> ent, ref GunRefreshModifiersEvent args)
    {
        var factor = ent.Comp.Factor;
        if (factor <= 0f)
            return;

        args.FireRate /= 1f + FireRatePenalty * factor;
        args.MaxAngle += Angle.FromDegrees(SpreadMaxDegrees * factor);
        args.MinAngle += Angle.FromDegrees(SpreadMinDegrees * factor);
        args.AngleIncrease += Angle.FromDegrees(SpreadIncreaseDegrees * factor);
    }
}
