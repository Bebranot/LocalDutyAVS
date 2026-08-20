using Content.Server.Spawners.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.Server.Spawners.EntitySystems;

public sealed class SpawnOnDespawnSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpawnOnDespawnComponent, TimedDespawnEvent>(OnDespawn);
    }

    private void OnDespawn(EntityUid uid, SpawnOnDespawnComponent comp, ref TimedDespawnEvent args)
    {
        if (!TryComp(uid, out TransformComponent? xform))
            return;

        // Спавним по MapCoordinates, а не по EntityCoordinates деспавнящейся сущности:
        // при спавне через координаты, привязанные к её transform-цепочке, у заспавненной
        // (anchored: true, например FoamedAluminiumMetal) сущности иногда ещё не успевает
        // проставиться GridUid, и SharedTransformSystem логирует "Tried to anchor entity
        // to a grid different from its GridUid ()".
        var mapCoords = _transform.ToMapCoordinates(xform.Coordinates);
        Spawn(comp.Prototype, mapCoords);
    }

    public void SetPrototype(Entity<SpawnOnDespawnComponent> entity, EntProtoId prototype)
    {
        entity.Comp.Prototype = prototype;
    }
}
