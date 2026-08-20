using System.Collections.Generic;
using Content.Client.Stylesheets;
using Content.Shared.Construction.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Maths;

namespace Content.Client._Duty.Construction;

/// <summary>
/// Узкая панель избранного, пристыкованная справа к меню строительства. Показывает избранные
/// рецепты только иконками — вся информация о выбранном по-прежнему выводится в общий инфо-блок
/// меню, отдельного окна с описанием тут нет.
/// Панель появляется только когда есть хотя бы один доступный избранный рецепт; видимостью
/// управляет презентер, а не сама панель.
/// </summary>
public sealed class FavoritesPanel : PanelContainer
{
    private const int Columns = 2;
    private const int IconSeparation = 2;
    private const int ContentMargin = 4;

    /// <summary>
    /// Полоса прокрутки резервируется всегда, чтобы появление скролла при переполнении
    /// не сдвигало иконки.
    /// </summary>
    private const float ScrollBarReserve = 12f;

    /// <summary>
    /// Отступ, отделяющий панель от инфо-блока меню.
    /// </summary>
    private const float LeftMargin = 5f;

    private const float ContentWidth = FavoriteRecipeButton.ButtonSize * Columns
                                       + IconSeparation
                                       + ContentMargin * 2
                                       + ScrollBarReserve;

    /// <summary>
    /// Ширина, на которую окно меню расширяется вправо при показе панели, вместе с отступом.
    /// </summary>
    public const float PanelWidth = ContentWidth + LeftMargin;

    private readonly GridContainer _grid;
    private readonly Dictionary<string, FavoriteRecipeButton> _buttons = new();

    public event Action<ConstructionPrototype>? OnRecipeSelected;
    public event Action<ConstructionPrototype>? OnRecipeActivated;
    public event Action<ConstructionPrototype>? OnRecipeUnfavorited;

    public FavoritesPanel()
    {
        MinWidth = ContentWidth;
        Margin = new Thickness(LeftMargin, 0f, 0f, 0f);
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = StyleNano.PanelDark,
            ContentMarginLeftOverride = ContentMargin,
            ContentMarginRightOverride = ContentMargin,
            ContentMarginTopOverride = ContentMargin,
            ContentMarginBottomOverride = ContentMargin,
        };

        _grid = new GridContainer
        {
            Columns = Columns,
            HSeparationOverride = IconSeparation,
            VSeparationOverride = IconSeparation,
        };

        AddChild(new ScrollContainer
        {
            HScrollEnabled = false,
            VerticalExpand = true,
            Children = { _grid },
        });
    }

    /// <summary>
    /// Перестроить содержимое. <paramref name="recipes"/> уже отфильтрованы по доступности
    /// и идут в порядке добавления в избранное — панель порядок не меняет.
    /// </summary>
    public void Populate(
        IReadOnlyList<(ConstructionPrototype Recipe, EntityPrototype Target)> recipes,
        ConstructionPrototype? selected)
    {
        _grid.RemoveAllChildren();
        _buttons.Clear();

        foreach (var (recipe, target) in recipes)
        {
            var button = new FavoriteRecipeButton(recipe, target);

            button.OnRecipeSelected += pressed => OnRecipeSelected?.Invoke(pressed.Recipe);
            button.OnRecipeActivated += pressed => OnRecipeActivated?.Invoke(pressed.Recipe);
            button.OnRecipeUnfavorited += pressed => OnRecipeUnfavorited?.Invoke(pressed.Recipe);

            button.SetSelected(selected == recipe);

            _grid.AddChild(button);
            _buttons[recipe.ID] = button;
        }
    }

    /// <summary>
    /// Перенести подсветку выбранного рецепта. Если выбранного рецепта в избранном нет,
    /// подсветка просто снимается со всех иконок.
    /// </summary>
    public void SetSelected(ConstructionPrototype? selected)
    {
        foreach (var (id, button) in _buttons)
        {
            button.SetSelected(selected != null && id == selected.ID);
        }
    }
}
