// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared._Duty.CodeAlpha;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._Duty.CodeAlpha;

/// <summary>
/// _Duty: держит панель отсчёта кода «Альфа» на игровом экране.
///
/// Данные берутся напрямую из сетевого <see cref="DutyCodeAlphaComponent"/> на станции, поэтому
/// панель видят все без исключения — живые, мёртвые и призраки. Отдельной рассылки не нужно.
/// </summary>
public sealed class DutyCodeAlphaTimerUIController : UIController
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private DutyCodeAlphaTimerWidget? _widget;

    /// <summary>
    /// Пороги, которые уже отработали. Нужны, чтобы вернуть спрятанную панель ровно один раз на
    /// каждом рубеже, а не каждый кадр после его пересечения.
    /// </summary>
    private readonly HashSet<int> _passedThresholds = new();

    public override void Initialize()
    {
        UIManager.OnScreenChanged += OnScreenChanged;
    }

    private void OnScreenChanged((UIScreen? Old, UIScreen? New) args)
    {
        // Виджет живёт внутри экрана: при смене экрана старый уходит вместе с ним.
        _widget = null;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        if (!TryGetAlpha(out var alpha))
        {
            if (_widget != null)
                _widget.Visible = false;

            _passedThresholds.Clear();
            return;
        }

        var screen = UIManager.ActiveScreen;
        if (screen == null)
            return;

        _widget ??= screen.GetOrAddWidget<DutyCodeAlphaTimerWidget>();

        var remaining = alpha.Deadline - _timing.CurTime;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        UpdateReshow(remaining);

        _widget.SetRemaining(remaining);
        _widget.Visible = !_widget.Dismissed;

        if (!_widget.Visible)
            return;

        var pos = new Vector2(
            screen.Size.X - _widget.DesiredSize.X - DutyCodeAlphaVisuals.Margin,
            screen.Size.Y - _widget.DesiredSize.Y - DutyCodeAlphaVisuals.Margin);

        LayoutContainer.SetPosition(_widget, pos);
    }

    /// <summary>
    /// Возвращает спрятанную панель на ключевых остатках. Единственный способ увидеть таймер
    /// снова после двойного ПКМ.
    /// </summary>
    private void UpdateReshow(TimeSpan remaining)
    {
        if (_widget == null)
            return;

        for (var i = 0; i < DutyCodeAlphaVisuals.ReshowThresholds.Length; i++)
        {
            if (remaining > DutyCodeAlphaVisuals.ReshowThresholds[i])
                continue;

            if (!_passedThresholds.Add(i))
                continue;

            _widget.Dismissed = false;
        }
    }

    private bool TryGetAlpha(out DutyCodeAlphaComponent alpha)
    {
        // AllEntityQueryEnumerator, а не EntityQueryEnumerator: сущность станции живёт в нулевом
        // пространстве, и обычный перечислитель пропустил бы её вместе с паузнутыми.
        var query = EntityManager.AllEntityQueryEnumerator<DutyCodeAlphaComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            alpha = comp;
            return true;
        }

        alpha = default!;
        return false;
    }
}
