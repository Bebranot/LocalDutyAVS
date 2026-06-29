using Content.Shared._Duty.Weapons.MoraleBuff;
using Content.Shared.Alert;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Weapons.MoraleBuff;

/// <summary>
/// _Duty: серверная часть баффа морали — наложение/продление и снятие по таймеру.
/// Жизненный цикл (резист, игнор боли, скорость, очистка алерта) — в общем
/// <c>SharedMoraleBuffSystem</c>.
/// </summary>
public sealed class MoraleBuffSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MoraleBuffComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now >= comp.EndTime)
                RemCompDeferred<MoraleBuffComponent>(uid);
        }
    }

    /// <summary>
    /// Накладывает (или продлевает) бафф морали на цель на <paramref name="duration"/>.
    /// </summary>
    public void Apply(EntityUid target, TimeSpan duration)
    {
        var buff = EnsureComp<MoraleBuffComponent>(target); // ComponentStartup в SharedMoraleBuffSystem навесит игнор боли

        var end = _timing.CurTime + duration;
        if (end > buff.EndTime)
            buff.EndTime = end;

        // Показываем/обновляем иконку-статус с обратным отсчётом до конца баффа.
        _alerts.ShowAlert(target, buff.Alert, cooldown: (_timing.CurTime, buff.EndTime));
    }
}
