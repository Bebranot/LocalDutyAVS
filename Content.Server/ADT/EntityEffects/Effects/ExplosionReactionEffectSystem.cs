using Content.Shared.ADT.EntityEffects.Effects;
using Content.Shared.EntityEffects;
using Content.Shared.Explosion;
using Content.Shared.Explosion.EntitySystems;

namespace Content.Server.ADT.EntityEffects.Effects;

public sealed partial class ExplosionReactionEffectSystem : EntityEffectSystem<TransformComponent, ExplosionEvent>
{
    [Dependency] private readonly SharedExplosionSystem _explosion = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<ExplosionEvent> args)
    {
        var uid = entity.Owner;
        var ev = args.Effect;

        // _Duty: было `ev.MaxIntensity * args.Scale` без ограничения — при большом
        // Scale (например, много реагента) итоговая интенсивность росла неограниченно,
        // хотя поле называется "MaxIntensity" (максимум). Поле `IntensityPerUnit` при
        // этом вообще нигде не читалось. По аналогии с EmpReactionEffect
        // (RangePerUnit + MaxRange → Min(RangePerUnit * Scale, MaxRange)) интенсивность
        // теперь считается как per-unit ставка, ограниченная максимумом.
        var totalIntensity = Math.Min(ev.IntensityPerUnit * args.Scale, ev.MaxIntensity);

        void DoExplosion()
        {
            _explosion.QueueExplosion(
                uid,
                ev.ExplosionType,
                totalIntensity,
                ev.IntensitySlope,
                ev.MaxTotalIntensity,
                ev.TileBreakScale,
                canCreateVacuum: false,
                user: null,
                addLog: true
            );
        }

        // _Duty: `ev.Delay` тоже нигде не читался — взрыв всегда происходил мгновенно.
        if (ev.Delay > 0)
            Robust.Shared.Timing.Timer.Spawn(TimeSpan.FromSeconds(ev.Delay), DoExplosion);
        else
            DoExplosion();
    }
}