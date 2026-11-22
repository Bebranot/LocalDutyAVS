// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Client.Resources;
using Content.Shared._Duty.Neurointerface;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Neurointerface;

/// <summary>
/// _Duty: ScreenSpace-оверлей тактических приказов нейроинтерфейса.
/// Проецирует активные приказы отряда в экранные координаты в виде светящихся оранжевых ромбов
/// с векторными иконками типов приказов, дистанцией в метрах и краевыми стрелками-указателями.
/// </summary>
public sealed partial class DutyNeuroWorldMarkersOverlay : Overlay
{
    private const int MarkersZIndex = 15;
    private const float DiamondRadius = 15f;
    private const float EdgePadding = 40f;

    private static readonly Color NeonOrange = Color.FromHex("#FF7A18");
    private static readonly Color BrightHighlight = Color.FromHex("#FFC078");
    private static readonly Color DarkFill = new(0.08f, 0.03f, 0.01f, 0.84f);
    private static readonly Color GlowColor = Color.FromHex("#FF7A18").WithAlpha(0.24f);
    private static readonly Color ShadowColor = new(0f, 0f, 0f, 0.78f);

    /// <summary>Верхняя граница размера кэша подписей дистанции (метры), чтобы он не рос бесконечно.</summary>
    private const int DistanceCacheLimit = 1024;

    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Font _distFont;

    // Системы кэшируются лениво: _entMan.System<>() каждый кадр — лишний поиск по словарю.
    private DutyNeuroClientSystem? _clientSystem;
    private SharedTransformSystem? _transform;

    // Носитель и его сущность из BeforeDraw — Draw идёт в том же кадре, повторно не ищем.
    private EntityUid _local;
    private DutyNeurointerfaceWearerComponent? _wearer;

    // Переиспользуемые буферы вершин: Draw зовётся каждый кадр на каждый маркер.
    private readonly Vector2[] _glowVerts = new Vector2[6];
    private readonly Vector2[] _fillVerts = new Vector2[6];
    private readonly Vector2[] _arrowVerts = new Vector2[3];

    // Подписи дистанции по целому числу метров.
    private readonly Dictionary<int, string> _distanceLabels = new();

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public DutyNeuroWorldMarkersOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = MarkersZIndex;

        _distFont = _resCache.GetFont(new[]
        {
            "/Fonts/RobotoMono/RobotoMono-Bold.ttf",
            "/Fonts/NotoSans/NotoSans-Bold.ttf"
        }, 10);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        // Без глаза WorldToScreen не посчитать (нет камеры) — маркеры пропускаем.
        if (args.ViewportControl == null || args.Viewport.Eye == null)
            return false;

        _clientSystem ??= _entMan.System<DutyNeuroClientSystem>();
        if (!_clientSystem.TryGetActiveWearer(out _local, out var wearer))
            return false;

        _wearer = wearer;
        // Под ЭМИ панель погашена — меток нет, даже если сервер их ещё держит.
        return wearer.OwnerStatus != DutyNeuroMemberStatus.Dead &&
               !wearer.Jammed &&
               wearer.Orders.Count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Проверки (viewport, глаз, носитель, приказы) сделаны в BeforeDraw этого же кадра.
        if (args.ViewportControl is not { } viewport || _wearer is not { } wearer)
            return;

        var local = _local;
        _transform ??= _entMan.System<SharedTransformSystem>();
        var transform = _transform;
        var playerMapPos = transform.GetMapCoordinates(local);
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

