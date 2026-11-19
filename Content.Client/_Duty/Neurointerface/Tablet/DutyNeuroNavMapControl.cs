// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Numerics;
using Content.Client.Pinpointer.UI;
using Content.Client.Stylesheets;
using Content.Shared._Duty.Neurointerface;
using Content.Shared.Pinpointer;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._Duty.Neurointerface.Tablet;

/// <summary>
/// _Duty: тактическая навигационная карта планшета NT-SEC в киберпанк-палитре.
/// Отрисовывает каркас станции в багровых тонах, бойцов отряда, активные приказы
/// и открывает радиальное меню приказов по ПКМ.
/// </summary>
public sealed partial class DutyNeuroNavMapControl : NavMapControl
{
    private static readonly Color MapBgColor = Color.FromHex("#060A10");
    private static readonly Color MapWallColor = Color.FromHex("#C82337");
    private static readonly Color MapWindowColor = Color.FromHex("#7D1A2B");
    private static readonly Color MapTileColor = Color.FromHex("#14090E");
    private static readonly Color GridLineColor = Color.FromHex("#C82337").WithAlpha(0.08f);
    private static readonly Color GridAxisColor = Color.FromHex("#C82337").WithAlpha(0.16f);
    private static readonly Color SquadDotColor = Color.FromHex("#FF8C24");
    private static readonly Color CritDotColor = Color.FromHex("#FACC15");
    private static readonly Color DeadDotColor = Color.FromHex("#EF4444");
    private static readonly Color OfflineDotColor = Color.FromHex("#6B7280");

    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    [Dependency] private IResourceCache _resourceCache = default!;

    private readonly SharedTransformSystem _xformSystem;
    private readonly Font _markerFont;
    private readonly Font _noDataFont;

    private static readonly List<DutyNeuroSquadMemberEntry> EmptyMembers = new();
    private static readonly List<DutyNeuroOrderEntry> EmptyOrders = new();

    // Единые цвета и подписи приказов: окно планшета и радиальное меню берут их отсюда.
    private static readonly DutyNeuroOrderType[] AllOrderTypes =
    {
        DutyNeuroOrderType.Move,
        DutyNeuroOrderType.Assault,
        DutyNeuroOrderType.Hold,
        DutyNeuroOrderType.Retreat,
        DutyNeuroOrderType.Sos,
        DutyNeuroOrderType.Recon
    };

    private static string[]? _shortLabels;
    private static string? _unknownShortLabel;

    private readonly string _noDataText;
    private readonly string _defaultZoneName;

    // Буфер для рамок: DrawPrimitives копирует вершины, поэтому один массив безопасно переиспользуется.
    private readonly Vector2[] _boxPoints = new Vector2[5];
    private readonly Vector2[] _diamond = new Vector2[4];
    private readonly Vector2[] _diamondOutline = new Vector2[5];

    private List<DutyNeuroSquadMemberEntry> _members = EmptyMembers;
    private List<DutyNeuroOrderEntry> _orders = EmptyOrders;

    // Подписи пересобираются при смене данных, а не каждый кадр в Draw.
    private string[] _memberLabels = Array.Empty<string>();
    private string[] _orderTags = Array.Empty<string>();

    protected override Vector2 MidPointVector => PixelSize * 0.5f;
    protected override int MidPoint => Math.Max(1, (int)(Math.Min(PixelWidth, PixelHeight) * 0.5f));
    protected override int ScaledMinimapRadius => Math.Max(1, MidPoint - (int)(MinimapMargin * UIScale));

    /// <summary>
    /// Событие ПКМ по карте: (локальные координаты на гриде, ближайшая зона, экранные координаты курсора).
    /// </summary>
    public event Action<EntityCoordinates, string, Vector2>? OnMapRightClicked;

    /// <summary>
    /// Двойной быстрый ЛКМ по ромбу приказа: передаётся id приказа.
    /// </summary>
    public event Action<int>? OnOrderDoubleClicked;

    private const float ClickMaxDrag = 4f;
    private const float DoubleClickMaxDist = 8f;
    private const float DoubleClickTime = 0.35f;
    private const float HitPadding = 4f;

    // Буферы хит-теста: заполняются в DrawOrders, переиспользуются без аллокаций.
    private readonly List<int> _hitIds = new();
    private readonly List<Vector2> _hitCenters = new();
    private readonly List<float> _hitRadii = new();

    private bool _leftDown;
    private Vector2 _leftDownPos;
    private Vector2 _mousePos;
    private bool _mouseInside;
    private int _lastClickOrderId = -1;
    private Vector2 _lastClickPos;
    private TimeSpan _lastClickTime;

