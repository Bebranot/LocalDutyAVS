using System.Collections.Generic;
using Content.Client.Stylesheets;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Duty.Construction;

/// <summary>
/// Вертикальный список категорий слева в меню строительства — замена выпадающему
/// <see cref="OptionButton"/>.
/// Категории работают как тумблер, а не как радиокнопка: повторный клик по активной категории
/// снимает фильтр и возвращает список к «Всё».
/// </summary>
public sealed class CategorySidebar : ScrollContainer
{
    public const float SidebarWidth = 132f;

    /// <summary>
    /// Выбрана категория, либо <c>null</c>, если фильтр снят.
    /// </summary>
    public event Action<string?>? OnCategorySelected;

    /// <summary>
    /// Текущая категория, либо <c>null</c>, если фильтр снят.
    /// </summary>
    public string? Selected { get; private set; }

    private readonly BoxContainer _list;
    private readonly Dictionary<string, Button> _buttons = new();

    /// <summary>
    /// Идентификатор псевдокатегории «Всё». Она же — визуальное состояние «фильтр снят».
    /// </summary>
    private string _allCategory = string.Empty;

    public CategorySidebar()
    {
        HScrollEnabled = false;
        // Ширина жёстко фиксирована: длинное название категории не должно
        // раздвигать сайдбар и перекраивать остальные колонки.
        MinWidth = SidebarWidth;
        MaxWidth = SidebarWidth;

        _list = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };

        AddChild(_list);
    }

    /// <summary>
    /// Перестроить список. <paramref name="categories"/> не должен содержать
    /// <paramref name="allCategory"/> — кнопка «Всё» всегда добавляется первой сама.
    /// </summary>
    public void Populate(string allCategory, IReadOnlyList<string> categories, string? selected)
    {
        _allCategory = allCategory;

        _list.RemoveAllChildren();
        _buttons.Clear();

        AddCategoryButton(allCategory);

        foreach (var category in categories)
        {
            AddCategoryButton(category);
        }

        SetSelected(selected);
    }

    /// <summary>
    /// Перенести подсветку. <c>null</c> подсвечивает «Всё».
    /// </summary>
    public void SetSelected(string? selected)
    {
        // Категории могло не стать (например, «Избранное» после удаления последнего рецепта) —
        // тогда фильтр считаем снятым, иначе он остался бы применён без подсветки.
        if (selected != null && !_buttons.ContainsKey(selected))
            selected = null;

        Selected = selected;

        foreach (var (category, button) in _buttons)
        {
            // Присваивание Pressed не поднимает OnToggled, так что рекурсии тут нет.
            button.Pressed = selected == null
                ? category == _allCategory
                : category == selected;
        }
    }

    private void AddCategoryButton(string category)
    {
        var button = new Button
        {
            Text = Loc.GetString(category),
            ToggleMode = true,
            HorizontalExpand = true,
            ClipText = true,
            // Group намеренно не задаётся: сгруппированную кнопку движок запрещает снимать
            // кликом, а нам нужен именно тумблер.
        };

        button.AddStyleClass(StyleClass.ButtonSquare);
        button.OnToggled += args => OnButtonToggled(category, args.Pressed);

        _list.AddChild(button);
        _buttons[category] = button;
    }

    private void OnButtonToggled(string category, bool pressed)
    {
        // «Всё» снять нельзя — это и есть состояние «фильтр снят».
        if (category == _allCategory)
        {
            SetSelected(null);
            OnCategorySelected?.Invoke(null);
            return;
        }

        var selection = pressed ? category : null;

        SetSelected(selection);
        OnCategorySelected?.Invoke(selection);
    }
}