        var center = new Vector2((left + right) * 0.5f, (top + bottom) * 0.5f);
        var extents = new Vector2((right - left) * 0.5f, (bottom - top) * 0.5f);
        var pulse = 0.88f + 0.12f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 4.2f);
        var radius = DiamondRadius * uiScale;

        var handle = args.ScreenHandle;
        handle.SetTransform(Matrix3x2.Identity);

        foreach (var order in wearer.Orders)
        {
            var coords = _entMan.GetCoordinates(order.Coordinates);
            if (!coords.IsValid(_entMan))
                continue;

            var targetMapPos = transform.ToMapCoordinates(coords);
            if (targetMapPos.MapId != args.MapId)
                continue;

            var distance = playerMapPos.MapId == targetMapPos.MapId
                ? (targetMapPos.Position - playerMapPos.Position).Length()
                : 0f;

            var screen = viewport.WorldToScreen(targetMapPos.Position);
            var offScreen = screen.X < left || screen.X > right || screen.Y < top || screen.Y > bottom;

            var drawPos = screen;
            var dir = Vector2.UnitY;
            if (offScreen)
            {
                var delta = screen - center;
                if (delta.LengthSquared() < 0.0001f)
                    continue;

                dir = delta.Normalized();
                drawPos = center + delta * EdgeScale(delta, extents);
            }

            DrawDiamondMarker(handle, drawPos, radius, pulse);
            DrawOrderIcon(handle, drawPos, radius * 0.52f, order.OrderType, BrightHighlight);

            if (offScreen)
                DrawEdgeArrow(handle, drawPos + dir * (radius + 9f * uiScale), dir, 7f * uiScale, NeonOrange);

            DrawDistanceLabel(handle, drawPos + new Vector2(0f, radius + 4f * uiScale), distance, uiScale);
        }
    }

    private void DrawDistanceLabel(DrawingHandleScreen handle, Vector2 anchor, float distance, float uiScale)
    {
        var meters = (int) MathF.Round(distance);
        if (!_distanceLabels.TryGetValue(meters, out var label))
        {
            if (_distanceLabels.Count >= DistanceCacheLimit)
                _distanceLabels.Clear();

            label = Loc.GetString("duty-neuro-hud-distance", ("meters", meters.ToString()));
            _distanceLabels[meters] = label;
        }

        var dims = handle.GetDimensions(_distFont, label, uiScale);
        var pos = new Vector2(anchor.X - dims.X * 0.5f, anchor.Y);

        handle.DrawString(_distFont, pos + new Vector2(1f, 1f) * uiScale, label, uiScale, ShadowColor);
        handle.DrawString(_distFont, pos, label, uiScale, BrightHighlight);
    }

    private void DrawDiamondMarker(DrawingHandleScreen handle, Vector2 center, float radius, float pulse)
    {
        var outerR = radius * (1.28f + 0.06f * pulse);
        FillDiamondTriangles(center, outerR, _glowVerts);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _glowVerts, GlowColor);

        FillDiamondTriangles(center, radius, _fillVerts);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _fillVerts, DarkFill);

        var top = center + new Vector2(0f, -radius);
        var right = center + new Vector2(radius, 0f);
        var bottom = center + new Vector2(0f, radius);
        var left = center + new Vector2(-radius, 0f);

        var edgeColor = NeonOrange.WithAlpha(pulse);
        handle.DrawLine(top, right, edgeColor);
        handle.DrawLine(right, bottom, edgeColor);
        handle.DrawLine(bottom, left, edgeColor);
        handle.DrawLine(left, top, edgeColor);

        var inner = radius * 0.78f;
        var innerColor = BrightHighlight.WithAlpha(0.45f * pulse);
        handle.DrawLine(center + new Vector2(0f, -inner), center + new Vector2(inner, 0f), innerColor);
        handle.DrawLine(center + new Vector2(inner, 0f), center + new Vector2(0f, inner), innerColor);
        handle.DrawLine(center + new Vector2(0f, inner), center + new Vector2(-inner, 0f), innerColor);
        handle.DrawLine(center + new Vector2(-inner, 0f), center + new Vector2(0f, -inner), innerColor);
    }

    private static void DrawOrderIcon(
        DrawingHandleScreen handle,
        Vector2 center,
        float s,
        DutyNeuroOrderType type,
        Color color)
    {
        switch (type)
        {
            case DutyNeuroOrderType.Move:
                // Бегущий маркер / двойной тактический шеврон вперёд
                handle.DrawLine(center + new Vector2(-s * 0.65f, -s * 0.1f), center + new Vector2(0f, -s * 0.85f), color);
                handle.DrawLine(center + new Vector2(0f, -s * 0.85f), center + new Vector2(s * 0.65f, -s * 0.1f), color);
                handle.DrawLine(center + new Vector2(-s * 0.55f, s * 0.55f), center + new Vector2(0f, -s * 0.15f), color);
                handle.DrawLine(center + new Vector2(0f, -s * 0.15f), center + new Vector2(s * 0.55f, s * 0.55f), color);
                break;

            case DutyNeuroOrderType.Assault:
                // Скрещённые клинки
                handle.DrawLine(center + new Vector2(-s * 0.7f, s * 0.7f), center + new Vector2(s * 0.7f, -s * 0.7f), color);
                handle.DrawLine(center + new Vector2(s * 0.7f, s * 0.7f), center + new Vector2(-s * 0.7f, -s * 0.7f), color);
                handle.DrawLine(center + new Vector2(-s * 0.75f, s * 0.25f), center + new Vector2(-s * 0.25f, s * 0.75f), color);
                handle.DrawLine(center + new Vector2(s * 0.25f, s * 0.75f), center + new Vector2(s * 0.75f, s * 0.25f), color);
                break;

            case DutyNeuroOrderType.Hold:
                // Тактический щит
                var tl = center + new Vector2(-s * 0.62f, -s * 0.65f);
                var tr = center + new Vector2(s * 0.62f, -s * 0.65f);
                var mr = center + new Vector2(s * 0.62f, s * 0.05f);
                var bot = center + new Vector2(0f, s * 0.82f);
                var ml = center + new Vector2(-s * 0.62f, s * 0.05f);
                handle.DrawLine(tl, tr, color);
                handle.DrawLine(tr, mr, color);
                handle.DrawLine(mr, bot, color);
                handle.DrawLine(bot, ml, color);
                handle.DrawLine(ml, tl, color);
                handle.DrawLine(center + new Vector2(0f, -s * 0.45f), center + new Vector2(0f, s * 0.55f), color);
                break;

            case DutyNeuroOrderType.Retreat:
                // U-образная стрелка возврата
                handle.DrawLine(center + new Vector2(s * 0.5f, s * 0.65f), center + new Vector2(s * 0.5f, -s * 0.35f), color);
                handle.DrawLine(center + new Vector2(s * 0.5f, -s * 0.35f), center + new Vector2(-s * 0.3f, -s * 0.35f), color);
                handle.DrawLine(center + new Vector2(-s * 0.3f, -s * 0.35f), center + new Vector2(-s * 0.3f, s * 0.55f), color);
                handle.DrawLine(center + new Vector2(-s * 0.65f, s * 0.2f), center + new Vector2(-s * 0.3f, s * 0.55f), color);
                handle.DrawLine(center + new Vector2(s * 0.05f, s * 0.2f), center + new Vector2(-s * 0.3f, s * 0.55f), color);
                break;

            case DutyNeuroOrderType.Sos:
                // Маяк SOS: восклицательный знак + боковые волны
                handle.DrawLine(center + new Vector2(0f, -s * 0.72f), center + new Vector2(0f, s * 0.2f), color);
                handle.DrawCircle(center + new Vector2(0f, s * 0.58f), MathF.Max(1.4f, s * 0.16f), color);
                handle.DrawLine(center + new Vector2(-s * 0.55f, -s * 0.4f), center + new Vector2(-s * 0.72f, 0f), color);
                handle.DrawLine(center + new Vector2(-s * 0.72f, 0f), center + new Vector2(-s * 0.55f, s * 0.4f), color);
                handle.DrawLine(center + new Vector2(s * 0.55f, -s * 0.4f), center + new Vector2(s * 0.72f, 0f), color);
                handle.DrawLine(center + new Vector2(s * 0.72f, 0f), center + new Vector2(s * 0.55f, s * 0.4f), color);
                break;

            case DutyNeuroOrderType.Recon:
                // Кибер-глаз разведки
                var leftEye = center + new Vector2(-s * 0.85f, 0f);
                var rightEye = center + new Vector2(s * 0.85f, 0f);
                var topEye = center + new Vector2(0f, -s * 0.55f);
                var botEye = center + new Vector2(0f, s * 0.55f);
                handle.DrawLine(leftEye, topEye, color);
                handle.DrawLine(topEye, rightEye, color);
                handle.DrawLine(rightEye, botEye, color);
                handle.DrawLine(botEye, leftEye, color);
                handle.DrawCircle(center, MathF.Max(2f, s * 0.28f), color, filled: false);
                handle.DrawCircle(center, MathF.Max(1f, s * 0.12f), color);
                break;
        }
    }

    private void DrawEdgeArrow(
        DrawingHandleScreen handle,
        Vector2 tipPos,
        Vector2 direction,
        float size,
        Color color)
    {
        var perp = new Vector2(-direction.Y, direction.X);
        var tip = tipPos + direction * (size * 0.5f);
        var left = tipPos - direction * (size * 0.55f) + perp * (size * 0.52f);
        var right = tipPos - direction * (size * 0.55f) - perp * (size * 0.52f);

        _arrowVerts[0] = tip;
        _arrowVerts[1] = left;
        _arrowVerts[2] = right;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _arrowVerts, color);
    }

    private static void FillDiamondTriangles(Vector2 center, float radius, Span<Vector2> verts)
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
        var controlBounds = new UIBox2(pos.X, pos.Y, pos.X + size.X, pos.Y + size.Y);

        return new UIBox2(
            MathF.Max(bounds.Left, controlBounds.Left),
            MathF.Max(bounds.Top, controlBounds.Top),
            MathF.Min(bounds.Right, controlBounds.Right),
            MathF.Min(bounds.Bottom, controlBounds.Bottom));
    }

    private static float EdgeScale(Vector2 delta, Vector2 extents)
    {
        var scaleX = MathF.Abs(delta.X) > 0.0001f ? extents.X / MathF.Abs(delta.X) : float.MaxValue;
        var scaleY = MathF.Abs(delta.Y) > 0.0001f ? extents.Y / MathF.Abs(delta.Y) : float.MaxValue;
        return MathF.Min(scaleX, scaleY);
    }
}
