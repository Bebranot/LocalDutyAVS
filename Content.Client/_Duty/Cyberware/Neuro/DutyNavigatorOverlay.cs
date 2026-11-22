// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Client._Duty.Cyberware;
using Content.Client.Resources;
using Content.Shared._Duty.Cyberware.Neuro;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Cyberware.Neuro;

/// <summary>
/// _Duty: «Навигатор» — ромб над точкой отдела, на краю экрана стрелка, под ромбом название и дистанция.
/// Стиль меток нейроинтерфейса, но цвет — цвет маяка отдела, чтобы не путать маршрут с приказом.
/// </summary>
public sealed partial class DutyNavigatorOverlay : Overlay
{
    private const float DiamondRadius = 11f;
    private const float EdgePadding = 48f;
    /// <summary>Ближе этого (м) маршрут считается пройденным: ромб гаснет, чтобы не висел в упор.</summary>
    private const float ArrivedDistance = 2.5f;

    private static readonly Color DarkFill = new(0.02f, 0.03f, 0.05f, 0.78f);
    private static readonly Color ShadowColor = new(0f, 0f, 0f, 0.78f);

    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Font _font;
    private readonly SharedTransformSystem _transform;
    private readonly DutyCyberwareClientSystem _cyber;

    private readonly Vector2[] _fillVerts = new Vector2[6];
    private readonly Vector2[] _arrowVerts = new Vector2[3];

    private DutyNavigatorComponent? _nav;
    private EntityUid _local;

    // Подпись «ОТДЕЛ · 42 м» пересобирается только при смене метра или цели.
    private int _labelMeters = -1;
    private string _labelSource = string.Empty;
    private string _label = string.Empty;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public DutyNavigatorOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 14;

        _transform = _entMan.System<SharedTransformSystem>();
        _cyber = _entMan.System<DutyCyberwareClientSystem>();
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

        if (_player.LocalEntity is not { } local ||
            !_entMan.TryGetComponent(local, out DutyNavigatorComponent? nav) ||
            nav.Target == null)
        {
            return false;
        }

        // Под ЭМИ навигатор слепнет вместе с остальной киберсистемой.
        if (_cyber.IsHostEmped(local))
            return false;

        _local = local;
        _nav = nav;
        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_nav?.Target is not { } netTarget || args.ViewportControl is not { } viewport)
            return;

        var coords = _entMan.GetCoordinates(netTarget);
        if (!coords.IsValid(_entMan))
            return;

        var target = _transform.ToMapCoordinates(coords);
        var self = _transform.GetMapCoordinates(_local);
        if (target.MapId != args.MapId || self.MapId != target.MapId)
            return;

        var distance = (target.Position - self.Position).Length();
        if (distance < ArrivedDistance)
            return;

        var control = viewport as Control;
        var uiScale = control?.UIScale ?? 1f;
        var pad = EdgePadding * uiScale;
        var bounds = args.ViewportBounds;
        var left = bounds.Left + pad;
        var top = bounds.Top + pad;
        var right = bounds.Right - pad;
        var bottom = bounds.Bottom - pad;
        if (right <= left || bottom <= top)
            return;

        var center = new Vector2((left + right) * 0.5f, (top + bottom) * 0.5f);
        var extents = new Vector2((right - left) * 0.5f, (bottom - top) * 0.5f);

        var screen = viewport.WorldToScreen(target.Position);
        var offScreen = screen.X < left || screen.X > right || screen.Y < top || screen.Y > bottom;
        var drawPos = screen;
        var dir = Vector2.UnitY;
        if (offScreen)
        {
            var delta = screen - center;
            if (delta.LengthSquared() < 0.0001f)
                return;

            dir = delta.Normalized();
            var scaleX = MathF.Abs(delta.X) > 0.0001f ? extents.X / MathF.Abs(delta.X) : float.MaxValue;
            var scaleY = MathF.Abs(delta.Y) > 0.0001f ? extents.Y / MathF.Abs(delta.Y) : float.MaxValue;
            drawPos = center + delta * MathF.Min(scaleX, scaleY);
        }

        var handle = args.ScreenHandle;
        handle.SetTransform(Matrix3x2.Identity);

        var color = _nav.Color;
        var pulse = 0.8f + 0.2f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 3.4f);
        var radius = DiamondRadius * uiScale;

        FillDiamond(drawPos, radius * 1.3f, _fillVerts);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _fillVerts, color.WithAlpha(0.2f * pulse));
        FillDiamond(drawPos, radius, _fillVerts);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _fillVerts, DarkFill);

        var edge = color.WithAlpha(pulse);
        handle.DrawLine(drawPos + new Vector2(0f, -radius), drawPos + new Vector2(radius, 0f), edge);
        handle.DrawLine(drawPos + new Vector2(radius, 0f), drawPos + new Vector2(0f, radius), edge);
        handle.DrawLine(drawPos + new Vector2(0f, radius), drawPos + new Vector2(-radius, 0f), edge);
        handle.DrawLine(drawPos + new Vector2(-radius, 0f), drawPos + new Vector2(0f, -radius), edge);
        handle.DrawCircle(drawPos, radius * 0.22f, color);

        if (offScreen)
        {
            var size = 8f * uiScale;
            var tipPos = drawPos + dir * (radius + 9f * uiScale);
            var perp = new Vector2(-dir.Y, dir.X);
            _arrowVerts[0] = tipPos + dir * (size * 0.5f);
            _arrowVerts[1] = tipPos - dir * (size * 0.55f) + perp * (size * 0.52f);
            _arrowVerts[2] = tipPos - dir * (size * 0.55f) - perp * (size * 0.52f);
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _arrowVerts, color);
        }

        var meters = (int) MathF.Round(distance);
        if (meters != _labelMeters || !string.Equals(_labelSource, _nav.Label, StringComparison.Ordinal))
        {
            _labelMeters = meters;
            _labelSource = _nav.Label;
            _label = Loc.GetString("duty-navigator-overlay-label", ("name", _nav.Label), ("meters", meters));
        }

        var dims = handle.GetDimensions(_font, _label, uiScale);
        var pos = new Vector2(drawPos.X - dims.X * 0.5f, drawPos.Y + radius + 4f * uiScale);
        handle.DrawString(_font, pos + new Vector2(1f, 1f) * uiScale, _label, uiScale, ShadowColor);
        handle.DrawString(_font, pos, _label, uiScale, Color.InterpolateBetween(color, Color.White, 0.55f));
    }

    private static void FillDiamond(Vector2 center, float radius, Vector2[] verts)
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
}
