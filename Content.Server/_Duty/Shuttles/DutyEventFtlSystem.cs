// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Body;
using Content.Shared.Database;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Station;
using Content.Shared.Station.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Shuttles;

public enum DutyEventFtlTarget : byte
{
    Centcomm,
    Station,
}

/// <summary>
/// _Duty: ивентовый FTL-прыжок шаттла в случайную точку открытого космоса рядом с ЦК или станцией.
///
/// Идёт через обычный <see cref="ShuttleSystem.FTLToCoordinates"/> — со стартовым гулом, гиперпространством,
/// маркером точки выхода и нокдауном, — но мимо проверок консоли (диск, FTLDestination, whitelist,
/// PreventFTL, лимит массы): это решение админа.
///
/// Главная опасность — выход из FTL сносит всё под гридом, включая чужие гриды и людей. Поэтому точка
/// берётся с отступом от целевой станции, проверяется на гриды, тела, предметы и чужие FTL-цели,
/// а до самого выхода перепроверяется (см. <see cref="DutyEventFtlComponent"/>).
/// </summary>
public sealed partial class DutyEventFtlSystem : EntitySystem
{
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedStationSystem _station = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;

    /// <summary>
    /// Зазор от края целевой станции до шаттла. Минимум — чтобы не вылететь в обшивку и не задеть
    /// пристыкованное; максимум — чтобы станцию было видно и до неё можно было долететь своим ходом.
    /// </summary>
    private const float MinGap = 48f;
    private const float MaxGap = 200f;

    /// <summary>
    /// Пустое пространство вокруг шаттла в точке выхода.
    /// </summary>
    private const float Clearance = 8f;

    private const int Attempts = 40;

    public const float MaxTravelTime = 600f;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    private EntityQuery<BodyComponent> _bodyQuery;
    private EntityQuery<MapGridComponent> _gridQuery;

    private List<Entity<MapGridComponent>> _grids = new();
    private readonly HashSet<EntityUid> _ents = new();

    public override void Initialize()
    {
        base.Initialize();

        _bodyQuery = GetEntityQuery<BodyComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
    }

    /// <summary>
    /// Минимальное время в гиперпространстве: короче фазы прибытия FTL не умеет.
    /// </summary>
    public float MinTravelTime => _shuttle.DefaultArrivalTime + 1f;

    public float DefaultTravelTime => _shuttle.DefaultTravelTime;

    /// <summary>
    /// Отправляет шаттл в прыжок. При ошибке возвращает false и локализованную причину.
    /// </summary>
    public bool TryStart(
        EntityUid shuttle,
        DutyEventFtlTarget target,
        float travelTime,
        out EntityCoordinates destination,
        out EntityUid anchor,
        [NotNullWhen(false)] out string? error)
    {
        destination = default;
        anchor = default;

        if (!TryValidateShuttle(shuttle, out var shuttleComp, out error))
            return false;

        if (!TryResolveAnchor(shuttle, target, out anchor, out error))
            return false;

        if (!TryPickSpot(shuttle, anchor, null, out destination, out var angle))
        {
            error = Loc.GetString("cmd-dutyftl-no-spot", ("anchor", Name(anchor)));
            return false;
        }

        travelTime = Math.Clamp(travelTime, MinTravelTime, MaxTravelTime);
        _shuttle.FTLToCoordinates(shuttle, shuttleComp, destination, angle, hyperspaceTime: travelTime);

        // FTLToCoordinates молча выходит, если прыжок не завёлся.
        if (!HasComp<FTLComponent>(shuttle))
        {
            error = Loc.GetString("cmd-dutyftl-start-failed");
            return false;
        }

        var marker = EnsureComp<DutyEventFtlComponent>(shuttle);
        marker.Anchor = anchor;
        marker.NextCheck = _timing.CurTime + CheckInterval;

        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"Ивентовый FTL: {ToPrettyString(shuttle)} летит к {ToPrettyString(anchor)} в {_transform.ToMapCoordinates(destination)}, {travelTime:0} с в гиперпространстве");

