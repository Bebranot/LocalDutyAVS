using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._Duty.Administration;
using Content.Client.Eui;
using Content.Client.Stylesheets;
using Content.Shared._Duty.Administration;
using Content.Shared.Administration;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Content.Shared.Administration.PermissionsEuiMsg;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client.Administration.UI
{
    // _Duty: панель переписана под дерево прав (PermissionTreeEditor): права — строки (старый флаг или id узла).
    [UsedImplicitly]
    public sealed class PermissionsEui : BaseEui
    {
        private const int NoRank = -1;

        [Dependency] private readonly IPrototypeManager _proto = default!;

        private readonly Menu _menu;
        private readonly List<BaseWindow> _subWindows = new();

        private Dictionary<int, PermissionsEuiState.AdminRankData> _ranks = new();
        private HashSet<string> _editorGrants = new();
        private AdminPermissionTree? _tree;
        private PermissionsEuiState? _lastState;

        public PermissionsEui()
        {
            IoCManager.InjectDependencies(this);

            _menu = new Menu();
            _menu.AddAdminButton.OnPressed += _ => OpenEditWindow(null);
            _menu.AddAdminRankButton.OnPressed += _ => OpenRankEditWindow(null);
            _menu.AdminSearch.OnTextChanged += _ => Rebuild();
            _menu.RankSearch.OnTextChanged += _ => Rebuild();
            _menu.OnClose += CloseEverything;
        }

        private AdminPermissionTree Tree => _tree ??= new AdminPermissionTree(_proto);

        public override void Closed()
        {
            base.Closed();

            SendMessage(new CloseEuiMessage());
            CloseEverything();
        }

        private void CloseEverything()
        {
            foreach (var subWindow in _subWindows.ToArray())
            {
                subWindow.Close();
            }

            _menu.Close();
        }

        public override void Opened()
        {
            _menu.OpenCentered();
        }

        // ---- окна правки ---------------------------------------------------------------------------------------

        private void OpenEditWindow(PermissionsEuiState.AdminData? data)
        {
            var window = new EditAdminWindow(Tree, _editorGrants, _ranks, data);
            window.SaveButton.OnPressed += _ => SaveAdminPressed(window);
            window.OpenCentered();
            window.OnClose += () => _subWindows.Remove(window);
            if (data != null)
            {
                window.RemoveButton!.OnPressed += _ =>
                {
                    SendMessage(new RemoveAdmin { UserId = window.SourceData!.Value.UserId });
                    window.Close();
                };
            }

            _subWindows.Add(window);
        }

        private void OpenRankEditWindow(KeyValuePair<int, PermissionsEuiState.AdminRankData>? rank)
        {
            var window = new EditAdminRankWindow(Tree, _editorGrants, rank);
            window.SaveButton.OnPressed += _ => SaveAdminRankPressed(window);
            window.OpenCentered();
            window.OnClose += () => _subWindows.Remove(window);
            if (rank != null)
            {
                window.RemoveButton!.OnPressed += _ =>
                {
                    SendMessage(new RemoveAdminRank { Id = window.SourceId!.Value });
                    window.Close();
                };
            }

            _subWindows.Add(window);
        }

        private void SaveAdminPressed(EditAdminWindow popup)
        {
            var pos = popup.Editor.CollectPositive();
            var neg = popup.Editor.CollectNegative();

            int? rank = popup.RankButton.SelectedId;
            if (rank == NoRank)
            {
                rank = null;
            }

            var title = string.IsNullOrWhiteSpace(popup.TitleEdit.Text) ? null : popup.TitleEdit.Text;
            var suspended = popup.SuspendedCheckbox.Pressed;

            if (popup.SourceData is { } src)
            {
                SendMessage(new UpdateAdmin
                {
                    UserId = src.UserId,
                    Title = title,
                    Pos = pos,
                    Neg = neg,
                    RankId = rank,
                    Suspended = suspended,
                });
            }
            else
            {
                DebugTools.AssertNotNull(popup.NameEdit);

                SendMessage(new AddAdmin
                {
                    UserNameOrId = popup.NameEdit!.Text,
                    Title = title,
                    Pos = pos,
                    Neg = neg,
                    RankId = rank,
                    Suspended = suspended,
                });
            }

            popup.Close();
        }

        private void SaveAdminRankPressed(EditAdminRankWindow popup)
        {
            var grants = popup.Editor.CollectPositive();
            var name = popup.NameEdit.Text;

            if (popup.SourceId is { } src)
            {
                SendMessage(new UpdateAdminRank
                {
                    Id = src,
                    Grants = grants,
                    Name = name,
                });
            }
            else
            {
                SendMessage(new AddAdminRank
                {
                    Grants = grants,
                    Name = name
                });
            }

            popup.Close();
        }

        // ---- списки --------------------------------------------------------------------------------------------

        public override void HandleState(EuiStateBase state)
        {
            var s = (PermissionsEuiState) state;

            if (s.IsLoading)
            {
                return;
            }

            _lastState = s;
            _ranks = s.AdminRanks;
            _editorGrants = new HashSet<string>(s.EditorGrants);
            Rebuild();
        }

        /// <summary>Держит ли редактор все эти права (неизвестные имена не мешают).</summary>
        private bool Holds(IEnumerable<string> grants)
        {
            foreach (var g in grants)
            {
                var known = AdminFlagsHelper.TryNameToFlag(g, out _) || Tree.Exists(g);
                if (known && !_editorGrants.Contains(g))
                    return false;
            }

            return true;
        }

        private string GrantName(string grant)
        {
            return Tree.TryGet(grant, out var proto) ? Loc.GetString(proto.NameLoc) : grant;
        }

        /// <summary>Короткая сводка прав для строки списка, полный перечень идёт в подсказку.</summary>
        private string Summarize(IEnumerable<string> pos, IEnumerable<string> neg, out string full)
        {
            var names = pos.Select(GrantName).Concat(neg.Select(n => "−" + GrantName(n))).ToList();
            full = names.Count == 0 ? Loc.GetString("duty-perm-ui-none") : string.Join(", ", names);
            if (names.Count == 0)
                return Loc.GetString("duty-perm-ui-none");

            const int shown = 3;
            var text = string.Join(", ", names.Take(shown));
            if (names.Count > shown)
                text += " " + Loc.GetString("duty-perm-ui-more", ("count", names.Count - shown));
            return text;
        }

        private void Rebuild()
        {
            if (_lastState is not { } s)
                return;

            var adminQuery = _menu.AdminSearch.Text.Trim().ToLowerInvariant();
            var rankQuery = _menu.RankSearch.Text.Trim().ToLowerInvariant();

            _menu.AdminsList.RemoveAllChildren();
            foreach (var admin in s.Admins.OrderBy(d => d.UserName))
            {
                var al = _menu.AdminsList;
                var name = admin.UserName ?? admin.UserId.ToString();

                var rankName = admin.RankId is { } rid && s.AdminRanks.TryGetValue(rid, out var rd)
                    ? rd.Name
                    : Loc.GetString("permissions-eui-edit-no-rank-text").ToLowerInvariant();
                var rankGrants = admin.RankId is { } rid2 && s.AdminRanks.TryGetValue(rid2, out var rd2)
                    ? rd2.Grants
                    : Array.Empty<string>();

                var summary = Summarize(admin.Pos, admin.Neg, out var full);
                if (adminQuery.Length > 0)
                {
                    var hay = (name + " " + (admin.Title ?? string.Empty) + " " + rankName + " " + full).ToLowerInvariant();
                    if (!hay.Contains(adminQuery))
                        continue;
                }

                al.AddChild(new Label { Text = name });

                var titleControl = new Label { Text = admin.Title ?? Loc.GetString("permissions-eui-edit-admin-title-control-text").ToLowerInvariant() };
                if (admin.Title == null) // none
                {
                    titleControl.StyleClasses.Add(StyleClass.Italic);
                }

                al.AddChild(titleControl);

                var rankControl = new Label { Text = rankName };
                if (admin.RankId == null)
                {
                    rankControl.StyleClasses.Add(StyleClass.Italic);
                }

                al.AddChild(rankControl);

                var flagsLabel = new Label
                {
                    Text = summary,
                    HorizontalExpand = true,
                    ClipText = true,
                    ToolTip = full,
                };
                al.AddChild(flagsLabel);

                var editButton = new Button { Text = Loc.GetString("permissions-eui-edit-title-button") };
                editButton.OnPressed += _ => OpenEditWindow(admin);
                al.AddChild(editButton);

                if (!Holds(admin.Pos.Concat(rankGrants)))
                {
                    editButton.Disabled = true;
                    editButton.ToolTip = Loc.GetString("permissions-eui-do-not-have-required-flags-to-edit-admin-tooltip");
                }
            }

            _menu.AdminRanksList.RemoveAllChildren();
            foreach (var kv in s.AdminRanks.OrderBy(k => k.Value.Name))
            {
                var rank = kv.Value;
                var summary = Summarize(rank.Grants, Array.Empty<string>(), out var full);
                if (rankQuery.Length > 0 && !(rank.Name + " " + full).ToLowerInvariant().Contains(rankQuery))
                    continue;

                _menu.AdminRanksList.AddChild(new Label { Text = rank.Name });
                _menu.AdminRanksList.AddChild(new Label
                {
                    Text = summary,
                    HorizontalExpand = true,
                    ClipText = true,
                    ToolTip = full,
                });
                var editButton = new Button { Text = Loc.GetString("permissions-eui-edit-admin-rank-button") };
                editButton.OnPressed += _ => OpenRankEditWindow(kv);
                _menu.AdminRanksList.AddChild(editButton);

                if (!Holds(rank.Grants))
                {
                    editButton.Disabled = true;
                    editButton.ToolTip = Loc.GetString("permissions-eui-do-not-have-required-flags-to-edit-rank-tooltip");
                }
            }
        }

        private sealed class Menu : DefaultWindow
        {
            public readonly GridContainer AdminsList;
            public readonly GridContainer AdminRanksList;
            public readonly Button AddAdminButton;
            public readonly Button AddAdminRankButton;
            public readonly LineEdit AdminSearch;
            public readonly LineEdit RankSearch;

            public Menu()
            {
                Title = Loc.GetString("permissions-eui-menu-title");
                MinSize = new Vector2(760, 460);
                SetSize = new Vector2(860, 520); // фиксированный размер: содержимое не должно растягивать окно

                var tab = new TabContainer();

                AddAdminButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-menu-add-admin-button"),
                    HorizontalAlignment = HAlignment.Right
                };

                AddAdminRankButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-menu-add-admin-rank-button"),
                    HorizontalAlignment = HAlignment.Right
                };

                AdminSearch = new LineEdit { PlaceHolder = Loc.GetString("duty-perm-ui-search-admins") };
                RankSearch = new LineEdit { PlaceHolder = Loc.GetString("duty-perm-ui-search-ranks") };

                AdminsList = new GridContainer { Columns = 5, HSeparationOverride = 12, HorizontalExpand = true };
                var adminVBox = new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical,
                    SeparationOverride = 4,
                    Children =
                    {
                        AdminSearch,
                        new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { AdminsList } },
                        AddAdminButton,
                    },
                };
                TabContainer.SetTabTitle(adminVBox, Loc.GetString("permissions-eui-menu-admins-tab-title"));

                AdminRanksList = new GridContainer { Columns = 3, HSeparationOverride = 12, HorizontalExpand = true };
                var rankVBox = new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical,
                    SeparationOverride = 4,
                    Children =
                    {
                        RankSearch,
                        new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { AdminRanksList } },
                        AddAdminRankButton,
                    },
                };
                TabContainer.SetTabTitle(rankVBox, Loc.GetString("permissions-eui-menu-admin-ranks-tab-title"));

                tab.AddChild(adminVBox);
                tab.AddChild(rankVBox);

                ContentsContainer.AddChild(tab);
            }
        }

        private sealed class EditAdminWindow : DefaultWindow
        {
            public readonly PermissionsEuiState.AdminData? SourceData;

            public readonly LineEdit? NameEdit;
            public readonly LineEdit TitleEdit;
            public readonly CheckBox SuspendedCheckbox;
            public readonly OptionButton RankButton;
            public readonly Button SaveButton;
            public readonly Button? RemoveButton;
            public readonly PermissionTreeEditor Editor;

            public EditAdminWindow(
                AdminPermissionTree tree,
                HashSet<string> editorGrants,
                Dictionary<int, PermissionsEuiState.AdminRankData> ranks,
                PermissionsEuiState.AdminData? data)
            {
                MinSize = new Vector2(820, 560);
                SetSize = new Vector2(940, 640); // фиксированный размер: описания флагов не должны растягивать окно
                SourceData = data;

                Control nameControl;

                if (data is { } dat)
                {
                    Title = Loc.GetString("permissions-eui-edit-admin-window-edit-admin-label",
                        ("admin", dat.UserName ?? dat.UserId.ToString()));
                    nameControl = new Label { Text = dat.UserName ?? dat.UserId.ToString() };
                }
                else
                {
                    Title = Loc.GetString("permissions-eui-menu-add-admin-button");
                    nameControl = NameEdit = new LineEdit { PlaceHolder = Loc.GetString("permissions-eui-edit-admin-window-name-edit-placeholder") };
                }

                TitleEdit = new LineEdit { PlaceHolder = Loc.GetString("permissions-eui-edit-admin-window-title-edit-placeholder") };

                SaveButton = new Button { Text = Loc.GetString("permissions-eui-edit-admin-window-save-button"), HorizontalAlignment = HAlignment.Right };

                SuspendedCheckbox = new CheckBox
                {
                    Text = Loc.GetString("permissions-eui-edit-admin-window-suspended"),
                    Pressed = data?.Suspended ?? false,
                };

                RankButton = new OptionButton();
                RankButton.AddItem(Loc.GetString("permissions-eui-edit-admin-window-no-rank-button"), NoRank);
                foreach (var (id, rank) in ranks.OrderBy(r => r.Value.Name))
                {
                    RankButton.AddItem(rank.Name, id);
                }

                RankButton.SelectId(data?.RankId ?? NoRank);
                RankButton.OnItemSelected += args =>
                {
                    RankButton.SelectId(args.Id);
                    UpdateInherited(ranks);
                };

                Editor = new PermissionTreeEditor(tree, editorGrants, PermissionTreeEditor.EditorMode.Admin);

                var left = new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical,
                    SeparationOverride = 6,
                    MinSize = new Vector2(230, 0),
                    Children =
                    {
                        nameControl,
                        TitleEdit,
                        RankButton,
                        SuspendedCheckbox,
                        new Control { VerticalExpand = true },
                    },
                };

                if (data != null)
                {
                    RemoveButton = new Button { Text = Loc.GetString("permissions-eui-edit-admin-window-remove-flag-button") };
                    left.AddChild(RemoveButton);
                }

                left.AddChild(SaveButton);

                ContentsContainer.AddChild(new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                    SeparationOverride = 8,
                    VerticalExpand = true,
                    Children = { left, Editor },
                });

                TitleEdit.Text = data?.Title ?? string.Empty;
                Editor.SetData(data?.Pos ?? Array.Empty<string>(), data?.Neg ?? Array.Empty<string>(), InheritedFor(ranks));
            }

            private IEnumerable<string> InheritedFor(Dictionary<int, PermissionsEuiState.AdminRankData> ranks)
            {
                var id = RankButton.SelectedId;
                return id != NoRank && ranks.TryGetValue(id, out var rank) ? rank.Grants : Array.Empty<string>();
            }

            private void UpdateInherited(Dictionary<int, PermissionsEuiState.AdminRankData> ranks)
            {
                Editor.SetInherited(InheritedFor(ranks));
            }
        }

        private sealed class EditAdminRankWindow : DefaultWindow
        {
            public readonly int? SourceId;
            public readonly LineEdit NameEdit;
            public readonly Button SaveButton;
            public readonly Button? RemoveButton;
            public readonly PermissionTreeEditor Editor;

            public EditAdminRankWindow(
                AdminPermissionTree tree,
                HashSet<string> editorGrants,
                KeyValuePair<int, PermissionsEuiState.AdminRankData>? data)
            {
                MinSize = new Vector2(760, 540);
                SetSize = new Vector2(900, 620); // фиксированный размер
                Title = Loc.GetString("permissions-eui-edit-admin-rank-window-title");
                SourceId = data?.Key;

                NameEdit = new LineEdit
                {
                    PlaceHolder = Loc.GetString("permissions-eui-edit-admin-rank-window-name-edit-placeholder"),
                    Text = data?.Value.Name ?? string.Empty,
                };

                SaveButton = new Button
                {
                    Text = Loc.GetString("permissions-eui-menu-save-admin-rank-button"),
                    HorizontalAlignment = HAlignment.Right,
                };

                Editor = new PermissionTreeEditor(tree, editorGrants, PermissionTreeEditor.EditorMode.Rank);
                Editor.SetData(data?.Value.Grants ?? Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

                var buttons = new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                    SeparationOverride = 6,
                    HorizontalAlignment = HAlignment.Right,
                };

                if (data != null)
                {
                    RemoveButton = new Button { Text = Loc.GetString("permissions-eui-menu-remove-admin-rank-button") };
                    buttons.AddChild(RemoveButton);
                }

                buttons.AddChild(SaveButton);

                ContentsContainer.AddChild(new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical,
                    SeparationOverride = 6,
                    VerticalExpand = true,
                    Children = { NameEdit, Editor, buttons },
                });
            }
        }
    }
}
