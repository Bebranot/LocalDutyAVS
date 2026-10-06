// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Duty.LatheUi;

/// <summary>
/// Плоская кнопка-строка. Стандартный стиль "button" рисует объёмную рамку — в списке из сотни рецептов
/// это рябит, поэтому наведение и выбор рисуем своими плоскими стилбоксами.
/// </summary>
public class DutyLatheFlatButton : ContainerButton
{
    private bool _selected;
    private Color _accent = DutyLatheStyle.DefaultAccent;

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;

            _selected = value;
            DrawModeChanged();
        }
    }

    public Color Accent
    {
        get => _accent;
        set
        {
            if (_accent == value)
                return;

            _accent = value;
            DrawModeChanged();
        }
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();

        if (_selected)
        {
            StyleBoxOverride = DutyLatheStyle.RowSelected(_accent);
            return;
        }

        StyleBoxOverride = DrawMode switch
        {
            DrawModeEnum.Hover => DutyLatheStyle.RowHover,
            DrawModeEnum.Pressed => DutyLatheStyle.RowPressed,
            _ => DutyLatheStyle.RowNormal,
        };
    }
}

/// <summary>
/// Категория слева: название и счётчик подходящих рецептов (при поиске — сколько нашлось в этой категории).
/// </summary>
public sealed class DutyLatheCategoryButton : DutyLatheFlatButton
{
    public readonly string Key;
    private readonly Label _count;
    private int _lastCount = -1;

    public DutyLatheCategoryButton(string key, string name)
    {
        Key = key;
        HorizontalExpand = true;

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(8, 4, 6, 4),
        };
        box.AddChild(new Label
        {
            Text = name,
            ClipText = true,
            HorizontalExpand = true,
        });
        _count = new Label { FontColorOverride = DutyLatheStyle.TextMuted };
        box.AddChild(_count);
        AddChild(box);
    }

    public void SetCount(int count)
    {
        if (_lastCount == count)
            return;

        _lastCount = count;
        _count.Text = count.ToString();
        // Пустые категории не прячем — иначе список прыгает под курсором при наборе поиска.
        _count.FontColorOverride = count > 0 ? DutyLatheStyle.TextMuted : DutyLatheStyle.TextUnavailable;
    }
}

/// <summary>
/// Значок в шапке окна: что умеет станок (исследования, чертежи, мензурка, канал объявлений).
/// </summary>
public sealed class DutyLatheBadge : PanelContainer
{
    private readonly Label _label;

    public DutyLatheBadge()
    {
        PanelOverride = DutyLatheStyle.Chip;
        // Без ClipText: у обрезаемой метки нулевая желаемая ширина, и значок схлопнулся бы.
        // Ширину окна это не раздувает — ряд значков сам обрезается (RectClipContent в разметке).
        _label = new Label();
        AddChild(_label);
    }

    public void Set(string text, string tooltip, Color color)
    {
        _label.Text = text;
        _label.FontColorOverride = color;
        ToolTip = tooltip;
    }
}
