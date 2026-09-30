using Content.Shared._Duty;
using Content.Shared.ADT.SpaceWhale;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Events;

namespace Content.Server._Duty.SpaceWhales.Systems;

/// <summary>
/// Forwards damage taken by Devourer tail segments to the worm head (shared HP pool),
/// and instantly kills anything the Devourer head rams into.
/// </summary>
public sealed partial class DutyDevourerSegmentSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DutyDevourerSegmentComponent, BeforeDamageChangedEvent>(OnBeforeDamage);
        SubscribeLocalEvent<DutyDevourerRammingComponent, StartCollideEvent>(OnHeadCollide);
    }

    private void OnBeforeDamage(EntityUid uid, DutyDevourerSegmentComponent comp, ref BeforeDamageChangedEvent args)
    {
        var netUid = EntityManager.GetNetEntity(uid);

        // Find head whose TailedEntityComponent contains this segment
        var query = AllEntityQuery<TailedEntityComponent>();
        while (query.MoveNext(out var headUid, out var tail))
        {
            if (tail.TailSegments.Contains(netUid))
            {
                _damageable.TryChangeDamage(headUid, args.Damage);
                args.Cancelled = true;
                return;
            }
        }
    }

    private void OnHeadCollide(Entity<DutyDevourerRammingComponent> ent, ref StartCollideEvent args)
    {
        var other = args.OtherEntity;
        if (!TryComp<MobStateComponent>(other, out var mobState) || mobState.CurrentState == MobState.Dead)
            return;

        _mobState.ChangeMobState(other, MobState.Dead, mobState);
    }
}