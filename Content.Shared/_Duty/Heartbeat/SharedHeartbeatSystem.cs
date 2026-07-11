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
public abstract class SharedHeartbeatSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <summary>Ниже этой «живучести» пациент считается на грани смерти (мед-алерт в анализаторе).</summary>
    public const float NearDeathFraction = 0.10f;

    /// <summary>
    /// «Живучесть» сущности: 1 = полное HP, 0 = у порога крита, отрицательное = в крите
    /// на пути к смерти (−1 = у порога смерти). Значение непрерывно проходит через ноль.
    /// </summary>
    public float GetVitalFraction(EntityUid uid, DamageableComponent? dmg = null)
    {
        if (!Resolve(uid, ref dmg, false))
            return 1f;

        if (!_mobThreshold.TryGetThresholdForState(uid, MobState.Critical, out var crit) || crit is not { } critT || critT <= 0)
            return 1f;

        var total = _damageable.GetTotalDamage((uid, dmg));

        if (total <= critT)
            return Math.Clamp(1f - (total / critT).Float(), 0f, 1f);

        // Уже в крите: уходим в минус от порога крита к порогу смерти.
        if (_mobThreshold.TryGetThresholdForState(uid, MobState.Dead, out var dead)
            && dead is { } deadT && deadT > critT)
        {
            var deep = ((total - critT) / (deadT - critT)).Float();
            return -Math.Clamp(deep, 0f, 1f);
        }

        return 0f;
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
                var deep = -GetVitalFraction(uid); // 0..1 глубина крита
                return deep >= comp.CriticalDeepFraction ? HeartbeatLevel.Critical : HeartbeatLevel.Heavy;

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
