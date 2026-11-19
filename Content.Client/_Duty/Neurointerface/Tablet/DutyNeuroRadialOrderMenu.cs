// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared._Duty.Neurointerface;
using Content.Shared.Input;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._Duty.Neurointerface.Tablet;

/// <summary>
/// _Duty: 6-секторное киберпанк-радиальное меню отдачи тактических приказов по карте планшета.
/// Рисуется процедурно через <see cref="DrawingHandleScreen"/> с векторными иконками и русскими подписями.
/// </summary>
public sealed partial class DutyNeuroRadialOrderMenu : Popup
{
    private const float LogicalSize = 310f;
    private const float OuterRadiusLogical = 132f;
    private const float InnerRadiusLogical = 34f;
    private const int ArcSegmentsPerSector = 14;

    private static readonly Color CircleBgColor = Color.FromHex("#070B12").WithAlpha(0.93f);
    private static readonly Color OuterRingColor = Color.FromHex("#C82337");
    private static readonly Color DividerColor = Color.FromHex("#FF8C24").WithAlpha(0.55f);
    private static readonly Color HoverDividerColor = Color.FromHex("#FF8C24");
    private static readonly Color CenterBgColor = Color.FromHex("#16090F").WithAlpha(0.96f);
    private static readonly Color CenterHoverBgColor = Color.FromHex("#5B121F").WithAlpha(0.96f);
    private static readonly Color LabelBgColor = Color.FromHex("#070B12").WithAlpha(0.92f);

    private readonly record struct SectorInfo(
        DutyNeuroOrderType Type,
        string Label,
        Color Accent);

    private static readonly DutyNeuroOrderType[] SectorTypes =
    {
        DutyNeuroOrderType.Move,
        DutyNeuroOrderType.Assault,
        DutyNeuroOrderType.Hold,
        DutyNeuroOrderType.Retreat,
        DutyNeuroOrderType.Sos,
        DutyNeuroOrderType.Recon
    };

    [Dependency] private IResourceCache _cache = default!;

    // Подписи берутся через Loc при создании экземпляра (static-массив строил бы их до загрузки локали).
    private readonly SectorInfo[] _sectors;
    private readonly string _cancelText;
    private readonly string _sosText;

    // Переиспользуемые буферы вершин: Draw вызывается каждый кадр, new-массивы там давали мусор.
    private readonly Vector2[] _fillVerts = new Vector2[ArcSegmentsPerSector * 6];
    private readonly Vector2[] _arcPoints = new Vector2[ArcSegmentsPerSector + 1];
    private readonly Vector2[] _boxPoints = new Vector2[5];
    private readonly Vector2[] _shieldPoints = new Vector2[6];
    private readonly Vector2[] _retreatPoints = new Vector2[5];
    private readonly Vector2[] _eyeUpperPoints = new Vector2[5];
    private readonly Vector2[] _eyeLowerPoints = new Vector2[5];

    private readonly Font _labelFont;
    private readonly Font _tooltipFont;
    private readonly Font _sosFont;

    private string _zoneName = string.Empty;
    private string _headerText = string.Empty;
    private int _headerTextIndex = int.MinValue; // для какого _hoveredIndex собран _headerText
    private int _hoveredIndex = -1; // -1 = вне меню, -2 = центральный X, 0..5 = сектор

    /// <summary>
    /// Вызывается при выборе сектора приказа.
    /// </summary>
    public event Action<DutyNeuroOrderType>? OnOrderSelected;

    public DutyNeuroRadialOrderMenu()
    {
        IoCManager.InjectDependencies(this);

        SetSize = new Vector2(LogicalSize, LogicalSize);
        MouseFilter = MouseFilterMode.Stop;
        CanKeyboardFocus = true;

        _labelFont = _cache.NotoStack(variation: "Bold", size: 9);
        _tooltipFont = _cache.NotoStack(variation: "Bold", size: 11);
        _sosFont = _cache.NotoStack(variation: "Bold", size: 12);

        _cancelText = Loc.GetString("duty-neuro-radial-cancel");
        _sosText = Loc.GetString("duty-neuro-order-sos");

        _sectors = new SectorInfo[SectorTypes.Length];
        for (var i = 0; i < SectorTypes.Length; i++)
        {
            var type = SectorTypes[i];
            _sectors[i] = new SectorInfo(
                type,
                Loc.GetString(GetOrderLocKey(type)).ToUpperInvariant(),
                DutyNeuroNavMapControl.GetOrderColor(type));
        }
    }