        return true;
    }

    /// <summary>
    /// Шаттл, но не станция, не ЦК, не планета и не уже летящий.
    /// </summary>
    private bool TryValidateShuttle(
        EntityUid shuttle,
        [NotNullWhen(true)] out ShuttleComponent? shuttleComp,
        [NotNullWhen(false)] out string? error)
    {
        shuttleComp = null;
        error = null;

        if (!_gridQuery.HasComp(shuttle) || HasComp<MapComponent>(shuttle))
        {
            error = Loc.GetString("cmd-dutyftl-not-grid");
            return false;
        }

        if (!TryComp(shuttle, out shuttleComp))
        {
            error = Loc.GetString("cmd-dutyftl-not-shuttle", ("grid", Name(shuttle)));
            return false;
        }

        if (HasComp<FTLComponent>(shuttle))
        {
            error = Loc.GetString("cmd-dutyftl-already-ftl", ("grid", Name(shuttle)));
            return false;
        }

        // Основной грид станции и сам ЦК тоже несут ShuttleComponent — утащить их в FTL означает сломать раунд.
        var centcomms = EntityQueryEnumerator<StationCentcommComponent>();
        while (centcomms.MoveNext(out var centcomm))
        {
            if (centcomm.Entity == shuttle)
            {
                error = Loc.GetString("cmd-dutyftl-is-station", ("grid", Name(shuttle)));
                return false;
            }
        }

        var stations = EntityQueryEnumerator<StationDataComponent>();
        while (stations.MoveNext(out var station, out var data))
        {
            if (_station.GetLargestGrid((station, data)) == shuttle)
            {
                error = Loc.GetString("cmd-dutyftl-is-station", ("grid", Name(shuttle)));
                return false;
            }
        }

        return true;
    }

    private bool TryResolveAnchor(
        EntityUid shuttle,
        DutyEventFtlTarget target,
        out EntityUid anchor,
        [NotNullWhen(false)] out string? error)
    {
        error = null;
        anchor = default;

        // Если шаттл приписан к станции — берём её ЦК/её саму, иначе первую подходящую.
        var owner = _station.GetOwningStation(shuttle);

        EntityUid? found = target switch
        {
            DutyEventFtlTarget.Centcomm => FindCentcommGrid(owner),
            _ => FindStationGrid(owner),
        };

        if (found is not { } grid || grid == shuttle)
        {
            error = Loc.GetString(target == DutyEventFtlTarget.Centcomm
                ? "cmd-dutyftl-no-centcomm"
                : "cmd-dutyftl-no-station");
            return false;
        }

        anchor = grid;
        return true;
    }

    private EntityUid? FindCentcommGrid(EntityUid? owner)
    {
        if (TryComp<StationCentcommComponent>(owner, out var own) && IsValidCentcomm(own))
            return own.Entity;

        var query = EntityQueryEnumerator<StationCentcommComponent>();
        while (query.MoveNext(out var centcomm))
        {
            if (IsValidCentcomm(centcomm))
                return centcomm.Entity;
        }

        return null;
    }

    private bool IsValidCentcomm(StationCentcommComponent centcomm)
    {
        return centcomm.Entity is { } grid && !TerminatingOrDeleted(grid) && _gridQuery.HasComp(grid)
               && Transform(grid).MapUid is { } map && !TerminatingOrDeleted(map);
    }

    /// <summary>
    /// Основной грид игровой станции. Игровые станции отличаем по StationCentcommComponent: он есть только
    /// у тех, к кому летит эвакуация, — у служебных «станций» (шаттлы ОБР и т.п.) его нет.
    /// </summary>
    private EntityUid? FindStationGrid(EntityUid? owner)
    {
        if (owner != null && HasComp<StationCentcommComponent>(owner) && _station.GetLargestGrid(owner.Value) is { } own)
            return own;

        EntityUid? best = null;
        var bestArea = -1f;
        var query = EntityQueryEnumerator<StationCentcommComponent, StationDataComponent>();
        while (query.MoveNext(out var station, out _, out var data))
        {
            if (_station.GetLargestGrid((station, data)) is not { } grid || !_gridQuery.TryComp(grid, out var gridComp))
                continue;

            var area = gridComp.LocalAABB.Width * gridComp.LocalAABB.Height;
            if (area <= bestArea)
                continue;

            best = grid;
            bestArea = area;
        }

        return best;
    }

    /// <summary>
    /// Случайная свободная точка в кольце вокруг <paramref name="anchor"/>. Первую половину попыток требуем
    /// совсем пустой космос, потом соглашаемся на мелкий мусор (его снесёт при выходе), а кольцо расширяем.
    /// Гриды, тела и чужие FTL-цели недопустимы всегда.
    /// </summary>
    private bool TryPickSpot(EntityUid shuttle, EntityUid anchor, EntityUid? ignore, out EntityCoordinates coords, out Angle angle)
    {
        coords = default;
        angle = default;

        if (!_gridQuery.TryComp(shuttle, out var shuttleGrid) || !_gridQuery.TryComp(anchor, out var anchorGrid))
            return false;

        var anchorXform = Transform(anchor);
        if (anchorXform.MapUid is not { } mapUid)
            return false;

        var mapId = anchorXform.MapID;
        var anchorBox = _transform.GetWorldMatrix(anchorXform).TransformBox(anchorGrid.LocalAABB);
        var anchorRadius = anchorBox.Size.Length() / 2f;
        var shuttleRadius = GetRadius(shuttleGrid.LocalAABB);

        for (var i = 0; i < Attempts; i++)
        {
            var strict = i < Attempts / 2;
            var maxGap = MaxGap * (1f + i / 10f);
            var distance = anchorRadius + shuttleRadius + _random.NextFloat(MinGap, maxGap);
            var position = anchorBox.Center + _random.NextAngle().ToVec() * distance;
            var rotation = _random.NextAngle();

            var box = GetArrivalBox(shuttleGrid, position, rotation);
            if (!IsSpotFree(shuttle, mapUid, mapId, box, ignore, strict))
                continue;

            coords = new EntityCoordinates(mapUid, position);
            angle = rotation;
            return true;
        }

        return false;
    }

    private bool IsSpotFree(EntityUid shuttle, EntityUid mapUid, MapId mapId, Box2 box, EntityUid? ignore, bool strict)
    {
        _grids.Clear();
        _mapManager.FindGridsIntersecting(mapUid, box, ref _grids, approx: true);
        foreach (var grid in _grids)
        {
            if (grid.Owner != shuttle)
                return false;
        }

        // Другие шаттлы, которые сейчас сами летят сюда.
        var ftls = EntityQueryEnumerator<FTLComponent, MapGridComponent>();
        while (ftls.MoveNext(out var other, out var ftl, out var otherGrid))
        {
            if (other == shuttle || ftl.State is not (FTLState.Starting or FTLState.Travelling or FTLState.Arriving))
                continue;

            if (TerminatingOrDeleted(ftl.TargetCoordinates.EntityId))
                continue;

            var target = _transform.ToMapCoordinates(ftl.TargetCoordinates);
            if (target.MapId != mapId)
                continue;

            if (GetArrivalBox(otherGrid, target.Position, ftl.TargetAngle).Intersects(box))
                return false;
        }

        _ents.Clear();
        _lookup.GetEntitiesIntersecting(mapId, box, _ents, LookupFlags.Uncontained);
        foreach (var ent in _ents)
        {
            if (ent == ignore || ent == shuttle || Transform(ent).GridUid == shuttle)
                continue;

            if (strict || _bodyQuery.HasComp(ent))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Мировой AABB шаттла, вставшего началом грида в <paramref name="position"/> с поворотом <paramref name="rotation"/>, —
    /// ровно так его ставит выход из FTL.
    /// </summary>
    private static Box2 GetArrivalBox(MapGridComponent grid, Vector2 position, Angle rotation)
    {
        var rotated = new Box2Rotated(grid.LocalAABB.Translated(position), rotation, position);
        return rotated.CalcBoundingBox().Enlarged(Clearance);
    }

    /// <summary>
    /// Радиус от начала грида до самого дальнего угла: начало грида не обязано быть в центре.
    /// </summary>
    private static float GetRadius(Box2 local)
    {
        return MathF.Max(
            MathF.Max(local.BottomLeft.Length(), local.BottomRight.Length()),
            MathF.Max(local.TopLeft.Length(), local.TopRight.Length()));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<DutyEventFtlComponent>();
        while (query.MoveNext(out var uid, out var marker))
        {
            if (now < marker.NextCheck)
                continue;

            marker.NextCheck = now + CheckInterval;

            // Прыжок закончился или прервался — дальше шаттл летает как обычно, точку больше не трогаем.
            if (!TryComp<FTLComponent>(uid, out var ftl)
                || ftl.State is not (FTLState.Starting or FTLState.Travelling or FTLState.Arriving)
                || TerminatingOrDeleted(marker.Anchor))
            {
                RemCompDeferred<DutyEventFtlComponent>(uid);
                continue;
            }

            Revalidate(uid, marker, ftl);
        }
    }

    /// <summary>
    /// Если точку выхода заняли, пока шаттл в пути, — переносим её. Мелкий мусор не считаем: из-за летающей
    /// банки точка прыгала бы туда-сюда, а снести её не жалко.
    /// </summary>
    private void Revalidate(EntityUid uid, DutyEventFtlComponent marker, FTLComponent ftl)
    {
        if (!_gridQuery.TryComp(uid, out var grid) || TerminatingOrDeleted(ftl.TargetCoordinates.EntityId))
            return;

        var target = _transform.ToMapCoordinates(ftl.TargetCoordinates);
        if (target.MapId == MapId.Nullspace || _transform.GetMap(ftl.TargetCoordinates) is not { } mapUid)
            return;

        var box = GetArrivalBox(grid, target.Position, ftl.TargetAngle);
        if (IsSpotFree(uid, mapUid, target.MapId, box, ftl.VisualizerEntity, strict: false))
            return;

        if (!TryPickSpot(uid, marker.Anchor, ftl.VisualizerEntity, out var coords, out var angle))
        {
            Log.Warning($"Точка выхода ивентового FTL {ToPrettyString(uid)} занята, а свободной рядом с {ToPrettyString(marker.Anchor)} нет");
            return;
        }

        ftl.TargetCoordinates = coords;
        ftl.TargetAngle = angle;
        Dirty(uid, ftl);

        // В фазе прибытия маркер точки выхода уже стоит — переносим и его.
        if (ftl.VisualizerEntity is { } visualizer && !TerminatingOrDeleted(visualizer))
        {
            _transform.SetCoordinates(visualizer, coords);
            _transform.SetLocalRotation(visualizer, angle);
        }

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"Ивентовый FTL: точка выхода {ToPrettyString(uid)} занята, перенесена в {_transform.ToMapCoordinates(coords)}");
    }
}
