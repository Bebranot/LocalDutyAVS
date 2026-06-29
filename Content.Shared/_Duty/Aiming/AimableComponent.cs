using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Aiming;

/// <summary>
/// Вешается на двуручное огнестрельное оружие. Позволяет владельцу прицеливаться (зажатие ПКМ),
/// удерживая оружие в обе руки (wielded). Хранит настройки прицеливания для конкретного оружия.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AimableComponent : Component
{
    /// <summary>Множитель скорости ходьбы во время прицеливания стоя.</summary>
    [DataField]
    public float WalkSpeedModifier = 0.6f;

    /// <summary>Множитель скорости бега во время прицеливания стоя.</summary>
    [DataField]
    public float SprintSpeedModifier = 0.5f;

    /// <summary>Множитель zoom (FOV) во время прицеливания стоя.</summary>
    [DataField]
    public float ZoomMultiplier = 1.2f;

    /// <summary>Множитель zoom (FOV) во время прицеливания лёжа — выше, чем стоя.</summary>
    [DataField]
    public float ProneZoomMultiplier = 1.4f;

    /// <summary>
    /// Множитель MinAngle/MaxAngle/AngleIncrease во время прицеливания (0.5 = разброс в два раза меньше).
    /// Умножение, а не вычитание — безопаснее для уже маленьких углов (после вычитания GunWieldBonus
    /// итоговый угол может стать отрицательным, что ломает GetRecoilAngle).
    /// </summary>
    [DataField]
    public float AimSpreadMultiplier = 0.5f;

    /// <summary>Множитель силы отдачи камеры во время прицеливания.</summary>
    [DataField]
    public float AimCameraRecoilScalar = 0.6f;

    /// <summary>
    /// Множитель скорострельности во время прицеливания ЛЁЖА (0.8 = на 20% медленнее).
    /// Стоя скорострельность не меняется. Баланс за устойчивую стрельбу с упора.
    /// </summary>
    [DataField]
    public float ProneFireRateMultiplier = 0.8f;

    /// <summary>Минимальная дистанция от персонажа до точки прицеливания (в тайлах) — ближе прицеливание не активируется.</summary>
    [DataField]
    public float MinAimDistance = 3.5f;

    /// <summary>
    /// Длительность обездвиживания после выхода из прицеливания лёжа — игрок "приходит в себя" перед тем как встать/уйти.
    /// </summary>
    [DataField]
    public TimeSpan PostProneAimImmobilizeDuration = TimeSpan.FromSeconds(2);

    // ── Прошивание пуль при стрельбе лёжа ──────────────────────────────────────

    /// <summary>Прошивает ли оружие цели при прицельной стрельбе лёжа.</summary>
    [DataField]
    public bool ProneAimPenetration = true;

    /// <summary>Максимум живых целей при прошивании лёжа (включая первую).</summary>
    [DataField]
    public int ProneAimPenetrationTargets = 2;

    /// <summary>Множитель урона второй и последующим целям при прошивании (0.35 = 35%).</summary>
    [DataField]
    public float ProneAimPenetrationFalloff = 0.35f;
}
