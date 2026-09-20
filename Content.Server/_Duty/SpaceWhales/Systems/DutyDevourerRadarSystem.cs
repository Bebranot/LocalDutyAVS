using System.Numerics;
using Content.Server.Shuttles.Systems;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._Duty.SpaceWhales.Systems;

/// <summary>
///     Periodically refreshes the mass scanner (radar) interface state for the Universe Devourer
///     so the view stays centered on the worm while it moves through space.
/// </summary>
public sealed class DutyDevourerRadarSystem : EntitySystem
{
    [Dependency] private readonly ShuttleConsoleSystem _console = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private const string DevourerRadarAction = "ActionDutyDevourerMassScan";
    private const float UpdateInterval = 1f;
    private float _accumulator;

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;
        _accumulator = 0f;

        var query = EntityQueryEnumerator<RadarConsoleComponent, IntrinsicUIComponent>();
        while (query.MoveNext(out var uid, out var radar, out var iui))
        {
            if (!UsesDevourerRadar(iui))
                continue;

            var xform = Transform(uid);
            var coordinates = radar.FollowEntity ? new EntityCoordinates(uid, Vector2.Zero) : xform.Coordinates;
            var angle = radar.FollowEntity ? Angle.Zero : xform.LocalRotation;

            var docks = _console.GetAllDocks();
            var state = _console.GetNavState(uid, docks, coordinates, angle);
            state.RotateWithEntity = !radar.FollowEntity;

            _ui.SetUiState(uid, RadarConsoleUiKey.Key, new NavBoundUserInterfaceState(state));
        }
    }

    private static bool UsesDevourerRadar(IntrinsicUIComponent iui)
    {
        foreach (var entry in iui.UIs.Values)
        {
            if (entry.ToggleAction?.Id == DevourerRadarAction)
                return true;
        }

        return false;
    }
}