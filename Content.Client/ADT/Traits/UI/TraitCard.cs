// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Traits;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;

namespace Content.Client.ADT.Traits.UI;

// _Duty: компактная карточка черты для переработанной вкладки черт (заменяет TraitEntry).
/// <summary>
/// Карточка одной черты: название, цена и короткая подсказка (почему черту нельзя взять).
/// Клик переключает выбор, наведение показывает подробности в правой панели.
/// </summary>
public sealed class TraitCard : PanelContainer
{
    public readonly ProtoId<TraitPrototype> TraitId;

    /// <summary>Нижний регистр: название + описание. Считается один раз, поиск потом не трогает Loc.</summary>
    public readonly string SearchText;

    public event Action<TraitCard>? Clicked;
    public event Action<TraitCard>? Hovered;

    private readonly Label _hint;

    public TraitCard(TraitPrototype trait, string name, string description, Color accent)
    {
        TraitId = trait.ID;
        SearchText = (name + " " + description).ToLowerInvariant();

        HorizontalExpand = true;
        Margin = new Thickness(0, 0, 0, 4);
        MouseFilter = MouseFilterMode.Stop;
        DefaultCursorShape = CursorShape.Hand;
        AddStyleClass("TraitsEntryPanel");

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };

        // Цветная полоска категории слева.
        row.AddChild(new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = accent },
            SetWidth = 3,
            MouseFilter = MouseFilterMode.Ignore,
        });

        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(8, 6),
            MouseFilter = MouseFilterMode.Ignore,
        };

        var top = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, MouseFilter = MouseFilterMode.Ignore };
        top.AddChild(new Label
        {
            Text = name,
            ClipText = true,
            HorizontalExpand = true,
            StyleClasses = { "TraitsEntryNameLabel" },
            MouseFilter = MouseFilterMode.Ignore,
        });

        var (costText, costColor) = FormatCost(trait.Cost);
        top.AddChild(new Label
        {
            Text = costText,
            ModulateSelfOverride = costColor,
            StyleClasses = { "TraitsEntryCostLabel" },
            MouseFilter = MouseFilterMode.Ignore,
        });
        content.AddChild(top);

        _hint = new Label
        {
            Visible = false,
            ClipText = true,
            ModulateSelfOverride = Color.FromHex("#a0a0a0"),
            StyleClasses = { "TraitsEntryDescriptionLabel" },
            MouseFilter = MouseFilterMode.Ignore,
        };
        content.AddChild(_hint);

        row.AddChild(content);
        AddChild(row);

        OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            args.Handle();
            Clicked?.Invoke(this);
        };
        OnMouseEntered += _ => Hovered?.Invoke(this);
    }

    /// <summary>
    /// Стоимость: положительная тратит очки («−2»), отрицательная даёт («+1»).
    /// </summary>
    public static (string Text, Color Color) FormatCost(int cost)
    {
        if (cost > 0)
            return ($"−{cost}", Color.FromHex("#f87171"));
        if (cost < 0)
            return ($"+{-cost}", Color.FromHex("#4ade80"));

        return ("0", Color.FromHex("#888888"));
    }

    /// <param name="selected">Черта выбрана.</param>
    /// <param name="dimmed">Взять нельзя (лимит, конфликт, требования).</param>
    /// <param name="hint">Короткая причина под названием.</param>
    public void SetState(bool selected, bool dimmed, string? hint)
    {
        if (selected)
            AddStyleClass("TraitsEntrySelected");
        else
            RemoveStyleClass("TraitsEntrySelected");

        if (dimmed && !selected)
            AddStyleClass("TraitsEntryDisabled");
        else
            RemoveStyleClass("TraitsEntryDisabled");

        var showHint = !string.IsNullOrEmpty(hint) && !selected;
        _hint.Visible = showHint;
        _hint.Text = showHint ? hint : string.Empty;
    }
}
