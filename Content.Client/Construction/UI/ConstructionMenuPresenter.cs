using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Client.Lobby;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared.CCVar;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Whitelist;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client.Construction.UI
{
    /// <summary>
    /// This class presents the Construction/Crafting UI to the client, linking the <see cref="ConstructionSystem" /> with the
    /// model. This is where the bulk of UI work is done, either calling functions in the model to change state, or collecting
    /// data out of the model to *present* to the screen though the UI framework.
    /// </summary>
    internal sealed class ConstructionMenuPresenter : IDisposable
    {
        [Dependency] private readonly EntityManager _entManager = default!;
        [Dependency] private readonly IEntitySystemManager _systemManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IPlacementManager _placementManager = default!;
        [Dependency] private readonly IUserInterfaceManager _uiManager = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IClientPreferencesManager _preferencesManager = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly ILogManager _logManager = default!;

        private readonly SpriteSystem _spriteSystem;
        private readonly ISawmill _sawmill;

        private readonly IConstructionMenuView _constructionView;
        private readonly EntityWhitelistSystem _whitelistSystem;

        private ConstructionSystem? _constructionSystem;
        private ConstructionPrototype? _selected;
        private List<ConstructionPrototype> _favoritedRecipes = [];
        private readonly Dictionary<string, ContainerButton> _recipeButtons = new();

        /// <summary>
        /// Выбранная категория, либо <c>null</c>, если фильтр снят (состояние «Всё»).
        /// </summary>
        private string? _selectedCategory;

        /// <summary>
        /// Защита от рекурсии: подсветка выбранного рецепта раскладывается сразу по трём местам
        /// (список, сетка, панель избранного), и каждое из них умеет само сообщать о выборе.
        /// </summary>
        private bool _syncingSelection;

        private const string FavoriteCatName = "construction-category-favorites";
        private const string ForAllCategoryName = "construction-category-all";

        private bool CraftingAvailable
        {
            get => _uiManager.GetActiveUIWidget<GameTopMenuBar>().CraftingButton.Visible;
            set
            {
                _uiManager.GetActiveUIWidget<GameTopMenuBar>().CraftingButton.Visible = value;
                if (!value)
                    _constructionView.Close();
            }
        }

        /// <summary>
        /// Does the window have focus? If the window is closed, this will always return false.
        /// </summary>
        private bool IsAtFront => _constructionView.IsOpen && _constructionView.IsAtFront();

        private bool WindowOpen
        {
            get => _constructionView.IsOpen;
            set
            {
                if (value && CraftingAvailable)
                {
                    if (_constructionView.IsOpen)
                        _constructionView.MoveToFront();
                    else
                        _constructionView.OpenCentered();

                    if (_selected != null)
                        PopulateInfo(_selected);
                }
                else
                    _constructionView.Close();
            }
        }

        /// <summary>
        /// Constructs a new instance of <see cref="ConstructionMenuPresenter" />.
        /// </summary>
        public ConstructionMenuPresenter()
        {
            // This is a lot easier than a factory
            IoCManager.InjectDependencies(this);
            _constructionView = new ConstructionMenu();
            _whitelistSystem = _entManager.System<EntityWhitelistSystem>();
            _spriteSystem = _entManager.System<SpriteSystem>();
            _sawmill = _logManager.GetSawmill("construction.ui");

            // This is required so that if we load after the system is initialized, we can bind to it immediately
            if (_systemManager.TryGetEntitySystem<ConstructionSystem>(out var constructionSystem))
                SystemBindingChanged(constructionSystem);

            _systemManager.SystemLoaded += OnSystemLoaded;
            _systemManager.SystemUnloaded += OnSystemUnloaded;

            _placementManager.PlacementChanged += OnPlacementChanged;

            _constructionView.OnClose +=
                () => _uiManager.GetActiveUIWidget<GameTopMenuBar>().CraftingButton.Pressed = false;
            _constructionView.ClearAllGhosts += (_, _) => _constructionSystem?.ClearAllGhosts();
            _constructionView.PopulateRecipes += OnViewPopulateRecipes;
            _constructionView.RecipeSelected += (_, item) => SelectRecipe(item?.Prototype);
            _constructionView.BuildButtonToggled += (_, b) => BuildButtonToggled(b);
            _constructionView.EraseButtonToggled += (_, b) =>
            {
                if (_constructionSystem is null)
                    return;
                if (b)
                    _placementManager.Clear();
                _placementManager.ToggleEraserHijacked(new ConstructionPlacementHijack(_constructionSystem, null));
                _constructionView.EraseButtonPressed = b;
            };

            _constructionView.RecipeFavorited += (_, _) => OnViewFavoriteRecipe();

            // _Duty: боковая панель избранного.
            _constructionView.FavoritesSidePanel.OnRecipeSelected += SelectRecipe;
            _constructionView.FavoritesSidePanel.OnRecipeActivated += OnFavoriteActivated;
            _constructionView.FavoritesSidePanel.OnRecipeUnfavorited += ToggleFavorite;
            _constructionView.FavoritesPanelToggled += (_, _) => OnFavoritesPanelToggled();

            SetFavorites(_preferencesManager.Preferences?.ConstructionFavorites ?? []);
        }

        public void OnHudCraftingButtonToggled(BaseButton.ButtonToggledEventArgs args)
        {
            WindowOpen = args.Pressed;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _constructionView.Dispose();

            SystemBindingChanged(null);
            _systemManager.SystemLoaded -= OnSystemLoaded;
            _systemManager.SystemUnloaded -= OnSystemUnloaded;

            _placementManager.PlacementChanged -= OnPlacementChanged;
        }

        private void OnPlacementChanged(object? sender, EventArgs e)
        {
            _constructionView.ResetPlacement();
        }

        #region Selection

        /// <summary>
        /// Единственная точка входа для выбора рецепта. Сюда сходятся список, сетка и панель
        /// избранного, чтобы выделение не разъезжалось между ними.
        /// </summary>
        private void SelectRecipe(ConstructionPrototype? recipe)
        {
            if (_syncingSelection)
                return;

            _selected = recipe;

            if (recipe != null && _placementManager is { IsActive: true, Eraser: false })
                UpdateGhostPlacement();

            PopulateInfo(recipe);
            SyncSelectionHighlight();
        }

        /// <summary>
        /// Разложить подсветку выбранного рецепта по всем местам, где он сейчас может быть виден,
        /// и снять её со всех остальных.
        /// </summary>
        private void SyncSelectionHighlight()
        {
            _syncingSelection = true;

            try
            {
                _constructionView.FavoritesSidePanel.SetSelected(_selected);
                UpdateGridSelection();
                UpdateListSelection();
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        private void UpdateGridSelection()
        {
            foreach (var (id, button) in _recipeButtons)
            {
                var selected = _selected != null && id == _selected.ID;

                // Присваивание Pressed не поднимает OnToggled, рекурсии тут нет.
                button.Pressed = selected;
                SelectGridButton(button, selected);
            }
        }

        private void UpdateListSelection()
        {
            if (_selected == null || _constructionView.GridViewButtonPressed)
                return;

            if (!TryGetTarget(_selected, out var target))
                return;

            // Если рецепт не проходит текущий фильтр, Select просто ничего не сделает —
            // подсветится только иконка в панели избранного.
            _constructionView.Recipes.Select(new ConstructionMenu.ConstructionMenuListData(_selected, target));
        }

        #endregion

        #region Recipe filtering

        private void OnViewPopulateRecipes(object? sender, (string search, string? category) args)
        {
            if (_constructionSystem is null)
                return;

            var (search, category) = args;
            _selectedCategory = category;

            var recipes = GetRecipes(search, category);

            var recipesList = _constructionView.Recipes;
            var recipesGrid = _constructionView.RecipesGrid;

            recipesGrid.RemoveAllChildren();
            // Без этой очистки словарь копил кнопки, уже удалённые из дерева.
            _recipeButtons.Clear();

            _constructionView.RecipesGridScrollContainer.Visible = _constructionView.GridViewButtonPressed;
            _constructionView.Recipes.Visible = !_constructionView.GridViewButtonPressed;

            if (_constructionView.GridViewButtonPressed)
            {
                recipesList.PopulateList([]);
                PopulateGrid(recipesGrid, recipes);
            }
            else
            {
                recipesList.PopulateList(recipes);
            }

            UpdateSearchHint(search, category, recipes.Count);
            SyncSelectionHighlight();
        }

        /// <summary>
        /// Если в выбранной категории по запросу ничего нет, но в остальных есть — предлагаем снять
        /// фильтр, вместо того чтобы оставлять игрока с пустым списком.
        /// </summary>
        private void UpdateSearchHint(string search, string? category, int shownCount)
        {
            if (shownCount > 0 || category == null || string.IsNullOrWhiteSpace(search))
            {
                _constructionView.HideSearchHint();
                return;
            }

            var elsewhere = GetRecipes(search, null).Count;

            if (elsewhere > 0)
                _constructionView.ShowSearchHint(category, elsewhere);
            else
                _constructionView.HideSearchHint();
        }

        /// <summary>
        /// <paramref name="category"/> равный <c>null</c> означает снятый фильтр — ищем по всем
        /// категориям сразу.
        /// </summary>
        private List<ConstructionMenu.ConstructionMenuListData> GetRecipes(string search, string? category)
        {
            var recipes = new List<ConstructionMenu.ConstructionMenuListData>();
            var trimmed = search.Trim();

            foreach (var recipe in _prototypeManager.EnumeratePrototypes<ConstructionPrototype>())
            {
                if (!IsRecipeAvailable(recipe))
                    continue;

                if (trimmed.Length > 0
                    && (recipe.Name is not { } name
                        || !name.Contains(trimmed, StringComparison.InvariantCultureIgnoreCase)))
                {
                    continue;
                }

                if (category != null && !MatchesCategory(recipe, category))
                    continue;

                if (!TryGetTarget(recipe, out var target))
                    continue;

                recipes.Add(new(recipe, target));
            }

            recipes.Sort(
                (a, b) => string.Compare(a.Prototype.Name, b.Prototype.Name, StringComparison.InvariantCulture));

            return recipes;
        }

        /// <summary>
        /// Доступен ли рецепт игроку прямо сейчас. Один предикат на список, сетку, сайдбар и панель
        /// избранного — иначе они разъезжаются между собой.
        /// </summary>
        private bool IsRecipeAvailable(ConstructionPrototype recipe)
        {
            if (recipe.Hide)
                return false;

            if (_playerManager.LocalSession == null || _playerManager.LocalEntity is not { } player)
                return false;

            return !_whitelistSystem.IsWhitelistFail(recipe.EntityWhitelist, player);
        }

        private bool MatchesCategory(ConstructionPrototype recipe, string category)
        {
            if (category == FavoriteCatName)
                return _favoritedRecipes.Contains(recipe);

            return recipe.Category == category;
        }

        private bool TryGetTarget(ConstructionPrototype recipe, [NotNullWhen(true)] out EntityPrototype? target)
        {
            target = null;

            if (_constructionSystem is null)
                return false;

            if (!_constructionSystem.TryGetRecipePrototype(recipe.ID, out var targetProtoId))
            {
                _sawmill.Error("Cannot find the target prototype in the recipe cache with the id \"{0}\" of {1}.",
                    recipe.ID,
                    nameof(ConstructionPrototype));
                return false;
            }

            return _prototypeManager.TryIndex(targetProtoId, out target);
        }

        #endregion

        #region Grid view

        private void PopulateGrid(GridContainer recipesGrid,
            IEnumerable<ConstructionMenu.ConstructionMenuListData> actualRecipes)
        {
            foreach (var recipe in actualRecipes)
            {
                var protoView = new EntityPrototypeView()
                {
                    Scale = new Vector2(1.2f),
                };
                protoView.SetPrototype(recipe.TargetPrototype);

                var itemButton = new ContainerButton()
                {
                    VerticalAlignment = Control.VAlignment.Center,
                    Name = recipe.Prototype.Name,
                    ToolTip = recipe.Prototype.Name,
                    ToggleMode = true,
                    Children = { protoView },
                };

                var itemButtonPanelContainer = new PanelContainer
                {
                    PanelOverride = new StyleBoxFlat { BackgroundColor = StyleNano.ButtonColorDefault },
                    Children = { itemButton },
                };

                itemButton.OnToggled += args => SelectRecipe(args.Pressed ? recipe.Prototype : null);

                recipesGrid.AddChild(itemButtonPanelContainer);
                _recipeButtons[recipe.Prototype.ID] = itemButton;
            }
        }

        private void SelectGridButton(BaseButton button, bool select)
        {
            if (button.Parent is not PanelContainer buttonPanel)
                return;

            button.Children.Single().Modulate = select ? Color.Green : Color.White;
            var buttonColor = select ? StyleNano.ButtonColorDefault : Color.Transparent;
            buttonPanel.PanelOverride = new StyleBoxFlat { BackgroundColor = buttonColor };
        }

        #endregion

        #region Categories

        private void PopulateCategories()
        {
            var uniqueCategories = new HashSet<string>();

            foreach (var prototype in _prototypeManager.EnumeratePrototypes<ConstructionPrototype>())
            {
                // Категория попадает в сайдбар, только если в ней есть хоть один доступный рецепт.
                if (!IsRecipeAvailable(prototype))
                    continue;

                if (!string.IsNullOrEmpty(prototype.Category))
                    uniqueCategories.Add(prototype.Category);
            }

            var categories = new List<string>();

            if (HasAvailableFavorites())
                categories.Add(FavoriteCatName);

            categories.AddRange(uniqueCategories.OrderBy(Loc.GetString));

            _constructionView.SetCategories(ForAllCategoryName, categories, _selectedCategory);

            // Сайдбар сбрасывает выбор, если категории не стало (например, «Избранное» опустело).
            _selectedCategory = _constructionView.SelectedCategory;
        }

        #endregion

        #region Favorites

        private void OnViewFavoriteRecipe()
        {
            if (_selected is null)
                return;

            ToggleFavorite(_selected);
        }

        private void ToggleFavorite(ConstructionPrototype recipe)
        {
            if (!_favoritedRecipes.Remove(recipe))
                _favoritedRecipes.Add(recipe);

            var newFavorites = new List<ProtoId<ConstructionPrototype>>(_favoritedRecipes.Count);
            foreach (var favorite in _favoritedRecipes)
                newFavorites.Add(favorite.ID);

            _preferencesManager.UpdateConstructionFavorites(newFavorites);

            RefreshAll();
        }

        private void OnFavoriteActivated(ConstructionPrototype recipe)
        {
            SelectRecipe(recipe);
            BuildButtonToggled(true);
        }

        private void OnFavoritesPanelToggled()
        {
            var visible = _cfg.GetCVar(DutyCCVars.ConstructionFavoritesPanelVisible);
            _cfg.SetCVar(DutyCCVars.ConstructionFavoritesPanelVisible, !visible);

            RefreshFavoritesPanel();
        }

        /// <summary>
        /// Избранное хранится по ID рецепта и переживает смену персонажа, поэтому часть записей
        /// может быть недоступна текущей роли. Такие в панель не попадают, но из настроек не
        /// удаляются — вернёшься нужной ролью, и они снова на месте.
        /// </summary>
        private List<(ConstructionPrototype Recipe, EntityPrototype Target)> GetAvailableFavorites()
        {
            var favorites = new List<(ConstructionPrototype, EntityPrototype)>();

            foreach (var recipe in _favoritedRecipes)
            {
                if (!IsRecipeAvailable(recipe))
                    continue;

                if (!TryGetTarget(recipe, out var target))
                    continue;

                favorites.Add((recipe, target));
            }

            return favorites;
        }

        private bool HasAvailableFavorites()
        {
            foreach (var recipe in _favoritedRecipes)
            {
                if (IsRecipeAvailable(recipe) && TryGetTarget(recipe, out _))
                    return true;
            }

            return false;
        }

        private void RefreshFavoritesPanel()
        {
            var favorites = GetAvailableFavorites();

            _constructionView.FavoritesSidePanel.Populate(favorites, _selected);
            _constructionView.SetFavoritesPanel(
                favorites.Count > 0,
                favorites.Count,
                _cfg.GetCVar(DutyCCVars.ConstructionFavoritesPanelVisible));
        }

        public void SetFavorites(IReadOnlyList<ProtoId<ConstructionPrototype>> favorites)
        {
            _favoritedRecipes.Clear();

            foreach (var id in favorites)
            {
                if (_prototypeManager.TryIndex(id, out ConstructionPrototype? recipe))
                    _favoritedRecipes.Add(recipe);
            }

            RefreshAll();
        }

        #endregion

        /// <summary>
        /// Полное обновление окна: сайдбар, панель избранного, список и инфо-блок.
        /// </summary>
        private void RefreshAll()
        {
            PopulateCategories();
            RefreshFavoritesPanel();
            PopulateInfo(_selected);
            OnViewPopulateRecipes(_constructionView, (_constructionView.SearchText, _selectedCategory));
        }

        private void PopulateInfo(ConstructionPrototype? prototype)
        {
            if (_constructionSystem is null)
                return;

            _constructionView.ClearRecipeInfo();

            if (prototype is null)
                return;

            if (!TryGetTarget(prototype, out var proto))
                return;

            _constructionView.SetRecipeInfo(
                prototype.Name!,
                prototype.Description!,
                proto,
                prototype.Type != ConstructionType.Item,
                !_favoritedRecipes.Contains(prototype));

            var stepList = _constructionView.RecipeStepList;
            GenerateStepList(prototype, stepList);
        }

        private void GenerateStepList(ConstructionPrototype prototype, ItemList stepList)
        {
            if (_constructionSystem?.GetGuide(prototype) is not { } guide)
                return;

            foreach (var entry in guide.Entries)
            {
                var text = entry.Arguments != null
                    ? Loc.GetString(entry.Localization, entry.Arguments)
                    : Loc.GetString(entry.Localization);

                if (entry.EntryNumber is { } number)
                {
                    text = Loc.GetString("construction-presenter-step-wrapper",
                        ("step-number", number),
                        ("text", text));
                }

                // The padding needs to be applied regardless of text length... (See PadLeft documentation)
                text = text.PadLeft(text.Length + entry.Padding);

                var icon = entry.Icon != null ? _spriteSystem.Frame0(entry.Icon) : Texture.Transparent;
                stepList.AddItem(text, icon, false);
            }
        }

        private void BuildButtonToggled(bool pressed)
        {
            if (pressed)
            {
                if (_selected == null)
                    return;

                // not bound to a construction system
                if (_constructionSystem is null)
                {
                    _constructionView.BuildButtonPressed = false;
                    return;
                }

                if (_selected.Type == ConstructionType.Item)
                {
                    _constructionSystem.TryStartItemConstruction(_selected.ID);
                    _constructionView.BuildButtonPressed = false;
                    return;
                }

                _placementManager.BeginPlacing(new PlacementInformation
                    {
                        IsTile = false,
                        PlacementOption = _selected.PlacementMode
                    },
                    new ConstructionPlacementHijack(_constructionSystem, _selected));

                UpdateGhostPlacement();
            }
            else
                _placementManager.Clear();

            _constructionView.BuildButtonPressed = pressed;
        }

        private void UpdateGhostPlacement()
        {
            if (_selected == null)
                return;

            if (_selected.Type != ConstructionType.Structure)
            {
                _placementManager.Clear();
                return;
            }

            var constructSystem = _systemManager.GetEntitySystem<ConstructionSystem>();

            _placementManager.BeginPlacing(new PlacementInformation()
                {
                    IsTile = false,
                    PlacementOption = _selected.PlacementMode,
                },
                new ConstructionPlacementHijack(constructSystem, _selected));

            _constructionView.BuildButtonPressed = true;
        }

        private void OnSystemLoaded(object? sender, SystemChangedArgs args)
        {
            if (args.System is ConstructionSystem system)
                SystemBindingChanged(system);
        }

        private void OnSystemUnloaded(object? sender, SystemChangedArgs args)
        {
            if (args.System is ConstructionSystem)
                SystemBindingChanged(null);
        }

        private void SystemBindingChanged(ConstructionSystem? newSystem)
        {
            if (newSystem is null)
            {
                if (_constructionSystem is null)
                    return;

                UnbindFromSystem();
            }
            else
            {
                if (_constructionSystem is null)
                {
                    BindToSystem(newSystem);
                    return;
                }

                UnbindFromSystem();
                BindToSystem(newSystem);
            }
        }

        private void BindToSystem(ConstructionSystem system)
        {
            _constructionSystem = system;

            RefreshAll();

            system.ToggleCraftingWindow += SystemOnToggleMenu;
            system.FlipConstructionPrototype += SystemFlipConstructionPrototype;
            system.CraftingAvailabilityChanged += SystemCraftingAvailabilityChanged;
            system.ConstructionGuideAvailable += SystemGuideAvailable;
            if (_uiManager.GetActiveUIWidgetOrNull<GameTopMenuBar>() != null)
            {
                CraftingAvailable = system.CraftingEnabled;
            }
        }

        private void UnbindFromSystem()
        {
            var system = _constructionSystem;

            if (system is null)
                throw new InvalidOperationException();

            system.ToggleCraftingWindow -= SystemOnToggleMenu;
            system.FlipConstructionPrototype -= SystemFlipConstructionPrototype;
            system.CraftingAvailabilityChanged -= SystemCraftingAvailabilityChanged;
            system.ConstructionGuideAvailable -= SystemGuideAvailable;
            _constructionSystem = null;
        }

        private void SystemCraftingAvailabilityChanged(object? sender, CraftingAvailabilityChangedArgs e)
        {
            if (_uiManager.ActiveScreen == null)
                return;
            CraftingAvailable = e.Available;
        }

        private void SystemOnToggleMenu(object? sender, EventArgs eventArgs)
        {
            if (!CraftingAvailable)
                return;

            if (WindowOpen)
            {
                if (IsAtFront)
                {
                    WindowOpen = false;
                    _uiManager.GetActiveUIWidget<GameTopMenuBar>()
                        .CraftingButton.SetClickPressed(false); // This does not call CraftingButtonToggled
                }
                else
                    _constructionView.MoveToFront();
            }
            else
            {
                WindowOpen = true;
                _uiManager.GetActiveUIWidget<GameTopMenuBar>()
                    .CraftingButton.SetClickPressed(true); // This does not call CraftingButtonToggled
            }
        }

        private void SystemFlipConstructionPrototype(object? sender, EventArgs eventArgs)
        {
            if (!_placementManager.IsActive || _placementManager.Eraser)
            {
                return;
            }

            if (_selected == null || _selected.Mirror == null)
            {
                return;
            }

            _selected = _prototypeManager.Index<ConstructionPrototype>(_selected.Mirror);
            UpdateGhostPlacement();
        }

        private void SystemGuideAvailable(object? sender, string e)
        {
            if (!CraftingAvailable)
                return;

            if (!WindowOpen)
                return;

            if (_selected == null)
                return;

            PopulateInfo(_selected);
        }
    }
}