    /// <summary>
    /// Текущее приближение карты, отформатированное как "1,0x".
    /// </summary>
    public string FormattedZoom => $"{ZoomFactor.ToString("0.0", RuCulture)}x";

    /// <summary>
    /// Числовое приближение карты; окно сравнивает округлённое значение, чтобы не пересоздавать строку каждый кадр.
    /// </summary>
    public float ZoomFactor => WorldRange > 0f ? DefaultDisplayedRange / WorldRange : 1f;

    /// <summary>
    /// Отображать ли названия отделов и маяков на карте.
    /// </summary>
    public bool ShowBeacons
    {
        get => BeaconsCheckbox.Pressed;
        set => BeaconsCheckbox.Pressed = value;
    }

    public DutyNeuroNavMapControl()
    {
        IoCManager.InjectDependencies(this);
        _xformSystem = EntManager.System<SharedTransformSystem>();

        // Сбрасываем жёсткий размер MapGridControl, чтобы карта тянулась по контейнеру планшета.
        SetSize = new Vector2(float.NaN, float.NaN);
        HorizontalExpand = true;
        VerticalExpand = true;
        RectClipContent = true;

        // Скрываем стандартную верхнюю панель NavMapControl: планшет рисует свою панель над картой.
        NavMapTopPanel.Visible = false;

        WallColor = MapWallColor;
        WindowColor = MapWindowColor;
        TileColor = MapTileColor;
        BackgroundColor = MapBgColor.WithAlpha(0.92f);

        WorldMinRange = 10f;
        WorldMaxRange = 140f;
        WorldRange = DefaultDisplayedRange;
        ActualRadarRange = DefaultDisplayedRange;

        _markerFont = _resourceCache.NotoStack(variation: "Bold", size: 9);
        _noDataFont = _resourceCache.NotoStack(variation: "Bold", size: 13);

        _noDataText = Loc.GetString("duty-neuro-tablet-map-no-data");
        _defaultZoneName = Loc.GetString("duty-neuro-tablet-zone-default");
    }

    /// <summary>
    /// Обновляет списки отображаемых бойцов активного отряда и приказов на карте.
    /// </summary>
    public void SetOverlayData(List<DutyNeuroSquadMemberEntry> members, List<DutyNeuroOrderEntry> orders)
    {
        _members = members;
        _orders = orders;

        if (_memberLabels.Length != members.Count)
            _memberLabels = new string[members.Count];
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            _memberLabels[i] = member.IsLeader ? $"★ {ShortName(member.Name)}" : ShortName(member.Name);
        }

