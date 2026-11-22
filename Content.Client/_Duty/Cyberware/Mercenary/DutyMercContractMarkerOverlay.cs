// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Client.Resources;
using Content.Shared._Duty.Cyberware.Mercenary;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Cyberware.Mercenary;

/// <summary>
/// _Duty: метка цели «Контракта» в серо-зелёной палитре наёмника.
/// Точность задаёт сервер (режим и уже огрублённая позиция): вблизи — ромб на цели, на средней дистанции —
/// «плавающий» ромб с кольцом погрешности, издалека — только стрелка-компас вокруг носителя.
/// </summary>
public sealed partial class DutyMercContractMarkerOverlay : Overlay
{
    private const int MarkerZIndex = 16;
    private const float DiamondRadius = 13f;
    private const float EdgePadding = 40f;
    private const float CompassRadius = 64f;
    private const int LabelCacheLimit = 512;

    // Палитра Mercenary из HUD киберсистемы (DutyCyberHudWidget).
    private static readonly Color Accent = Color.FromHex("#7CFF9B");
    private static readonly Color Dim = Color.FromHex("#4E9A63");
    private static readonly Color Highlight = Color.FromHex("#CCFFD9");
    private static readonly Color Steel = Color.FromHex("#4C6B55");
    private static readonly Color Glow = Color.FromHex("#3DDC84").WithAlpha(0.22f);
    private static readonly Color Fill = new(0.02f, 0.06f, 0.03f, 0.82f);
    private static readonly Color Shadow = new(0f, 0f, 0f, 0.78f);

    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Font _font;

    private DutyMercContractClientSystem? _system;
    private SharedTransformSystem? _transform;

    private EntityUid _local;
    private DutyMercContractTrackerComponent? _tracker;

    private readonly Vector2[] _glowVerts = new Vector2[6];
    private readonly Vector2[] _fillVerts = new Vector2[6];
    private readonly Vector2[] _arrowVerts = new Vector2[3];

    private readonly Dictionary<int, string> _exactLabels = new();
    private readonly Dictionary<int, string> _approxLabels = new();

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public DutyMercContractMarkerOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = MarkerZIndex;

