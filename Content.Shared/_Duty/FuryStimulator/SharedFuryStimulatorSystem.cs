using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Maths;

namespace Content.Shared._Duty.FuryStimulator;

/// <summary>
/// _Duty: общая математика и предсказываемые эффекты Fury-16.
/// Стадии выводятся из уровня вещества (единый стейт-машина). Здесь же живут баффы/дебаффы,
/// которые считаются на клиенте и сервере одинаково (скорость движения, скорость ближней атаки,
/// разброс/скорострельность оружия). Авторитетная логика (убывание, стадии, урон, музыка,
/// передоз) — в серверном <c>FuryStimulatorSystem</c>.
/// </summary>
public abstract class SharedFuryStimulatorSystem : EntitySystem
{
    // ── Уровни/пороги ─────────────────────────────────────────

    /// <summary>Максимальный безопасный объём.</summary>
    public const float MaxSafe = 50f;

    /// <summary>Свыше этого — мгновенный передоз.</summary>
    public const float OverdoseThreshold = 55f;

    public const float IntroLevel = 45f;   // [45..50] — ввод
    public const float PeakLevel = 25f;    // [25..45) — пик
    public const float DeclineLevel = 5f;  // [5..25)  — спад
    // (0..5) — выход; <=0 — нет эффекта

    // ── Сила баффов/дебаффов (на пике; на спаде — вдвое) ──────

    public const float MoveSpeedBonus = 0.40f;   // +40%
    public const float DamageResist = 0.50f;     // -50% урона
    public const float MeleeRateBonus = 0.35f;   // +35% скорости атаки

    /// <summary>Скорострельность делится на (1 + FireRatePenalty·factor). На пике ×0.2 (÷5).</summary>
    public const float FireRatePenalty = 4f;

    /// <summary>На сколько градусов раздуваем разброс на пике (factor=1).</summary>
    public const float SpreadMaxDegrees = 90f;
    public const float SpreadMinDegrees = 70f;
    public const float SpreadIncreaseDegrees = 30f;

    public override void Initialize()
    {
        base.Initialize();

        // Предсказываемые эффекты — считаются на клиенте и сервере.
        SubscribeLocalEvent<FuryStimulatorComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<FuryMeleeBonusComponent, GetMeleeAttackRateEvent>(OnGetMeleeRate);
        SubscribeLocalEvent<FuryGunPenaltyComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);
    }

    /// <summary>Стадия по уровню вещества.</summary>
    public static FuryStage LevelToStage(float level)
    {
        if (level <= 0f)
            return FuryStage.None;
        if (level >= IntroLevel)
            return FuryStage.Intro;
        if (level >= PeakLevel)
            return FuryStage.Peak;
        if (level >= DeclineLevel)
            return FuryStage.Decline;
        return FuryStage.Washout;
    }

    /// <summary>
    /// Множитель силы баффов/дебаффов для стадии: пик = 1, спад = 0.5, остальное = 0.
    /// </summary>
    public static float StageFactor(FuryStage stage) => stage switch
    {
        FuryStage.Peak => 1f,
        FuryStage.Decline => 0.5f,
        _ => 0f,
    };

    /// <summary>Множитель силы визуала (тряска/виньетка): пик = 1, спад = 0.5, ввод/выход = базовый 0.35.</summary>
    public static float VisualIntensity(FuryStage stage) => stage switch
    {
        FuryStage.Intro => 0.35f,
        FuryStage.Peak => 1f,
        FuryStage.Decline => 0.5f,
        FuryStage.Washout => 0.35f,
        _ => 0f,
    };

    // ── Предсказываемые эффекты ───────────────────────────────

    private void OnRefreshMovementSpeed(EntityUid uid, FuryStimulatorComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        var factor = StageFactor(comp.Stage);
        if (factor <= 0f)
            return;

        var mult = 1f + MoveSpeedBonus * factor;
        args.ModifySpeed(mult);
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

        // Скорострельность вниз (÷5 на пике).
        args.FireRate /= 1f + FireRatePenalty * factor;

        // Разброс вверх — пули летят «куда угодно».
        args.MaxAngle += Angle.FromDegrees(SpreadMaxDegrees * factor);
        args.MinAngle += Angle.FromDegrees(SpreadMinDegrees * factor);
        args.AngleIncrease += Angle.FromDegrees(SpreadIncreaseDegrees * factor);
    }
}
