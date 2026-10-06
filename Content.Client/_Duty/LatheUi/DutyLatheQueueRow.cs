// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Client.Stylesheets;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Duty.LatheUi;

/// <summary>
/// Партия в очереди: номер, иконка, «напечатано/всего», время до конца партии и кнопки ⏶ ⏷ ✖.
/// Строки переиспользуются по индексу; иконка меняется, только если на этом месте другой рецепт.
/// </summary>
public sealed class DutyLatheQueueRow : BoxContainer
{
    public event Action<int>? OnMoveUp;
    public event Action<int>? OnMoveDown;
    public event Action<int>? OnDelete;

    private int _index;
    private string? _iconRecipe;

    private readonly Label _number;
    private readonly BoxContainer _iconHolder;
    private readonly Label _name;
    private readonly Label _progress;
    private readonly Button _up;
    private readonly Button _down;

    public DutyLatheQueueRow()
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 4;
        Margin = new Thickness(0, 1);

        _number = new Label
        {
            MinWidth = 16,
            FontColorOverride = DutyLatheStyle.TextMuted,
            VerticalAlignment = VAlignment.Center,
        };
        AddChild(_number);

        _iconHolder = new BoxContainer
        {
            SetSize = new Vector2(32, 32),
            VerticalAlignment = VAlignment.Center,
        };
        AddChild(_iconHolder);

        var text = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };
        _name = new Label { ClipText = true, MouseFilter = MouseFilterMode.Pass }; // Pass — чтобы работала подсказка с полным названием
        _progress = new Label { ClipText = true, FontColorOverride = DutyLatheStyle.TextMuted };
        text.AddChild(_name);
        text.AddChild(_progress);
        AddChild(text);

        _up = new Button
        {
            Text = "⏶",
            StyleClasses = { StyleClass.ButtonOpenRight },
            ToolTip = Loc.GetString("lathe-menu-move-up-tooltip"),
            VerticalAlignment = VAlignment.Center,
        };
        _down = new Button
        {
            Text = "⏷",
            StyleClasses = { StyleClass.ButtonOpenBoth },
            ToolTip = Loc.GetString("lathe-menu-move-down-tooltip"),
            VerticalAlignment = VAlignment.Center,
        };
        var delete = new Button
        {
            Text = "✖",
            StyleClasses = { StyleClass.ButtonOpenLeft, StyleClass.Negative },
            ToolTip = Loc.GetString("lathe-menu-delete-item-tooltip"),
            VerticalAlignment = VAlignment.Center,
        };

        _up.OnPressed += _ => OnMoveUp?.Invoke(_index);
        _down.OnPressed += _ => OnMoveDown?.Invoke(_index);
        delete.OnPressed += _ => OnDelete?.Invoke(_index);

        AddChild(_up);
        AddChild(_down);
        AddChild(delete);
    }

    public void Set(int index, int count, string recipeId, string name, string progress, Func<Control> iconFactory)
    {
        _index = index;
        _number.Text = (index + 1).ToString();
        _name.Text = name;
        _name.ToolTip = name;
        _progress.Text = progress;
        _up.Disabled = index == 0;
        _down.Disabled = index == count - 1;

        if (_iconRecipe == recipeId)
            return;

        _iconRecipe = recipeId;
        _iconHolder.RemoveAllChildren();
        _iconHolder.AddChild(iconFactory());
    }
}