        if (_orderTags.Length != orders.Count)
            _orderTags = new string[orders.Count];
        for (var i = 0; i < orders.Count; i++)
        {
            var order = orders[i];
            _orderTags[i] = $"[{GetOrderShortLabel(order.OrderType)}] {order.ZoneName}";
        }
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            if (TryScreenToGridCoordinates(args.PointerLocation.Position, out var coords, out var zoneName))
            {
                OnMapRightClicked?.Invoke(coords, zoneName, args.PointerLocation.Position);
                args.Handle();
                return;
            }
        }

        // Базовая обработка ЛКМ (панорамирование) всегда сохраняется; мы только запоминаем точку нажатия.
        if (args.Function == EngineKeyFunctions.UIClick)
        {
            _leftDownPos = args.RelativePixelPosition;
            _leftDown = true;
        }

        base.KeyBindDown(args);
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            args.Handle();
            return;
        }

        if (args.Function == EngineKeyFunctions.UIClick && _leftDown)
        {
            _leftDown = false;
            var upPos = args.RelativePixelPosition;

            // Это «клик», только если курсор не утащили (иначе это было панорамирование).
            if ((upPos - _leftDownPos).Length() < ClickMaxDrag * UIScale)
                RegisterClick(upPos);
        }

        base.KeyBindUp(args);
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _mousePos = args.RelativePixelPosition;
        _mouseInside = true;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _mouseInside = false;
        _leftDown = false;
    }

    /// <summary>
    /// Учитывает короткий клик: два быстрых клика рядом по одному ромбу дают двойной клик по приказу.
    /// </summary>
    private void RegisterClick(Vector2 pos)
    {
        var orderId = FindOrderAt(pos);
        var now = Timing.RealTime;

        if (orderId >= 0 &&
            _lastClickOrderId == orderId &&
            (now - _lastClickTime).TotalSeconds <= DoubleClickTime &&
            (pos - _lastClickPos).Length() <= DoubleClickMaxDist * UIScale)
        {
            // Сбрасываем состояние, чтобы третий клик не считался новым двойным.
            _lastClickOrderId = -1;
            OnOrderDoubleClicked?.Invoke(orderId);
            return;
        }

        _lastClickOrderId = orderId;
        _lastClickPos = pos;
        _lastClickTime = now;
    }

    /// <summary>
    /// Ищет ближайший к точке ромб приказа по буферу, заполненному при последней отрисовке. -1, если нет.
    /// </summary>
    private int FindOrderAt(Vector2 pos)
    {
        var best = -1;
        var bestDist = float.PositiveInfinity;
        var pad = HitPadding * UIScale;

        for (var i = 0; i < _hitIds.Count; i++)
        {
            var dist = (pos - _hitCenters[i]).Length();
            if (dist <= _hitRadii[i] + pad && dist < bestDist)
            {
                bestDist = dist;
                best = _hitIds[i];
            }
        }

        return best;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        // Фон киберпанк-радара и тактическая сетка.
        handle.DrawRect(PixelSizeBox, MapBgColor);
        DrawTacticalGrid(handle);

        base.Draw(handle);

        if (MapUid == null || !EntManager.TryGetComponent<NavMapComponent>(MapUid, out var navMap) ||
            !EntManager.TryGetComponent<MapGridComponent>(MapUid, out _) ||
            !EntManager.TryGetComponent<TransformComponent>(MapUid, out var gridXform))
        {
            DrawNoMapData(handle);
            return;
        }

        EntManager.TryGetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(MapUid, out var physics);
        var offset = Offset + (physics?.LocalCenter ?? Vector2.Zero);

        DrawSquadMembers(handle, navMap, gridXform, offset);
        DrawOrders(handle, gridXform, offset);
    }

    private void DrawTacticalGrid(DrawingHandleScreen handle)
    {
        var size = PixelSize;
        if (size.X <= 0f || size.Y <= 0f)
            return;

        var step = MathF.Max(28f, 36f * UIScale);
        var center = MidPointVector;

        for (var x = center.X % step; x < size.X; x += step)
            handle.DrawLine(new Vector2(x, 0f), new Vector2(x, size.Y), GridLineColor);

        for (var y = center.Y % step; y < size.Y; y += step)
            handle.DrawLine(new Vector2(0f, y), new Vector2(size.X, y), GridLineColor);

        handle.DrawLine(new Vector2(center.X, 0f), new Vector2(center.X, size.Y), GridAxisColor);
        handle.DrawLine(new Vector2(0f, center.Y), new Vector2(size.X, center.Y), GridAxisColor);
    }

    private void DrawNoMapData(DrawingHandleScreen handle)
    {
        var msg = _noDataText;
        var dims = handle.GetDimensions(_noDataFont, msg, 1f);
        handle.DrawString(_noDataFont, MidPointVector - dims * 0.5f, msg, MapWallColor);
    }

    private void DrawSquadMembers(
        DrawingHandleScreen handle,
        NavMapComponent navMap,
        TransformComponent gridXform,
        Vector2 offset)
    {
        var time = (float)Timing.RealTime.TotalSeconds;
        var pulse = 0.5f + 0.5f * MathF.Sin(time * 4.5f);
        var invGridMatrix = _xformSystem.GetInvWorldMatrix(gridXform);

        var clipBox = UIBox2.FromDimensions(new Vector2(-24f, -24f), PixelSize + new Vector2(48f, 48f));

        for (var i = 0; i < _members.Count; i++)
        {
            var member = _members[i];
            if (!TryGetMemberGridPos(member, i, navMap, invGridMatrix, out var gridLocalPos))
                continue;

            var screenPos = ScalePosition(new Vector2(gridLocalPos.X, -gridLocalPos.Y) - new Vector2(offset.X, -offset.Y));
            if (!clipBox.Contains(screenPos))
                continue;

            var dotColor = member.Status switch
            {
                DutyNeuroMemberStatus.Critical => CritDotColor,
                DutyNeuroMemberStatus.Dead => DeadDotColor,
                DutyNeuroMemberStatus.Offline => OfflineDotColor,
                _ => SquadDotColor
            };

            var radius = MathF.Max(4f, 4.5f * UIScale);

            // Внешнее свечение оранжевой/статусной метки бойца.
            handle.DrawCircle(screenPos, radius * (1.9f + pulse * 0.45f), dotColor.WithAlpha(0.22f));
            handle.DrawCircle(screenPos, radius + 1.5f * UIScale, MapBgColor);
            handle.DrawCircle(screenPos, radius, dotColor);

            if (member.IsLeader)
                handle.DrawCircle(screenPos, radius + 3.5f * UIScale, Color.FromHex("#38BDF8"), false);

            var label = _memberLabels[i];
            var textSize = handle.GetDimensions(_markerFont, label, 1f);
            var textPos = screenPos + new Vector2(-textSize.X * 0.5f, radius + 3f * UIScale);
            var bgBox = UIBox2.FromDimensions(textPos - new Vector2(3f, 1f), textSize + new Vector2(6f, 2f));
            handle.DrawRect(bgBox, MapBgColor.WithAlpha(0.82f));
            handle.DrawString(_markerFont, textPos, label, dotColor);
        }
    }

    private void DrawOrders(
        DrawingHandleScreen handle,
        TransformComponent gridXform,
        Vector2 offset)
    {
        var time = (float)Timing.RealTime.TotalSeconds;
        var pulse = 0.5f + 0.5f * MathF.Sin(time * 5.2f);
        var invGridMatrix = _xformSystem.GetInvWorldMatrix(gridXform);

        var diamond = _diamond;
        var outline = _diamondOutline;
        var clipBox = UIBox2.FromDimensions(new Vector2(-32f, -32f), PixelSize + new Vector2(64f, 64f));

        _hitIds.Clear();
        _hitCenters.Clear();
        _hitRadii.Clear();

        // Ромб под курсором подсвечиваем (только если не тащим карту).
        var hoverId = _mouseInside && !_leftDown ? FindOrderAt(_mousePos) : -1;

        for (var i = 0; i < _orders.Count; i++)
        {
            var order = _orders[i];
            var coords = EntManager.GetCoordinates(order.Coordinates);
            var mapPos = _xformSystem.ToMapCoordinates(coords);
            if (mapPos.MapId == MapId.Nullspace)
                continue;

            var gridLocal = Vector2.Transform(mapPos.Position, invGridMatrix);
            var screenPos = ScalePosition(new Vector2(gridLocal.X, -gridLocal.Y) - new Vector2(offset.X, -offset.Y));
            if (!clipBox.Contains(screenPos))
                continue;

            var color = GetOrderColor(order.OrderType);
            var size = (order.OrderType == DutyNeuroOrderType.Sos ? 8.5f + pulse * 2.5f : 8f) * UIScale;

            _hitIds.Add(order.OrderId);
            _hitCenters.Add(screenPos);
            _hitRadii.Add(size);

            var hovered = order.OrderId == hoverId;
            if (hovered)
                size *= 1.25f;

            diamond[0] = screenPos + new Vector2(0f, -size);
            diamond[1] = screenPos + new Vector2(size, 0f);
            diamond[2] = screenPos + new Vector2(0f, size);
            diamond[3] = screenPos + new Vector2(-size, 0f);

            outline[0] = diamond[0];
            outline[1] = diamond[1];
            outline[2] = diamond[2];
            outline[3] = diamond[3];
            outline[4] = diamond[0];

            // Внешнее свечение и ромб тактического приказа.
            handle.DrawCircle(screenPos, size * (1.45f + pulse * 0.25f), color.WithAlpha(0.18f));
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, diamond, color.WithAlpha(0.85f));
            handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, outline, hovered ? Color.White : Color.White.WithAlpha(0.9f));

            var tag = _orderTags[i];
            var textSize = handle.GetDimensions(_markerFont, tag, 1f);
            var textPos = screenPos + new Vector2(-textSize.X * 0.5f, -size - textSize.Y - 4f * UIScale);
            var box = UIBox2.FromDimensions(textPos - new Vector2(4f, 1.5f), textSize + new Vector2(8f, 3f));

            handle.DrawRect(box, MapBgColor.WithAlpha(0.90f));
            DrawBoxOutline(handle, box, color.WithAlpha(0.85f));
            handle.DrawString(_markerFont, textPos, tag, color);
        }
    }

    private bool TryGetMemberGridPos(
        DutyNeuroSquadMemberEntry member,
        int index,
        NavMapComponent navMap,
        Matrix3x2 invGridMatrix,
        out Vector2 gridLocalPos)
    {
        if (EntManager.TryGetEntity(member.Entity, out var uid) &&
            EntManager.TryGetComponent<TransformComponent>(uid, out var memberXform) &&
            memberXform.MapID != MapId.Nullspace)
        {
            var mapPos = _xformSystem.GetMapCoordinates(uid.Value, memberXform);
            gridLocalPos = Vector2.Transform(mapPos.Position, invGridMatrix);
            return true;
        }

        // Если боец вне PVS клиента — отображаем у маяка его текущей зоны.
        if (!string.IsNullOrWhiteSpace(member.Location) && member.Location != "---")
        {
            foreach (var beacon in navMap.Beacons.Values)
            {
                if (!string.Equals(beacon.Text, member.Location, StringComparison.OrdinalIgnoreCase))
                    continue;

                var angle = index * 2.1f;
                gridLocalPos = beacon.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 1.4f;
                return true;
            }
        }

        gridLocalPos = Vector2.Zero;
        return false;
    }

    private bool TryScreenToGridCoordinates(
        Vector2 screenPixelPos,
        out EntityCoordinates coordinates,
        out string zoneName)
    {
        coordinates = EntityCoordinates.Invalid;
        zoneName = _defaultZoneName;

        if (MapUid == null ||
            !EntManager.TryGetComponent<NavMapComponent>(MapUid, out var navMap) ||
            !EntManager.TryGetComponent<TransformComponent>(MapUid, out _) ||
            MinimapScale <= 0f)
        {
            return false;
        }

        EntManager.TryGetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(MapUid, out var physics);
        var offset = Offset + (physics?.LocalCenter ?? Vector2.Zero);

        var localControlPixel = screenPixelPos - GlobalPixelPosition;
        var unscaled = (localControlPixel - MidPointVector) / MinimapScale;
        var gridLocalPos = new Vector2(unscaled.X, -unscaled.Y) + offset;

        coordinates = new EntityCoordinates(MapUid.Value, gridLocalPos);
        zoneName = FindNearestBeaconName(navMap, gridLocalPos);
        return true;
    }

    private string FindNearestBeaconName(NavMapComponent navMap, Vector2 gridLocalPos)
    {
        var bestDistSq = float.PositiveInfinity;
        var bestName = _defaultZoneName;

        foreach (var beacon in navMap.Beacons.Values)
        {
            if (string.IsNullOrWhiteSpace(beacon.Text))
                continue;

            var distSq = (beacon.Position - gridLocalPos).LengthSquared();
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestName = beacon.Text;
            }
        }

        return bestName;
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

    private static string ShortName(string fullName)
    {
        var spaceIdx = fullName.IndexOf(' ');
        return spaceIdx > 0 ? fullName[..spaceIdx] : fullName;
    }

    public static Color GetOrderColor(DutyNeuroOrderType type) => type switch
    {
        DutyNeuroOrderType.Move => Color.FromHex("#38BDF8"),
        DutyNeuroOrderType.Assault => Color.FromHex("#FF3B4E"),
        DutyNeuroOrderType.Hold => Color.FromHex("#FF8C24"),
        DutyNeuroOrderType.Retreat => Color.FromHex("#FACC15"),
        DutyNeuroOrderType.Sos => Color.FromHex("#FF1E38"),
        DutyNeuroOrderType.Recon => Color.FromHex("#2DD4BF"),
        _ => Color.White
    };

    public static string GetOrderShortLabel(DutyNeuroOrderType type)
    {
        // Подписи кэшируются: метод зовётся при пересборке списков, а язык не меняется за сессию.
        if (_shortLabels == null)
        {
            var labels = new string[AllOrderTypes.Length];
            for (var i = 0; i < AllOrderTypes.Length; i++)
            {
                labels[i] = Loc.GetString(GetShortLabelKey(AllOrderTypes[i]));
            }

            _shortLabels = labels;
        }

        var index = (int) type;
        if (index >= 0 && index < _shortLabels.Length)
            return _shortLabels[index];

        return _unknownShortLabel ??= Loc.GetString("duty-neuro-order-short-unknown");
    }

    private static string GetShortLabelKey(DutyNeuroOrderType type) => type switch
    {
        DutyNeuroOrderType.Move => "duty-neuro-order-short-move",
        DutyNeuroOrderType.Assault => "duty-neuro-order-short-assault",
        DutyNeuroOrderType.Hold => "duty-neuro-order-short-hold",
        DutyNeuroOrderType.Retreat => "duty-neuro-order-short-retreat",
        DutyNeuroOrderType.Sos => "duty-neuro-order-short-sos",
        DutyNeuroOrderType.Recon => "duty-neuro-order-short-recon",
        _ => "duty-neuro-order-short-unknown"
    };
}
