// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Client.UserInterface.Systems.Actions.Widgets;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Neurointerface;

/// <summary>
/// _Duty: контроллер верхне-левого HUD нейроинтерфейса NT-SEC.
/// Динамически позиционирует виджет справа от <see cref="ActionsBar"/> и ниже <see cref="GameTopMenuBar"/>,
/// сдвигая его влево, когда панель действий скрывается или пустеет (например, при смерти).
/// </summary>
public sealed class DutyNeuroHudUIController : UIController
{
    private const float ScreenMargin = 10f;
    private const float ElementGap = 10f;

    /// <summary>Зазор от хотбара экшенов (1-9): визор не должен прилипать к слотам.</summary>
    private const float ActionsBarGap = 26f;

    private DutyNeuroHudWidget? _widget;

    public override void Initialize()
    {
        base.Initialize();
        UIManager.OnScreenChanged += OnScreenChanged;
    }

    private void OnScreenChanged((UIScreen? Old, UIScreen? New) args)
    {
        _widget = null;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var screen = UIManager.ActiveScreen;
        if (screen == null)
            return;

        if (!EntityManager.TrySystem<DutyNeuroClientSystem>(out var clientSystem))
            return;

        if (!clientSystem.TryGetActiveWearer(out _, out var wearer))
        {
            if (_widget != null)
                _widget.UpdateState(null, TimeSpan.Zero, args.DeltaSeconds);
            return;
        }

        _widget ??= screen.GetOrAddWidget<DutyNeuroHudWidget>();
        _widget.UpdateState(wearer, clientSystem.GetCurrentRoundTime(), args.DeltaSeconds);

        if (!_widget.Visible)
            return;

        var pos = ComputeHudPosition(screen, _widget);
        LayoutContainer.SetPosition(_widget, pos);
    }

    private static Vector2 ComputeHudPosition(UIScreen screen, DutyNeuroHudWidget widget)
    {
        var screenOrigin = screen.GlobalPosition;
        var screenSize = screen.Size;

        var topBar = screen.GetWidget<GameTopMenuBar>();
        var actionsBar = screen.GetWidget<ActionsBar>();

        var y = ScreenMargin;
        if (topBar != null &&
            topBar.VisibleInTree &&
            topBar.Size.Y > 1f &&
            topBar.GlobalPosition.X - screenOrigin.X < screenSize.X * 0.5f)
        {
            y = (topBar.GlobalPosition.Y - screenOrigin.Y) + topBar.Size.Y + ElementGap;
        }

        var x = ScreenMargin;
        // Контейнер ActionsBar заметно шире самих слотов 1-9, поэтому ориентируемся на видимые слоты,
        // иначе визор уезжает далеко вправо от кнопок.
        if (actionsBar != null &&
            actionsBar.VisibleInTree &&
            TryGetVisibleActionsBounds(actionsBar, out var slotsRight, out var slotsTop))
        {
            x = MathF.Max(x, slotsRight - screenOrigin.X + ActionsBarGap);
            y = MathF.Max(y, slotsTop - screenOrigin.Y);
        }

        var widgetWidth = widget.DesiredSize.X > 1f ? widget.DesiredSize.X : widget.MinWidth;
        var widgetHeight = widget.DesiredSize.Y > 1f ? widget.DesiredSize.Y : 240f;

        if (screenSize.X > widgetWidth + ScreenMargin * 2f)
            x = Math.Clamp(x, ScreenMargin, screenSize.X - widgetWidth - ScreenMargin);
        if (screenSize.Y > widgetHeight + ScreenMargin * 2f)
            y = Math.Clamp(y, ScreenMargin, screenSize.Y - widgetHeight - ScreenMargin);

        return new Vector2(x, y);
    }

    /// <summary>
    /// Правый край и верх видимых слотов хотбара в глобальных координатах; false, если слотов нет (например, при смерти).
    /// </summary>
    private static bool TryGetVisibleActionsBounds(ActionsBar actionsBar, out float right, out float top)
    {
        right = float.MinValue;
        top = float.MaxValue;
        var any = false;

        foreach (var child in actionsBar.ActionsContainer.Children)
        {
            if (!child.Visible || child.Size.X <= 1f || child.Size.Y <= 1f)
                continue;

            any = true;
            right = MathF.Max(right, child.GlobalPosition.X + child.Size.X);
            top = MathF.Min(top, child.GlobalPosition.Y);
        }

        return any;
    }
}
