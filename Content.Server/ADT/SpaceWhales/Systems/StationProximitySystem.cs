using Content.Server.Popups;
using Content.Server.Station.Components;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Content.Shared.ADT.CCVar;
using Content.Server.ADT.MobCaller;
using System.Linq;
using Robust.Shared.Spawners;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;

namespace Content.Server.ADT.SpaceWhale.StationProximity;

public sealed class StationProximitySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _moveSpeed = default!;

    private const float CheckInterval = 1;
    private TimeSpan _nextCheck = TimeSpan.Zero;

    private EntityUid? _mobCaller;
    private bool _spawned = false;

    // Reused across CheckStationProximity calls to avoid allocating a new
    // Dictionary every second now that CheckInterval is 1s instead of 60s.
    private readonly Dictionary<EntityUid, (MapGridComponent Grid, TransformComponent Xform)> _stationsBuffer = new();

    public override void Initialize()
    {
        base.Initialize();
        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(CheckInterval);

        SubscribeLocalEvent<SpaceWhaleTargetComponent, MobStateChangedEvent>(OnTargetDeath);
        SubscribeLocalEvent<SpaceWhaleTargetComponent, ComponentShutdown>(OnTargetShutdown);
    }

    private void OnTargetDeath(Entity<SpaceWhaleTargetComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        RemComp<SpaceWhaleTargetComponent>(ent.Owner);
    }

    private void OnTargetShutdown(Entity<SpaceWhaleTargetComponent> ent, ref ComponentShutdown args)
    {
        StopFollowing(ent.Owner);
    }

    private void StopFollowing(Entity<SpaceWhaleTargetComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp, false))
            return;

        if (TryComp<MobCallerComponent>(ent.Comp.MobCaller, out var caller))
        {
            foreach (var item in caller.SpawnedEntities)
            {
                EnsureComp<TimedDespawnComponent>(item).Lifetime = 15f;
                _moveSpeed.ChangeBaseSpeed(item, 11, 30, 1);
                _moveSpeed.RefreshMovementSpeedModifiers(item);
            }
        }

        QueueDel(ent.Comp.MobCaller);
        _mobCaller = null;
        _spawned = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_mobCaller.HasValue && TerminatingOrDeleted(_mobCaller.Value))
        {
            _mobCaller = null;
            _spawned = false;
        }

        var query = EntityQueryEnumerator<SpaceWhaleTargetComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.MobCaller.HasValue || TerminatingOrDeleted(comp.MobCaller.Value))
            {
                RemCompDeferred(uid, comp);
                continue;
            }

            var caller = comp.MobCaller.Value.Comp;
            if (caller.SpawnedEntities.Count > 0)
            {
                _spawned = true;
                break;
            }
        }

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(CheckInterval);
        CheckStationProximity();
    }

    private void CheckStationProximity()
    {
        if (!_cfg.GetCVar(ADTCCVars.SpaceWhaleSpawn))
            return;

        var stationQuery = EntityQueryEnumerator<BecomesStationComponent, MapGridComponent>();
        _stationsBuffer.Clear();

        while (stationQuery.MoveNext(out var uid, out _, out var grid))
        {
            var xform = Transform(uid);
            _stationsBuffer.Add(uid, (grid, xform));
        }

        if (_stationsBuffer.Count == 0)
            return;

        var humanoidQuery = EntityQueryEnumerator<HumanoidProfileComponent, MobStateComponent, TransformComponent>();
        while (humanoidQuery.MoveNext(out var uid, out _, out var mobState, out var humanoidXform))
        {
            if (mobState.CurrentState != MobState.Alive)
                continue;

            var sameMap = false;
            foreach (var (_, (_, stationXform)) in _stationsBuffer)
            {
                if (stationXform.MapUid != humanoidXform.MapUid)
                    continue;

                sameMap = true;
                break;
            }

            if (!sameMap)
                continue;

            CheckHumanoidProximity(uid, _stationsBuffer, humanoidXform);
        }
    }

    private void CheckHumanoidProximity(EntityUid humanoid,
        Dictionary<EntityUid, (MapGridComponent Grid, TransformComponent Xform)> stations,
        TransformComponent humanoidTransform)
    {
        if (humanoidTransform.GridUid.HasValue && stations.TryGetValue(humanoidTransform.GridUid.Value, out _))
        {
            RemComp<SpaceWhaleTargetComponent>(humanoid);
            return;
        }

        var humanoidWorldPos = _transform.GetWorldPosition(humanoidTransform);
        var spawnDist = _cfg.GetCVar(ADTCCVars.SpaceWhaleSpawnDistance);

        foreach (var (stationUid, (grid, stationXform)) in stations)
        {
            if (stationXform.MapUid != humanoidTransform.MapUid)
                continue;

            var stationWorldPos = _transform.GetWorldPosition(stationXform);
            var delta = humanoidWorldPos - stationWorldPos;

            // удаление считается отдельно по координате X и по координате Y
            var dx = Math.Abs(delta.X);
            var dy = Math.Abs(delta.Y);

            if (dx < spawnDist && dy < spawnDist)
            {
                RemCompDeferred<SpaceWhaleTargetComponent>(humanoid);
                return;
            }
        }

        HandleFarFromStation(humanoid);
    }

    private void HandleFarFromStation(EntityUid entity) // basically handles space whale spawnings
    {
        if (_spawned)
            return;

        if (_mobCaller.HasValue)
            return;

        _popup.PopupEntity(
            Loc.GetString("station-proximity-far-from-station"),
            entity,
            entity,
            PopupType.LargeCaution);

        _audio.PlayGlobal(new SoundPathSpecifier("/Audio/_Duty/Effects/DevourerSpawn.ogg"),
            entity,
            AudioParams.Default.WithVolume(1f));

        // Spawn a dummy entity at the player's location and lock it onto the player
        _mobCaller = Spawn(null, Transform(entity).Coordinates);
        _transform.SetParent(_mobCaller.Value, entity);
        var mobCaller = new MobCallerComponent()
        {
            SpawnProto = "DutyUniverseDevourer", // _Duty - всегда Пожиратель Вселенных
            MaxAlive = 1,
            NeedAnchored = false,
            NeedPower = false,
            MinDistance = 75f,
            MaxDistance = 125f,
            OcclusionDistance = 200f,
            GridOcclusionDistance = 200f,
            SpawnSpacing = TimeSpan.FromSeconds(1),
        };

        AddComp(_mobCaller.Value, mobCaller);

        var targetComp = EnsureComp<SpaceWhaleTargetComponent>(entity);// track the dummy on the player
        targetComp.MobCaller = (_mobCaller.Value, mobCaller);
    }
}
