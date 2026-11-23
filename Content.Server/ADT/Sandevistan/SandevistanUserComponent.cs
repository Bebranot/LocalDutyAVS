using Content.Shared.FixedPoint;
using Content.Shared.ADT.Sandevistan;
using Content.Shared.ADT.Trail;
using Content.Shared.ADT.Abilities;
using Content.Shared.Damage;
using Robust.Shared.Audio;

// Ideally speaking this should be on the heart itself... but this also works.
namespace Content.Server.ADT.Sandevistan;

[RegisterComponent]
public sealed partial class SandevistanUserComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)]
    public ActiveSandevistanUserComponent? Active;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan? DisableAt;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan LastEnabled = TimeSpan.Zero;

    [DataField]
    public TimeSpan StatusEffectTime = TimeSpan.FromSeconds(5);

    [DataField]
    public TimeSpan PopupDelay = TimeSpan.FromSeconds(3);

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextPopupTime = TimeSpan.Zero;

    [DataField]
    public string ActionProto = "ActionToggleSandevistan";

    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? ActionUid;

    [ViewVariables(VVAccess.ReadWrite)]
    public float CurrentLoad = 0f; // Only updated when enabled

    [DataField]
    public float LoadPerActiveSecond = 1f;

    [DataField]
    public float LoadPerInactiveSecond = -0.5f;

    [DataField]
    public SortedDictionary<SandevistanState, FixedPoint2> Thresholds = new()
    {
        { SandevistanState.Warning, 8 },
        { SandevistanState.Shaking, 10 },
        { SandevistanState.Stamina, 14 },
        { SandevistanState.Damage, 16 },
        { SandevistanState.Death, 20 },
    };

    [DataField]
    public float StaminaDamage = 10f;

    // _Duty: каноничный урон нейроперегрузки вместо Blunt: 10
    [DataField]
    public DamageSpecifier Damage = new()
    {
        DamageDict = new()
        {
            { "Cellular", 2.5 },
            { "Asphyxiation", 3 },
            { "Heat", 1.5 },
        },
    };

    // _Duty: разовый импульс урона при активации сверх безопасного лимита
    [DataField]
    public DamageSpecifier OveruseBurstDamage = new()
    {
        DamageDict = new()
        {
            { "Cellular", 4 },
            { "Asphyxiation", 4 },
        },
    };

    // _Duty: баланс антагонистов — чуть мягче скорость и дилатация времени
    [DataField]
    public float MovementSpeedModifier = 2.1f;

    // _Duty: не используется — множитель скорости атак задан в прототипе StatusEffectDutySandevistanHaste
    [DataField]
    public float AttackSpeedModifier = 2.0f;

    // _Duty: коэффициент замедления времени на карте для окружающих сущностей
    [DataField]
    public float TimeDilationScale = 0.25f;

    // _Duty: защита от спама/мерцания — мгновенный скачок нагрузки и обязательный кулдаун
    [DataField]
    public float ActivationLoadSpike = 3.5f;

    [DataField]
    public TimeSpan CooldownBase = TimeSpan.FromSeconds(12);

    [DataField]
    public float CooldownDurationScale = 1.5f;

    [DataField]
    public TimeSpan CooldownMin = TimeSpan.FromSeconds(15);

    [DataField]
    public TimeSpan CooldownMax = TimeSpan.FromSeconds(45);

    [DataField]
    public TimeSpan SevereLockoutCooldown = TimeSpan.FromSeconds(50);

    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan? CooldownUntil;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan ActivatedAt = TimeSpan.Zero;

    // _Duty: счётчик запусков (нейроизнос) и синергия с иммуноблокаторами
    [ViewVariables(VVAccess.ReadWrite)]
    public int UsesCount;

    [ViewVariables(VVAccess.ReadOnly)]
    public float UseDecayElapsed;

    [DataField]
    public float UseDecayIntervalSeconds = 180f;

    [DataField]
    public int BaseSafeLimit = 3;

    [DataField]
    public int ImmunoblockerSafeLimitBonus = 2;

    [DataField]
    public int OverloadSafeLimitPenalty = 1;

    // _Duty: пост-эффект отката (Post-Use Crash) после выключения сверх лимита
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan? CrashUntil;

    [ViewVariables(VVAccess.ReadOnly)]
    public float CrashStaminaRate;

    [DataField]
    public TimeSpan CrashDuration = TimeSpan.FromSeconds(5);

    [DataField]
    public float CrashSpeedModifier = 0.7f;

    [DataField]
    public float CrashStaminaDrainPerSecond = 8f;

    [DataField]
    public TimeSpan SevereConvulsionDuration = TimeSpan.FromSeconds(3.5);

    // _Duty: идёт ли штатное затухание аудио (0.4s tail + fade), чтобы не обрывать exit-звук
    [ViewVariables(VVAccess.ReadOnly)]
    public bool GracefulDeactivating;

    [DataField]
    public SoundSpecifier? StartSound = new SoundPathSpecifier("/Audio/ADT/Misc/sande_start.ogg");

    [DataField]
    public SoundSpecifier? EndSound = new SoundPathSpecifier("/Audio/ADT/Misc/sande_end.ogg");

    [DataField] // So it fits the audio
    public TimeSpan ShiftDelay = TimeSpan.FromSeconds(1.9);

    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? RunningSound;

    [ViewVariables(VVAccess.ReadOnly)]
    public SandevistanVisionComponent? Overlay;

    [ViewVariables(VVAccess.ReadOnly)]
    public TrailComponent? Trail;

    [ViewVariables(VVAccess.ReadWrite)]
    public int ColorAccumulator = 0;

    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan EmpLastPulse = TimeSpan.Zero;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan EmpCooldown = TimeSpan.FromSeconds(60f);

    [ViewVariables(VVAccess.ReadWrite)]
    public float EmpOverload = 8f;

    [DataField]
    public DamageSpecifier EmpDamage = new()
    {
        DamageDict = new()
        {
            { "Shock", 40 },
        },
    };
}