    /// <summary>
    /// Открывает радиальное меню с центром в указанной экранной точке (в физических пикселях).
    /// </summary>
    public void OpenAtScreenPosition(Vector2 screenPixelPos, string zoneName)
    {
        _zoneName = zoneName;
        _hoveredIndex = -1;
        _headerTextIndex = int.MinValue;

        var modalRoot = UserInterfaceManager.ModalRoot;
        if (Parent != modalRoot)
        {
            Orphan();
            modalRoot.AddChild(this);
        }

        var uiPos = screenPixelPos / UIScale;
        var rootSize = modalRoot.Size;
        var half = LogicalSize * 0.5f;

        var topLeft = new Vector2(
            Math.Clamp(uiPos.X - half, 4f, Math.Max(4f, rootSize.X - LogicalSize - 4f)),
            Math.Clamp(uiPos.Y - half, 4f, Math.Max(4f, rootSize.Y - LogicalSize - 4f)));

        Open(UIBox2.FromDimensions(topLeft, new Vector2(LogicalSize, LogicalSize)));
        GrabKeyboardFocus();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        UpdateHover(args.RelativePosition);
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hoveredIndex = -1;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.UIClick)
        {
            if (_hoveredIndex >= 0 && _hoveredIndex < _sectors.Length)
                OnOrderSelected?.Invoke(_sectors[_hoveredIndex].Type);

            Close();
            args.Handle();
            return;
        }

        if (args.Function == EngineKeyFunctions.UIRightClick ||
            args.Function == ContentKeyFunctions.EscapeContext)
        {
            Close();
            args.Handle();
            return;
        }

