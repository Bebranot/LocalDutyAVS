namespace Content.Shared._Duty.Aiming;

/// <summary>
/// Навешивается на выпущенную пулю, когда стрелок вёл прицельный огонь ЛЁЖА
/// (см. <see cref="AimableComponent"/> + прицеливание лёжа). Пуля прошивает живые цели:
/// первой наносится полный урон, каждой следующей — со штрафом
/// (<see cref="FalloffMultiplier"/>), пока не исчерпан лимит <see cref="MaxTargets"/>.
/// Стена/неживой объект останавливают пулю. Серверная логика — в AimPenetrationSystem,
/// решение о "трате" пули — в Content.Server.Projectiles.ProjectileSystem.
/// </summary>
[RegisterComponent]
public sealed partial class AimPenetrationComponent : Component
{
    /// <summary>Максимум живых целей, которые прошивает пуля (включая первую).</summary>
    [DataField]
    public int MaxTargets = 2;

    /// <summary>Множитель урона для второй и последующих целей (0.35 = 35% от урона).</summary>
    [DataField]
    public float FalloffMultiplier = 0.35f;

    /// <summary>Сколько живых целей уже поражено.</summary>
    [ViewVariables]
    public int Hits;

    /// <summary>Уже поражённые цели — чтобы не бить одну и ту же дважды.</summary>
    [ViewVariables]
    public HashSet<EntityUid> HitEntities = new();
}
