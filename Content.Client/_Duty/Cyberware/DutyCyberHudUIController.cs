// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Client._Duty.Neurointerface;
using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Neurointerface;
using Content.Client.UserInterface.Systems.Actions.Widgets;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared.CCVar;
using Content.Shared.Mobs.Systems;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Cyberware;

/// <summary>
/// _Duty: размещает панель киберсистемы. С активным визором NT-SEC — сразу под ним, как его продолжение;
/// без визора — на том же месте, где был бы визор (справа от хотбара, под верхним меню).
/// </summary>
public sealed class DutyCyberHudUIController : UIController
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private const float ScreenMargin = 10f;
    private const float ElementGap = 10f;
    private const float ActionsBarGap = 26f;
    private const float DockGap = 9f;

    private DutyCyberHudWidget? _widget;
    private bool _enabled = true;

    public override void Initialize()
    {
        base.Initialize();
        UIManager.OnScreenChanged += _ => _widget = null;
        _cfg.OnValueChanged(DutyCCVars.CyberHudEnabled, v => _enabled = v, true);
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var screen = UIManager.ActiveScreen;
        if (screen == null)
            return;

        if (!EntityManager.TrySystem<DutyCyberwareClientSystem>(out var cyber) ||
            !EntityManager.TrySystem<MobStateSystem>(out var mobState))
            return;

        if (!_enabled || !cyber.TryGetLocalHost(out var uid, out var host) || mobState.IsDead(uid))
        {
            _widget?.UpdateState(null, false, TimeSpan.Zero, args.DeltaSeconds);
            return;
        }

        var neuro = screen.GetWidget<DutyNeuroHudWidget>();
        var docked = neuro != null && neuro.Visible && neuro.DesiredSize.Y > 1f;

        // Под тактической сетью Горлекса панель берёт красную палитру — иначе выглядит как чужой NT-блок.
        var dockedFaction = EntityManager.TryGetComponent(uid, out DutyNeurointerfaceWearerComponent? wearer) &&
                            wearer.Network == DutyNeuroNetwork.Gorlex
            ? DutyCyberFaction.Gorlex
            : DutyCyberFaction.NanoTrasen;

        _widget ??= screen.GetOrAddWidget<DutyCyberHudWidget>();
        _widget.UpdateState(host, docked, cyber.CurTime, args.DeltaSeconds, dockedFaction);

        if (!_widget.Visible)
            return;

        Vector2 pos;
        if (docked)
        {
            // Левый край совпадает с полосами визора — выглядит как четвёртый блок той же проекции.
            var origin = screen.GlobalPosition;
            pos = new Vector2(neuro!.GlobalPosition.X - origin.X, neuro.GlobalPosition.Y - origin.Y + neuro.DesiredSize.Y + DockGap);
        }
        else
        {
            pos = ComputeFreePosition(screen);
        }

        pos = ClampToScreen(screen, _widget, pos);
        LayoutContainer.SetPosition(_widget, pos);
    }

    private static Vector2 ComputeFreePosition(UIScreen screen)
    {
        var origin = screen.GlobalPosition;
        var topBar = screen.GetWidget<GameTopMenuBar>();
        var actionsBar = screen.GetWidget<ActionsBar>();

        var y = ScreenMargin;
        if (topBar != null && topBar.VisibleInTree && topBar.Size.Y > 1f &&
            topBar.GlobalPosition.X - origin.X < screen.Size.X * 0.5f)
        {
            y = topBar.GlobalPosition.Y - origin.Y + topBar.Size.Y + ElementGap;
        }

        var x = ScreenMargin;
        if (actionsBar != null && actionsBar.VisibleInTree)
        {
            var right = float.MinValue;
            var top = float.MaxValue;
            foreach (var child in actionsBar.ActionsContainer.Children)
            {
                if (!child.Visible || child.Size.X <= 1f || child.Size.Y <= 1f)
                    continue;

                right = MathF.Max(right, child.GlobalPosition.X + child.Size.X);
                top = MathF.Min(top, child.GlobalPosition.Y);
            }

            if (right > float.MinValue)
            {
                x = MathF.Max(x, right - origin.X + ActionsBarGap);
                y = MathF.Max(y, top - origin.Y);
            }
        }

        return new Vector2(x, y);
    }

    private static Vector2 ClampToScreen(UIScreen screen, Control widget, Vector2 pos)
    {
        var size = screen.Size;
        var w = widget.DesiredSize.X > 1f ? widget.DesiredSize.X : 200f;
        var h = widget.DesiredSize.Y > 1f ? widget.DesiredSize.Y : 80f;

        if (size.X > w + ScreenMargin * 2f)
            pos.X = Math.Clamp(pos.X, ScreenMargin, size.X - w - ScreenMargin);
        if (size.Y > h + ScreenMargin * 2f)
            pos.Y = Math.Clamp(pos.Y, ScreenMargin, size.Y - h - ScreenMargin);

        return pos;
    }
}