        base.KeyBindDown(args);
    }

    private void UpdateHover(Vector2 relativeUiPos)
    {
        var center = new Vector2(LogicalSize * 0.5f, LogicalSize * 0.5f);
        var delta = relativeUiPos - center;
        var dist = delta.Length();

        if (dist <= InnerRadiusLogical)
        {
            _hoveredIndex = -2;
            return;
        }

        if (dist > OuterRadiusLogical)
        {
            _hoveredIndex = -1;
            return;
        }

        // Угол от вершины круга (-PI/2) по часовой стрелке, сектор 0 — верхний.
        var angle = MathF.Atan2(delta.Y, delta.X) + MathF.PI * 0.5f + MathF.PI / 6f;
        if (angle < 0f)
            angle += MathF.Tau;
        angle %= MathF.Tau;

        var sector = (int)(angle / (MathF.Tau / _sectors.Length));
        _hoveredIndex = Math.Clamp(sector, 0, _sectors.Length - 1);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var center = PixelSize * 0.5f;
        var outerR = OuterRadiusLogical * UIScale;
        var innerR = InnerRadiusLogical * UIScale;
        var sectorAngle = MathF.Tau / _sectors.Length;
        var startOffset = -MathF.PI * 0.5f - sectorAngle * 0.5f;

        // Внешнее свечение и тёмный полупрозрачный диск.
        handle.DrawCircle(center, outerR + 5f * UIScale, OuterRingColor.WithAlpha(0.18f));
        handle.DrawCircle(center, outerR, CircleBgColor);

        // Заливка и подсветка секторов.
        for (var i = 0; i < _sectors.Length; i++)
        {
            var info = _sectors[i];
            var a0 = startOffset + i * sectorAngle;
            var a1 = a0 + sectorAngle;
            var isHovered = _hoveredIndex == i;

            var fillColor = isHovered
                ? info.Accent.WithAlpha(0.28f)
                : OuterRingColor.WithAlpha(0.05f);

            DrawSectorFill(handle, center, innerR, outerR, a0, a1, fillColor);

            if (isHovered)
                DrawArc(handle, center, outerR - 1.5f * UIScale, a0, a1, info.Accent, 2.5f);
        }

        // Оранжево-красные разделители секторов и кольца.
        for (var i = 0; i < _sectors.Length; i++)
        {
            var angle = startOffset + i * sectorAngle;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var isAdjacentHover = _hoveredIndex == i || _hoveredIndex == (i + _sectors.Length - 1) % _sectors.Length;
            handle.DrawLine(
                center + dir * innerR,
                center + dir * outerR,
                isAdjacentHover ? HoverDividerColor : DividerColor);
        }

        handle.DrawCircle(center, outerR, OuterRingColor, false);
        handle.DrawCircle(center, outerR - 2f * UIScale, OuterRingColor.WithAlpha(0.35f), false);

        // Иконки и подписи секторов.
        var iconRadius = (innerR + outerR) * 0.50f;
        for (var i = 0; i < _sectors.Length; i++)
        {
            var info = _sectors[i];
            var midAngle = startOffset + (i + 0.5f) * sectorAngle;
            var sectorCenter = center + new Vector2(MathF.Cos(midAngle), MathF.Sin(midAngle)) * iconRadius;
            var isHovered = _hoveredIndex == i;

            var iconColor = isHovered ? Color.White : info.Accent;
            var iconPos = sectorCenter - new Vector2(0f, 7f * UIScale);
            DrawOrderIcon(handle, info.Type, iconPos, 11f * UIScale, iconColor);

            var labelSize = handle.GetDimensions(_labelFont, info.Label, 1f);
            var labelPos = sectorCenter + new Vector2(-labelSize.X * 0.5f, 8f * UIScale);
            handle.DrawString(_labelFont, labelPos, info.Label, isHovered ? Color.White : info.Accent.WithAlpha(0.92f));
        }

        // Центральный круг отмены (X).
        var centerHovered = _hoveredIndex == -2;
        handle.DrawCircle(center, innerR, centerHovered ? CenterHoverBgColor : CenterBgColor);
        handle.DrawCircle(center, innerR, centerHovered ? Color.FromHex("#FF4B5C") : OuterRingColor, false);

        var xArm = 9f * UIScale;
        var xColor = centerHovered ? Color.White : OuterRingColor;
        handle.DrawLine(center + new Vector2(-xArm, -xArm), center + new Vector2(xArm, xArm), xColor);
        handle.DrawLine(center + new Vector2(-xArm, xArm), center + new Vector2(xArm, -xArm), xColor);

        // Верхняя плашка зоны и активного приказа.
        if (_headerTextIndex != _hoveredIndex)
        {
            _headerTextIndex = _hoveredIndex;
            _headerText = _hoveredIndex >= 0 && _hoveredIndex < _sectors.Length
                ? $"{_sectors[_hoveredIndex].Label} · {_zoneName}"
                : _hoveredIndex == -2
                    ? _cancelText
                    : _zoneName;
        }

        var headerText = _headerText;

        if (!string.IsNullOrWhiteSpace(headerText))
        {
            var dims = handle.GetDimensions(_tooltipFont, headerText, 1f);
            var pillPos = new Vector2(center.X - dims.X * 0.5f, MathF.Max(2f, center.Y - outerR - dims.Y - 10f * UIScale));
            var pillBox = UIBox2.FromDimensions(pillPos - new Vector2(6f, 2f), dims + new Vector2(12f, 4f));
            var accent = _hoveredIndex >= 0 && _hoveredIndex < _sectors.Length
                ? _sectors[_hoveredIndex].Accent
                : OuterRingColor;

            handle.DrawRect(pillBox, LabelBgColor);
            DrawBoxOutline(handle, pillBox, accent);
            handle.DrawString(_tooltipFont, pillPos, headerText, Color.White);
        }
    }

    private void DrawSectorFill(
        DrawingHandleScreen handle,
        Vector2 center,
        float innerR,
        float outerR,
        float startAngle,
        float endAngle,
        Color color)
    {
        var verts = _fillVerts;
        var n = 0;
        for (var s = 0; s < ArcSegmentsPerSector; s++)
        {
            var t0 = startAngle + (endAngle - startAngle) * (s / (float)ArcSegmentsPerSector);
            var t1 = startAngle + (endAngle - startAngle) * ((s + 1) / (float)ArcSegmentsPerSector);

            var d0 = new Vector2(MathF.Cos(t0), MathF.Sin(t0));
            var d1 = new Vector2(MathF.Cos(t1), MathF.Sin(t1));

            var i0 = center + d0 * innerR;
            var i1 = center + d1 * innerR;
            var o0 = center + d0 * outerR;
            var o1 = center + d1 * outerR;

            verts[n++] = i0;
            verts[n++] = o0;
            verts[n++] = o1;

            verts[n++] = i0;
            verts[n++] = o1;
            verts[n++] = i1;
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, verts, color);
    }

    private void DrawArc(
        DrawingHandleScreen handle,
        Vector2 center,
        float radius,
        float startAngle,
        float endAngle,
        Color color,
        float thickness)
    {
        FillArcPoints(center, radius, startAngle, endAngle);
        handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, _arcPoints, color);

        if (thickness > 1.5f)
        {
            FillArcPoints(center, radius - 1f, startAngle, endAngle);
            handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, _arcPoints, color);
        }
    }

    private void FillArcPoints(Vector2 center, float radius, float startAngle, float endAngle)
    {
        for (var s = 0; s <= ArcSegmentsPerSector; s++)
        {
            var t = startAngle + (endAngle - startAngle) * (s / (float)ArcSegmentsPerSector);
            _arcPoints[s] = center + new Vector2(MathF.Cos(t), MathF.Sin(t)) * radius;
        }
    }

    private static string GetOrderLocKey(DutyNeuroOrderType type) => type switch
    {
        DutyNeuroOrderType.Move => "duty-neuro-order-move",
        DutyNeuroOrderType.Assault => "duty-neuro-order-assault",
        DutyNeuroOrderType.Hold => "duty-neuro-order-hold",
        DutyNeuroOrderType.Retreat => "duty-neuro-order-retreat",
        DutyNeuroOrderType.Sos => "duty-neuro-order-sos",
        _ => "duty-neuro-order-recon"
    };

    private void DrawOrderIcon(
        DrawingHandleScreen handle,
        DutyNeuroOrderType type,
        Vector2 c,
        float s,
        Color color)
    {
        switch (type)
        {
            case DutyNeuroOrderType.Move:
            {
                // Бегущий силуэт: голова, наклонённый торс, руки, ноги.
                var head = c + new Vector2(s * 0.25f, -s * 0.65f);
                handle.DrawCircle(head, s * 0.20f, color);
                var neck = c + new Vector2(s * 0.12f, -s * 0.35f);
                var hip = c + new Vector2(-s * 0.12f, s * 0.15f);
                handle.DrawLine(neck, hip, color);
                // Руки
                handle.DrawLine(neck, c + new Vector2(s * 0.55f, -s * 0.25f), color);
                handle.DrawLine(neck, c + new Vector2(-s * 0.45f, -s * 0.05f), color);
                // Ноги
                handle.DrawLine(hip, c + new Vector2(s * 0.45f, s * 0.38f), color);
                handle.DrawLine(c + new Vector2(s * 0.45f, s * 0.38f), c + new Vector2(s * 0.35f, s * 0.80f), color);
                handle.DrawLine(hip, c + new Vector2(-s * 0.55f, s * 0.70f), color);
                break;
            }
            case DutyNeuroOrderType.Assault:
            {
                // Скрещённые клинки.
                handle.DrawLine(c + new Vector2(-s * 0.7f, s * 0.7f), c + new Vector2(s * 0.7f, -s * 0.7f), color);
                handle.DrawLine(c + new Vector2(s * 0.7f, s * 0.7f), c + new Vector2(-s * 0.7f, -s * 0.7f), color);
                // Гарды
                handle.DrawLine(c + new Vector2(-s * 0.65f, s * 0.25f), c + new Vector2(-s * 0.25f, s * 0.65f), color);
                handle.DrawLine(c + new Vector2(s * 0.65f, s * 0.25f), c + new Vector2(s * 0.25f, s * 0.65f), color);
                break;
            }
            case DutyNeuroOrderType.Hold:
            {
                // Тактический щит.
                var shield = _shieldPoints;
                shield[0] = c + new Vector2(-s * 0.60f, -s * 0.65f);
                shield[1] = c + new Vector2(s * 0.60f, -s * 0.65f);
                shield[2] = c + new Vector2(s * 0.55f, s * 0.15f);
                shield[3] = c + new Vector2(0f, s * 0.78f);
                shield[4] = c + new Vector2(-s * 0.55f, s * 0.15f);
                shield[5] = c + new Vector2(-s * 0.60f, -s * 0.65f);
                handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, shield, color);
                handle.DrawLine(c + new Vector2(0f, -s * 0.40f), c + new Vector2(0f, s * 0.45f), color);
                handle.DrawLine(c + new Vector2(-s * 0.30f, -s * 0.05f), c + new Vector2(s * 0.30f, -s * 0.05f), color);
                break;
            }
            case DutyNeuroOrderType.Retreat:
            {
                // Стрелка отступления (разворот назад).
                var path = _retreatPoints;
                path[0] = c + new Vector2(-s * 0.55f, s * 0.45f);
                path[1] = c + new Vector2(s * 0.35f, s * 0.45f);
                path[2] = c + new Vector2(s * 0.55f, s * 0.10f);
                path[3] = c + new Vector2(s * 0.35f, -s * 0.35f);
                path[4] = c + new Vector2(-s * 0.50f, -s * 0.35f);
                handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, path, color);
                handle.DrawLine(c + new Vector2(-s * 0.50f, -s * 0.35f), c + new Vector2(-s * 0.15f, -s * 0.65f), color);
                handle.DrawLine(c + new Vector2(-s * 0.50f, -s * 0.35f), c + new Vector2(-s * 0.15f, -s * 0.05f), color);
                break;
            }
            case DutyNeuroOrderType.Sos:
            {
                // Маяк SOS + текст SOS.
                var dims = handle.GetDimensions(_sosFont, _sosText, 1f);
                handle.DrawString(_sosFont, c - dims * 0.5f, _sosText, color);
                DrawArc(handle, c, s * 0.95f, -MathF.PI * 0.35f, MathF.PI * 0.35f, color.WithAlpha(0.7f), 1f);
                DrawArc(handle, c, s * 0.95f, MathF.PI * 0.65f, MathF.PI * 1.35f, color.WithAlpha(0.7f), 1f);
                break;
            }
            case DutyNeuroOrderType.Recon:
            {
                // Тактический глаз разведки.
                var upper = _eyeUpperPoints;
                upper[0] = c + new Vector2(-s * 0.80f, 0f);
                upper[1] = c + new Vector2(-s * 0.35f, -s * 0.45f);
                upper[2] = c + new Vector2(0f, -s * 0.55f);
                upper[3] = c + new Vector2(s * 0.35f, -s * 0.45f);
                upper[4] = c + new Vector2(s * 0.80f, 0f);
                var lower = _eyeLowerPoints;
                lower[0] = c + new Vector2(-s * 0.80f, 0f);
                lower[1] = c + new Vector2(-s * 0.35f, s * 0.45f);
                lower[2] = c + new Vector2(0f, s * 0.55f);
                lower[3] = c + new Vector2(s * 0.35f, s * 0.45f);
                lower[4] = c + new Vector2(s * 0.80f, 0f);
                handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, upper, color);
                handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, lower, color);
                handle.DrawCircle(c, s * 0.28f, color, false);
                handle.DrawCircle(c, s * 0.11f, color);
                break;
            }
        }
    }

    private void DrawBoxOutline(DrawingHandleScreen handle, UIBox2 box, Color color)
    {
        var pts = _boxPoints;
        pts[0] = box.TopLeft;
        pts[1] = box.TopRight;
        pts[2] = box.BottomRight;
        pts[3] = box.BottomLeft;
        pts[4] = box.TopLeft;
        handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, pts, color);
    }
}