        _font = _resCache.GetFont(new[]
        {
            "/Fonts/RobotoMono/RobotoMono-Bold.ttf",
            "/Fonts/NotoSans/NotoSans-Bold.ttf"
        }, 10);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.ViewportControl == null || args.Viewport.Eye == null)
            return false;

        _system ??= _entMan.System<DutyMercContractClientSystem>();
        if (!_system.TryGetLocalTracker(out _local, out var tracker))
            return false;

        _tracker = tracker;
        return tracker.Mode is DutyMercTrackMode.Exact or DutyMercTrackMode.Approximate or DutyMercTrackMode.Direction;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.ViewportControl is not { } viewport || _tracker is not { } tracker)
            return;

        _transform ??= _entMan.System<SharedTransformSystem>();
        var playerMap = _transform.GetMapCoordinates(_local);
        if (playerMap.MapId != args.MapId)
            return;

        var control = viewport as Control;
        var uiScale = control?.UIScale ?? 1f;
        var bounds = GetVisibleBounds(args.ViewportBounds, control);
        var pad = EdgePadding * uiScale;
        var left = bounds.Left + pad;
        var top = bounds.Top + pad;
        var right = bounds.Right - pad;
        var bottom = bounds.Bottom - pad;
        if (right <= left || bottom <= top)
            return;

        var handle = args.ScreenHandle;
        handle.SetTransform(Matrix3x2.Identity);

        var time = (float) _timing.RealTime.TotalSeconds;
        var pulse = 0.85f + 0.15f * MathF.Sin(time * 3.6f);
        var radius = DiamondRadius * uiScale;
        var distance = (int) MathF.Round(tracker.Distance);

        if (tracker.Mode == DutyMercTrackMode.Direction)
        {
            // Только направление: стрелка-компас вокруг носителя, без точки на карте.
            var origin = viewport.WorldToScreen(playerMap.Position);
            var target = viewport.WorldToScreen(tracker.TargetPos);
            var delta = target - origin;
            if (delta.LengthSquared() < 0.0001f)
                return;

            var dir = delta.Normalized();
            var tip = origin + dir * (CompassRadius * uiScale);
            DrawArrow(handle, tip, dir, 11f * uiScale, Accent.WithAlpha(pulse));
            DrawArrow(handle, tip - dir * (8f * uiScale), dir, 7f * uiScale, Dim.WithAlpha(0.6f * pulse));
            DrawLabel(handle, tip + dir * (14f * uiScale) - new Vector2(0f, 6f * uiScale), GetLabel(_approxLabels, "duty-merc-marker-approx", distance), uiScale, Dim);
            return;
        }

        var center = new Vector2((left + right) * 0.5f, (top + bottom) * 0.5f);
        var extents = new Vector2((right - left) * 0.5f, (bottom - top) * 0.5f);

        var screen = viewport.WorldToScreen(tracker.TargetPos);
        var offScreen = screen.X < left || screen.X > right || screen.Y < top || screen.Y > bottom;
        var drawPos = screen;
        var edgeDir = Vector2.UnitY;

        if (offScreen)
        {
            var delta = screen - center;
            if (delta.LengthSquared() < 0.0001f)
                return;

            edgeDir = delta.Normalized();
            drawPos = center + delta * EdgeScale(delta, extents);
        }

        var exact = tracker.Mode == DutyMercTrackMode.Exact;
        if (exact)
        {
            DrawDiamond(handle, drawPos, radius, pulse, Accent);
            // Перекрестье: «цель зафиксирована».
            var s = radius * 0.45f;
            handle.DrawLine(drawPos - new Vector2(s, 0f), drawPos + new Vector2(s, 0f), Highlight);
            handle.DrawLine(drawPos - new Vector2(0f, s), drawPos + new Vector2(0f, s), Highlight);
        }
        else
        {
            // Погрешность: ромб «дышит» и обведён рваным кольцом.
            var wobble = radius * (1.05f + 0.08f * MathF.Sin(time * 2.1f));
            DrawDiamond(handle, drawPos, wobble, pulse, Dim);
            DrawBrokenRing(handle, drawPos, radius * 2.1f, time, Steel.WithAlpha(0.8f * pulse));
        }

        if (offScreen)
            DrawArrow(handle, drawPos + edgeDir * (radius + 9f * uiScale), edgeDir, 7f * uiScale, Accent);

        var label = exact
            ? GetLabel(_exactLabels, "duty-merc-marker-exact", distance)
            : GetLabel(_approxLabels, "duty-merc-marker-approx", distance);
        DrawLabel(handle, drawPos + new Vector2(0f, radius + 4f * uiScale), label, uiScale, exact ? Highlight : Dim);
    }

    private static string GetLabel(Dictionary<int, string> cache, string key, int meters)
    {
        if (cache.TryGetValue(meters, out var label))
            return label;

        if (cache.Count >= LabelCacheLimit)
            cache.Clear();

        label = Loc.GetString(key, ("meters", meters.ToString()));
        cache[meters] = label;
        return label;
    }

    private void DrawLabel(DrawingHandleScreen handle, Vector2 anchor, string label, float uiScale, Color color)
    {
        var dims = handle.GetDimensions(_font, label, uiScale);
        var pos = new Vector2(anchor.X - dims.X * 0.5f, anchor.Y);
        handle.DrawString(_font, pos + new Vector2(1f, 1f) * uiScale, label, uiScale, Shadow);
        handle.DrawString(_font, pos, label, uiScale, color);
    }

    private void DrawDiamond(DrawingHandleScreen handle, Vector2 center, float radius, float pulse, Color edge)
    {
        FillDiamond(center, radius * (1.3f + 0.05f * pulse), _glowVerts);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _glowVerts, Glow);

        FillDiamond(center, radius, _fillVerts);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _fillVerts, Fill);

        var top = center + new Vector2(0f, -radius);
        var right = center + new Vector2(radius, 0f);
        var bottom = center + new Vector2(0f, radius);
        var left = center + new Vector2(-radius, 0f);
        var color = edge.WithAlpha(pulse);
        handle.DrawLine(top, right, color);
        handle.DrawLine(right, bottom, color);
        handle.DrawLine(bottom, left, color);
        handle.DrawLine(left, top, color);
    }

    /// <summary>Кольцо из 8 дуг-штрихов, медленно вращается — «зона, где цель где-то есть».</summary>
    private static void DrawBrokenRing(DrawingHandleScreen handle, Vector2 center, float radius, float time, Color color)
    {
        const int segments = 8;
        const float arc = MathF.Tau / segments;
        var spin = time * 0.6f;
        for (var i = 0; i < segments; i++)
        {
            var a0 = spin + i * arc;
            var a1 = a0 + arc * 0.55f;
            var p0 = center + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius;
            var p1 = center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius;
            handle.DrawLine(p0, p1, color);
        }
    }

    private void DrawArrow(DrawingHandleScreen handle, Vector2 pos, Vector2 dir, float size, Color color)
    {
        var perp = new Vector2(-dir.Y, dir.X);
        _arrowVerts[0] = pos + dir * (size * 0.5f);
        _arrowVerts[1] = pos - dir * (size * 0.55f) + perp * (size * 0.52f);
        _arrowVerts[2] = pos - dir * (size * 0.55f) - perp * (size * 0.52f);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _arrowVerts, color);
    }

    private static void FillDiamond(Vector2 center, float radius, Span<Vector2> verts)
    {
        var top = center + new Vector2(0f, -radius);
        var right = center + new Vector2(radius, 0f);
        var bottom = center + new Vector2(0f, radius);
        var left = center + new Vector2(-radius, 0f);
        verts[0] = top;
        verts[1] = right;
        verts[2] = bottom;
        verts[3] = top;
        verts[4] = bottom;
        verts[5] = left;
    }

    private static UIBox2 GetVisibleBounds(UIBox2i viewportBounds, Control? control)
    {
        var bounds = new UIBox2(viewportBounds.Left, viewportBounds.Top, viewportBounds.Right, viewportBounds.Bottom);
        if (control == null)
            return bounds;

        var pos = control.GlobalPixelPosition;
        var size = control.PixelSize;
        return new UIBox2(
            MathF.Max(bounds.Left, pos.X),
            MathF.Max(bounds.Top, pos.Y),
            MathF.Min(bounds.Right, pos.X + size.X),
            MathF.Min(bounds.Bottom, pos.Y + size.Y));
    }

    private static float EdgeScale(Vector2 delta, Vector2 extents)
    {
        var scaleX = MathF.Abs(delta.X) > 0.0001f ? extents.X / MathF.Abs(delta.X) : float.MaxValue;
        var scaleY = MathF.Abs(delta.Y) > 0.0001f ? extents.Y / MathF.Abs(delta.Y) : float.MaxValue;
        return MathF.Min(scaleX, scaleY);
    }
}
