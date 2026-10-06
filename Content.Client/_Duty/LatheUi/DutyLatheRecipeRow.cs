// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Research.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.LatheUi;

/// <summary>
/// Одна позиция стоимости рецепта: материал из хранилища или реагент из мензурки.
/// </summary>
public sealed class DutyLatheCost
{
    /// <summary>Id материала (единицы хранилища) или реагента (ед.).</summary>
    public string Id = string.Empty;

    public bool IsReagent;

    /// <summary>Сколько нужно на одну деталь — в единицах хранилища или в ед. реагента, уже с учётом деталей станка.</summary>
    public float Needed;

    /// <summary>Сколько показывать на одну деталь — в листах для материала.</summary>
    public float Shown;

    /// <summary>Единиц хранилища в одном показанном (объём листа; для реагента 1) — чтобы «есть N» было в листах.</summary>
    public float PerShown = 1;

    public string Name = string.Empty;
    public Texture? Icon;
    public Color Color;
}

/// <summary>
/// Строка рецепта. Создаётся один раз на всё время жизни окна: поиск и фильтры только переключают Visible,
/// а обновления с сервера лишь перекрашивают стоимость. Раньше на каждую букву поиска пересоздавалась иконка,
/// а EntityPrototypeView спавнит клиентскую сущность — отсюда и были лаги.
/// </summary>
public sealed class DutyLatheRecipeRow : DutyLatheFlatButton
{
    public readonly LatheRecipePrototype Recipe;
    public readonly string RecipeName;

    /// <summary>Название и описание в нижнем регистре, «ё» → «е»: считается один раз, а не на каждую букву.</summary>
    public readonly string SearchText;

    public IReadOnlyList<DutyLatheCost> Costs => _costs;

    public bool CanProduce { get; private set; }

    /// <summary>Примерное время одной детали без учёта смазки (её знает только сервер).</summary>
    public double ItemSeconds { get; private set; }

    private readonly Label _name;
    private readonly Label _time;
    private readonly BoxContainer _costBox;
    private readonly List<DutyLatheCost> _costs = new();
    private readonly List<Label> _costLabels = new();
    private bool _initialized;

    public DutyLatheRecipeRow(LatheRecipePrototype recipe, string name, string description, Control icon)
    {
        Recipe = recipe;
        RecipeName = name;
        SearchText = DutyLatheSearch.Normalize(name + "\n" + description);
        HorizontalExpand = true;

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(6, 3, 8, 3),
            SeparationOverride = 8,
        };

        var iconHolder = new BoxContainer
        {
            SetSize = new Vector2(32, 32),
            VerticalAlignment = VAlignment.Center,
        };
        iconHolder.AddChild(icon);
        box.AddChild(iconHolder);

        var text = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };
        _name = new Label { Text = name, ClipText = true };
        text.AddChild(_name);

        _costBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 3,
            RectClipContent = true,
        };
        text.AddChild(_costBox);
        box.AddChild(text);

        _time = new Label
        {
            FontColorOverride = DutyLatheStyle.TextMuted,
            VerticalAlignment = VAlignment.Center,
            MinWidth = 52,
            Align = Label.AlignMode.Right,
        };
        box.AddChild(_time);

        AddChild(box);
    }

    /// <summary>
    /// Пересобрать стоимость и время — только при смене деталей станка (множители), не при каждом обновлении.
    /// </summary>
    public void SetCosts(List<DutyLatheCost> costs, double itemSeconds)
    {
        _costs.Clear();
        _costs.AddRange(costs);
        _costLabels.Clear();
        _costBox.RemoveAllChildren();

        foreach (var cost in _costs)
        {
            // Pass: подсказка с названием материала показывается, а клик всё равно доходит до строки.
            var chip = new PanelContainer
            {
                PanelOverride = DutyLatheStyle.Chip,
                MouseFilter = MouseFilterMode.Pass,
            };
            var chipBox = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                SeparationOverride = 3,
            };

            if (cost.Icon != null)
            {
                chipBox.AddChild(new TextureRect
                {
                    Texture = cost.Icon,
                    SetSize = new Vector2(14, 14),
                    Stretch = TextureRect.StretchMode.KeepAspectCentered,
                    VerticalAlignment = VAlignment.Center,
                });
            }
            else
            {
                chipBox.AddChild(new PanelContainer
                {
                    SetSize = new Vector2(8, 8),
                    VerticalAlignment = VAlignment.Center,
                    PanelOverride = new StyleBoxFlat { BackgroundColor = cost.Color },
                });
            }

            var label = new Label { Text = DutyLatheStyle.FormatAmount(cost.Shown) };
            chipBox.AddChild(label);
            chip.AddChild(chipBox);
            chip.ToolTip = cost.Name;
            _costBox.AddChild(chip);
            _costLabels.Add(label);
        }

        ItemSeconds = itemSeconds;
        _time.Text = DutyLatheStyle.FormatTime(itemSeconds);
        _initialized = false;
    }

    /// <summary>
    /// Перекрасить по наличию материалов на <paramref name="amount"/> штук. Возвращает true, если доступность поменялась —
    /// тогда меню переприменит фильтр «Доступно».
    /// </summary>
    public bool RefreshAvailability(Dictionary<string, int> materials, Solution? beaker, int amount)
    {
        var canProduce = true;
        for (var i = 0; i < _costs.Count; i++)
        {
            var enough = HasEnough(_costs[i], materials, beaker, amount);
            canProduce &= enough;
            _costLabels[i].FontColorOverride = enough ? DutyLatheStyle.Text : DutyLatheStyle.Bad;
        }

        if (_initialized && canProduce == CanProduce)
            return false;

        _initialized = true;
        CanProduce = canProduce;
        _name.FontColorOverride = canProduce ? DutyLatheStyle.Text : DutyLatheStyle.TextUnavailable;
        return true;
    }

    public static float Have(DutyLatheCost cost, Dictionary<string, int> materials, Solution? beaker)
    {
        if (cost.IsReagent)
            return beaker?.GetTotalPrototypeQuantity(new ProtoId<ReagentPrototype>(cost.Id)).Float() ?? 0f;

        return materials.GetValueOrDefault(cost.Id);
    }

    public static bool HasEnough(DutyLatheCost cost, Dictionary<string, int> materials, Solution? beaker, int amount)
    {
        // Реагент без мензурки — сервер такой рецепт не примет, даже если реагента нужно немного.
        if (cost.IsReagent && beaker == null)
            return false;

        return Have(cost, materials, beaker) >= cost.Needed * amount;
    }
}

/// <summary>
/// Нормализация строки для поиска: нижний регистр и «ё» как «е» — игроки пишут по-разному.
/// </summary>
public static class DutyLatheSearch
{
    public static string Normalize(string text)
    {
        return text.ToLowerInvariant().Replace('ё', 'е');
    }
}
