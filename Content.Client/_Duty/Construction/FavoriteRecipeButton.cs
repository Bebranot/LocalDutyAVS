using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared.Construction.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Construction;

/// <summary>
/// Одна иконка в <see cref="FavoritesPanel"/>. Названия не показывает — только спрайт результата,
/// имя уходит в тултип.
/// ЛКМ — выбрать рецепт, двойной ЛКМ — сразу строить/крафтить, ПКМ — убрать из избранного.
/// </summary>
public sealed class FavoriteRecipeButton : Control
{
    /// <summary>
    /// Сторона кнопки: спрайт 32px со <c>Scale 1.2</c> плюс отступы.
    /// </summary>
    public const float ButtonSize = 42f;

    private const float IconScale = 1.2f;

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public ConstructionPrototype Recipe { get; }

    private readonly PanelContainer _background;
    private readonly EntityPrototypeView _icon;

    private TimeSpan? _lastClickTime;
    private Vector2? _lastClickPosition;
    private bool _selected;
    private bool _hovered;

    public event Action<FavoriteRecipeButton>? OnRecipeSelected;
    public event Action<FavoriteRecipeButton>? OnRecipeActivated;
    public event Action<FavoriteRecipeButton>? OnRecipeUnfavorited;

    public FavoriteRecipeButton(ConstructionPrototype recipe, EntityPrototype target)
    {
        IoCManager.InjectDependencies(this);

        Recipe = recipe;

        MinSize = new Vector2(ButtonSize, ButtonSize);
        MouseFilter = MouseFilterMode.Stop;
        ToolTip = $"{recipe.Name}\n{Loc.GetString("construction-menu-favorite-remove-hint")}";

        _background = new PanelContainer();

        _icon = new EntityPrototypeView
        {
            Scale = new Vector2(IconScale),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
        };
        _icon.SetPrototype(target);

        AddChild(_background);
        AddChild(_icon);

        OnKeyBindDown += OnKeyDown;

        UpdateAppearance();
    }

    /// <summary>
    /// Подсветить кнопку как выбранный рецепт. Вызывается панелью, а не самой кнопкой:
    /// единственный источник истины по выделению — презентер.
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (_selected == selected)
            return;

        _selected = selected;
        UpdateAppearance();
    }

    private void OnKeyDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            OnRecipeUnfavorited?.Invoke(this);
            args.Handle();
            return;
        }

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        var now = _timing.RealTime;
        var position = args.PointerLocation.Position;

        // Двойной клик считаем по движковым CVar'ам, как это делает LineEdit, — чтобы
        // уважать системные настройки игрока, а не хардкодить свой порог.
        var isDoubleClick = _lastClickTime is { } lastTime
                            && _lastClickPosition is { } lastPosition
                            && now - lastTime <= TimeSpan.FromMilliseconds(_cfg.GetCVar(Robust.Shared.CVars.DoubleClickDelay))
                            && (lastPosition - position).Length() <= _cfg.GetCVar(Robust.Shared.CVars.DoubleClickRange);

        if (isDoubleClick)
        {
            // Сбрасываем, иначе третий клик подряд снова засчитается как двойной.
            _lastClickTime = null;
            _lastClickPosition = null;
            OnRecipeActivated?.Invoke(this);
        }
        else
        {
            _lastClickTime = now;
            _lastClickPosition = position;
            OnRecipeSelected?.Invoke(this);
        }

        args.Handle();
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();
        _hovered = true;
        UpdateAppearance();
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hovered = false;
        UpdateAppearance();
    }

    private void UpdateAppearance()
    {
        var color = _selected
            ? StyleNano.ButtonColorPressed
            : _hovered
                ? StyleNano.ButtonColorHovered
                : StyleNano.ButtonColorDefault;

        _background.PanelOverride = new StyleBoxFlat { BackgroundColor = color };
    }
}
