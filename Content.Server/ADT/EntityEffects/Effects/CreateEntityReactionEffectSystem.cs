using Content.Shared.ADT.EntityEffects.Effects;
using Content.Shared.EntityEffects;
using Robust.Shared.Timing;

namespace Content.Server.ADT.EntityEffects.Effects;

public sealed partial class CreateEntityReactionEffectSystem : EntityEffectSystem<TransformComponent, CreateEntityEvent>
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    protected override void Effect(Entity<TransformComponent> entity, ref EntityEffectEvent<CreateEntityEvent> args)
    {
        var uid = entity.Owner;
        var ev = args.Effect;
        var xform = entity.Comp;

        if (ev.Delay > 0)
        {
            // _Duty: было — ветка "с задержкой" спавнила сущности точно так же мгновенно,
            // как и ветка без задержки (просто с координатами, посчитанными до цикла);
            // само поле Delay нигде не читалось после этого и ни на что не влияло.
            var coords = _transform.GetMapCoordinates(uid, xform);
            _transform.AttachToGridOrMap(uid);
            var entityId = ev.Entity;
            var number = ev.Number;
            Robust.Shared.Timing.Timer.Spawn(TimeSpan.FromSeconds(ev.Delay), () =>
            {
                for (var i = 0; i < number; i++)
                {
                    var spawned = Spawn(entityId, coords);
                    _transform.AttachToGridOrMap(spawned);
                }
            });
        }
        else
        {
            for (var i = 0; i < ev.Number; i++)
            {
                var mapCoords = _transform.GetMapCoordinates(uid, xform);
                var spawned = Spawn(ev.Entity, mapCoords);
                _transform.AttachToGridOrMap(spawned);
            }
        }
    }
}