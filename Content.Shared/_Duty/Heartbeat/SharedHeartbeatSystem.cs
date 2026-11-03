using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Heartbeat;

/// <summary>
/// _Duty: общая математика пульса — расчёт «живучести» (доли HP) и уровня пульса из
/// урона и mob-порогов. Одинаково на клиенте и сервере, поэтому анализатор здоровья
/// (сервер) и тело (сервер) считают уровень по одним и тем же порогам.
///
/// Воспроизведение звука тела живёт в серверной <c>HeartbeatSystem</c>; звук в
/// анализаторе — в клиентской <c>HealthAnalyzerAudioSystem</c>.
/// </summary>
public abstract partial class SharedHeartbeatSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Ниже этой «живучести» пациент считается на грани смерти (мед-алерт в анализаторе).</summary>
    public const float NearDeathFraction = 0.10f;

    /// <summary>
    /// «Живучесть» сущности: 1 = полное HP, 0 = у порога недееспособности (софт-крит, если он
    /// есть, иначе крит), отрицательное = за ним на пути к смерти (−1 = у порога смерти).
    /// Значение непрерывно проходит через ноль.
    /// </summary>
    /// <remarks>
    /// Ноль — именно первый порог выхода из строя: с софт-критом порог крита отодвинулся на 150,
    /// и при 99 урона (уже на грани софт-крита) пациент выглядел бы «на треть здоровым».
    /// </remarks>
    public float GetVitalFraction(EntityUid uid, DamageableComponent? dmg = null)
    {
        if (!Resolve(uid, ref dmg, false) || !TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return 1f;

        if (!_mobThreshold.TryGetIncapThreshold(uid, out var incap, thresholds) || incap is not { } incapT || incapT <= 0)
            return 1f;

        var total = _damageable.GetTotalDamage((uid, dmg));

        if (total <= incapT)
            return Math.Clamp(1f - (total / incapT).Float(), 0f, 1f);

        // Уже выведен из строя: уходим в минус от порога недееспособности к порогу смерти.
        if (_mobThreshold.TryGetThresholdForState(uid, MobState.Dead, out var dead, thresholds)
            && dead is { } deadT && deadT > incapT)
        {
            var deep = ((total - incapT) / (deadT - incapT)).Float();
            return -Math.Clamp(deep, 0f, 1f);
        }

        return 0f;
    }

    /// <summary>Глубина настоящего крита: 0 = у порога крита, 1 = у порога смерти.</summary>
    private float GetCritDepth(EntityUid uid)
    {
        if (!TryComp<MobThresholdsComponent>(uid, out var thresholds)
            || !_mobThreshold.TryGetThresholdForState(uid, MobState.Critical, out var crit, thresholds)
            || crit is not { } critT
            || !_mobThreshold.TryGetThresholdForState(uid, MobState.Dead, out var dead, thresholds)
            || dead is not { } deadT
            || deadT <= critT)
        {
            return 0f;
        }

        var total = _damageable.GetTotalDamage(uid);
        return Math.Clamp(((total - critT) / (deadT - critT)).Float(), 0f, 1f);
    }

    /// <summary>Текущий уровень пульса по mob-состоянию и доле HP.</summary>
    public HeartbeatLevel GetLevel(EntityUid uid, HeartbeatComponent? comp = null)
    {
        // Пороги берём из компонента (тюнятся датафилдами); без компонента — пульса нет.
        if (!Resolve(uid, ref comp, false))
            return HeartbeatLevel.None;

        if (!TryComp<MobStateComponent>(uid, out var mob))
            return HeartbeatLevel.None;

        switch (mob.CurrentState)
        {
            case MobState.Dead:
                return HeartbeatLevel.None;

            case MobState.Critical:
                var deep = GetCritDepth(uid); // 0..1 глубина крита
                return deep >= comp.CriticalDeepFraction ? HeartbeatLevel.Critical : HeartbeatLevel.Heavy;

            // Софт-крит попадает сюда: «живучесть» уже отрицательная — тяжёлый пульс.
            default:
                var hp = GetVitalFraction(uid); // 0..1
                if (hp < comp.HeavyHpThreshold)
                    return HeartbeatLevel.Heavy;
                if (hp < comp.LightHpThreshold)
                    return HeartbeatLevel.Light;
                return HeartbeatLevel.None;
        }
    }

    /// <summary>Пациент в mob-состоянии Critical (триггер писка монитора).</summary>
    public bool IsInCrit(EntityUid uid)
    {
        return TryComp<MobStateComponent>(uid, out var mob) && mob.CurrentState == MobState.Critical;
    }

    /// <summary>
    /// Заглушка «второй жизни» (Лазарус) сейчас активна — все наши звуки (тело и анализатор)
    /// должны молчать. Общая проверка для <c>HeartbeatSystem</c> и <c>HealthAnalyzerSystem</c>,
    /// чтобы не дублировать сравнение с <see cref="IGameTiming.CurTime"/> в двух местах.
    /// </summary>
    public bool IsSuppressed(EntityUid uid, HeartbeatComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return false;

        return _timing.CurTime < comp.SuppressUntil;
    }
}
