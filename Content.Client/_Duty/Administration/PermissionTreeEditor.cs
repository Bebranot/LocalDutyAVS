// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Numerics;
using Content.Shared._Duty.Administration;
using Content.Shared.Administration;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Duty.Administration;

/// <summary>
/// Дерево выдачи админ-прав: области и подфлаги с галочками, поиск по названию, описанию и командам,
/// подсказка с командами при наведении и панель описания внизу. Используется и для рангов, и для самих админов.
/// Внутри хранится выбор по «листьям»; при сохранении полностью выбранные области сворачиваются в один id области.
/// </summary>
public sealed class PermissionTreeEditor : BoxContainer
{
    public enum EditorMode
    {
        /// <summary>Только разрешения.</summary>
        Rank,

        /// <summary>Разрешения и личные запреты поверх ранга.</summary>
        Admin,
    }

    private const int MaxCommandsInTip = 24;

    private static readonly Color ColorView = Color.FromHex("#639922");
    private static readonly Color ColorAction = Color.FromHex("#BA7517");
    private static readonly Color ColorDanger = Color.FromHex("#E24B4A");

    private sealed class Entry
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Desc = string.Empty;
        public string[] Commands = Array.Empty<string>();
        public AdminPermissionDanger Danger;
        public bool Legacy;
        public bool Held;
        public string Search = string.Empty;

        public Entry? Parent;
        public readonly List<Entry> Children = new();

        public BoxContainer Holder = default!;
        public BoxContainer ChildBox = default!;
        public CheckBox Check = default!;
        public CheckBox? Block;
        public Label Counter = default!;
        public Button? Fold;
        public bool Folded = true;

        public bool IsLeaf => Children.Count == 0;
    }

    private readonly AdminPermissionTree _tree;
    private readonly IReadOnlySet<string> _editorGrants;
    private readonly EditorMode _mode;

    private readonly List<Entry> _roots = new();
    private readonly Dictionary<string, Entry> _byId = new();

    private readonly HashSet<string> _selected = new();
    private readonly HashSet<string> _blocked = new();
    private HashSet<string> _inherited = new();

    private readonly LineEdit _search;
    private readonly BoxContainer _list;
    private readonly RichTextLabel _info;
    private readonly Label _summary;
    private bool _refreshing;

    public event Action? Changed;

    public PermissionTreeEditor(AdminPermissionTree tree, IReadOnlySet<string> editorGrants, EditorMode mode)
    {
        _tree = tree;
        _editorGrants = editorGrants;
        _mode = mode;

        Orientation = LayoutOrientation.Vertical;
        VerticalExpand = true;
        HorizontalExpand = true;
        SeparationOverride = 4;

        _search = new LineEdit
        {
            PlaceHolder = Loc.GetString("duty-perm-ui-search"),
            HorizontalExpand = true,
        };
        _search.OnTextChanged += args => ApplyFilter(args.Text);

        var viewOnly = new Button { Text = Loc.GetString("duty-perm-ui-view-only") };
        viewOnly.OnPressed += _ => SelectViewOnly();
        var clear = new Button { Text = Loc.GetString("duty-perm-ui-clear") };
        clear.OnPressed += _ => ClearSelection();
        var fold = new Button { Text = Loc.GetString("duty-perm-ui-fold") };
        fold.OnPressed += _ => SetAllFolded(true);
        var unfold = new Button { Text = Loc.GetString("duty-perm-ui-unfold") };
        unfold.OnPressed += _ => SetAllFolded(false);

        AddChild(new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            Children = { _search, viewOnly, clear, unfold, fold },
        });

        _list = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        AddChild(new PanelContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#00000030") },
            Children =
            {
                new ScrollContainer
                {
                    VerticalExpand = true,
                    HorizontalExpand = true,
                    HScrollEnabled = false,
                    Children = { _list },
                },
            },
        });

        _info = new RichTextLabel { HorizontalExpand = true };
        AddChild(new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#00000050") },
            MinSize = new Vector2(0, 76),
            Children = { new Control { Margin = new Thickness(6), Children = { _info } } },
        });

        _summary = new Label { StyleClasses = { "LabelSubText" } };
        AddChild(_summary);

        BuildEntries();
        ShowInfo(null);
        Refresh();
    }

    private void BuildEntries()
    {
        foreach (var root in _tree.Roots)
        {
            _roots.Add(BuildNode(root, null));
        }

        // Устаревшие флаги идут в конец: они грубые и нужны только для команд и проверок, ещё не разнесённых по узлам.
        var legacy = new Entry
        {
            Id = "legacy",
            Name = Loc.GetString("duty-perm-legacy"),
            Desc = Loc.GetString("duty-perm-legacy-area-desc"),
            Danger = AdminPermissionDanger.Danger,
            Legacy = true,
            Held = false,
        };
        foreach (var flag in AdminFlagsHelper.AllFlags)
        {
            var name = flag.ToString().ToUpperInvariant();
            var key = "duty-perm-legacy-" + name.ToLowerInvariant();
            var child = new Entry
            {
                Id = name,
                Name = name,
                Desc = Loc.GetString(key + ".desc"),
                Danger = flag is AdminFlags.Host or AdminFlags.Permissions or AdminFlags.Debug or AdminFlags.VarEdit
                    ? AdminPermissionDanger.Danger
                    : AdminPermissionDanger.Action,
                Legacy = true,
                Held = _editorGrants.Contains(name),
                Parent = legacy,
            };
            legacy.Children.Add(child);
        }

        legacy.Held = legacy.Children.All(c => c.Held);
        _roots.Add(legacy);

        foreach (var entry in _roots)
        {
            RegisterAndBuild(entry);
        }
    }

    private Entry BuildNode(AdminPermissionPrototype proto, Entry? parent)
    {
        var commands = proto.Commands.Concat(proto.Toolshed).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray();
        var entry = new Entry
        {
            Id = proto.ID,
            Name = Loc.GetString(proto.NameLoc),
            Desc = Loc.GetString(proto.DescLoc),
            Commands = commands,
            Danger = proto.Danger,
            Held = _editorGrants.Contains(proto.ID),
            Parent = parent,
        };

        foreach (var child in _tree.ChildrenOf(proto.ID))
        {
            entry.Children.Add(BuildNode(child, entry));
        }

        return entry;
    }

    /// <summary>Строит контролы дерева сверху вниз, чтобы дочерние вставали в контейнер родителя.</summary>
    private void RegisterAndBuild(Entry entry)
    {
        BuildControls(entry);
        _list.AddChild(entry.Holder);
    }

    private void BuildControls(Entry entry)
    {
        _byId[entry.Id] = entry;
        entry.Search = string.Join(" ", new[]
        {
            entry.Name.ToLowerInvariant(),
            entry.Desc.ToLowerInvariant(),
            entry.Id.ToLowerInvariant(),
            string.Join(" ", entry.Commands).ToLowerInvariant(),
        });

        var depth = 0;
        for (var p = entry.Parent; p != null; p = p.Parent)
            depth++;

        var dot = new PanelContainer
        {
            MinSize = new Vector2(8, 8),
            VerticalAlignment = VAlignment.Center,
            PanelOverride = new StyleBoxFlat { BackgroundColor = DangerColor(entry.Danger) },
        };

        entry.Check = new CheckBox { Text = entry.Name, HorizontalExpand = true };
        entry.Check.OnToggled += args => OnCheckToggled(entry, args.Pressed);
        entry.Check.OnMouseEntered += _ => ShowInfo(entry);
        entry.Check.TooltipDelay = 0.25f;
        entry.Check.TooltipSupplier = _ => BuildTooltip(entry);
        if (!entry.Legacy || !entry.IsLeaf)
            entry.Check.ToolTip = entry.Desc; // запасной вариант, если поставщик подсказки не сработает

        entry.Counter = new Label { StyleClasses = { "LabelSubText" } };

        var header = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            Margin = new Thickness(depth * 18, 0, 0, 0),
        };

        if (!entry.IsLeaf)
        {
            entry.Fold = new Button { Text = "+", MinSize = new Vector2(24, 0) };
            entry.Fold.OnPressed += _ => SetFolded(entry, !entry.Folded);
            header.AddChild(entry.Fold);
        }
        else
        {
            header.AddChild(new Control { MinSize = new Vector2(24, 0) });
        }

        header.AddChild(dot);
        header.AddChild(entry.Check);
        header.AddChild(entry.Counter);

        if (_mode == EditorMode.Admin)
        {
            entry.Block = new CheckBox
            {
                Text = Loc.GetString("duty-perm-ui-block"),
                Modulate = Color.FromHex("#E27A7A"),
                ToolTip = Loc.GetString("duty-perm-ui-block-tooltip"),
            };
            entry.Block.OnToggled += args => OnBlockToggled(entry, args.Pressed);
            header.AddChild(entry.Block);
        }

        entry.ChildBox = new BoxContainer { Orientation = LayoutOrientation.Vertical, Visible = false };
        entry.Holder = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Children = { header, entry.ChildBox },
        };

        foreach (var child in entry.Children)
        {
            BuildControls(child);
            entry.ChildBox.AddChild(child.Holder);
        }
    }

    private static Color DangerColor(AdminPermissionDanger danger)
    {
        return danger switch
        {
            AdminPermissionDanger.View => ColorView,
            AdminPermissionDanger.Danger => ColorDanger,
            _ => ColorAction,
        };
    }

    private static string DangerText(AdminPermissionDanger danger)
    {
        return Loc.GetString(danger switch
        {
            AdminPermissionDanger.View => "duty-perm-ui-danger-view",
            AdminPermissionDanger.Danger => "duty-perm-ui-danger-danger",
            _ => "duty-perm-ui-danger-action",
        });
    }

    private Control BuildTooltip(Entry entry)
    {
        var msg = BuildInfoMessage(entry, MaxCommandsInTip);
        var tip = new Tooltip();
        tip.SetMessage(msg);
        tip.MaxWidth = 320f;
        return tip;
    }

    private FormattedMessage BuildInfoMessage(Entry entry, int maxCommands)
    {
        var msg = new FormattedMessage();
        msg.PushColor(Color.White);
        msg.AddText(entry.Name);
        msg.Pop();
        msg.AddText("  ");
        msg.PushColor(DangerColor(entry.Danger));
        msg.AddText(DangerText(entry.Danger));
        msg.Pop();
        msg.PushNewline();
        msg.AddText(entry.Desc);

        var commands = CollectCommands(entry);
        if (commands.Count > 0)
        {
            msg.PushNewline();
            msg.PushColor(Color.FromHex("#9a9a9a"));
            msg.AddText(Loc.GetString("duty-perm-ui-commands"));
            msg.Pop();
            msg.AddText(" " + string.Join(", ", commands.Take(maxCommands)));
            if (commands.Count > maxCommands)
                msg.AddText(" " + Loc.GetString("duty-perm-ui-more", ("count", commands.Count - maxCommands)));
        }
        else if (!entry.Legacy && entry.IsLeaf)
        {
            msg.PushNewline();
            msg.PushColor(Color.FromHex("#9a9a9a"));
            msg.AddText(Loc.GetString("duty-perm-ui-no-commands"));
            msg.Pop();
        }

        if (!entry.IsLeaf)
        {
            msg.PushNewline();
            msg.PushColor(Color.FromHex("#9a9a9a"));
            msg.AddText(Loc.GetString("duty-perm-ui-inside", ("count", Leaves(entry, false).Count)));
            msg.Pop();
        }

        if (!entry.Held && !AllLeavesHeld(entry))
        {
            msg.PushNewline();
            msg.PushColor(ColorDanger);
            msg.AddText(Loc.GetString("duty-perm-ui-not-held"));
            msg.Pop();
        }

        return msg;
    }

    private List<string> CollectCommands(Entry entry)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        Walk(entry, e => set.UnionWith(e.Commands));
        return set.ToList();
    }

    private static void Walk(Entry entry, Action<Entry> action)
    {
        action(entry);
        foreach (var c in entry.Children)
            Walk(c, action);
    }

    private static List<Entry> Leaves(Entry entry, bool onlyHeld)
    {
        var list = new List<Entry>();
        Walk(entry, e =>
        {
            if (e.IsLeaf && (!onlyHeld || e.Held))
                list.Add(e);
        });
        return list;
    }

    private static bool AllLeavesHeld(Entry entry) => Leaves(entry, false).All(l => l.Held);

    private void ShowInfo(Entry? entry)
    {
        if (entry == null)
        {
            var hint = new FormattedMessage();
            hint.AddText(Loc.GetString("duty-perm-ui-hint"));
            _info.SetMessage(hint);
            return;
        }

        _info.SetMessage(BuildInfoMessage(entry, 60));
    }

    // ---- выбор -------------------------------------------------------------------------------------------------

    private void OnCheckToggled(Entry entry, bool pressed)
    {
        if (_refreshing)
            return;

        foreach (var leaf in Leaves(entry, true))
        {
            if (_inherited.Contains(leaf.Id))
                continue;

            if (pressed)
                _selected.Add(leaf.Id);
            else
                _selected.Remove(leaf.Id);
        }

        Refresh();
        Changed?.Invoke();
    }

    private void OnBlockToggled(Entry entry, bool pressed)
    {
        if (_refreshing)
            return;

        foreach (var leaf in Leaves(entry, false))
        {
            if (pressed)
                _blocked.Add(leaf.Id);
            else
                _blocked.Remove(leaf.Id);
        }

        Refresh();
        Changed?.Invoke();
    }

    private void SelectViewOnly()
    {
        foreach (var leaf in _byId.Values.Where(e => e.IsLeaf && e.Held && e.Danger == AdminPermissionDanger.View))
        {
            if (!_inherited.Contains(leaf.Id))
                _selected.Add(leaf.Id);
        }

        Refresh();
        Changed?.Invoke();
    }

    private void ClearSelection()
    {
        _selected.Clear();
        _blocked.Clear();
        Refresh();
        Changed?.Invoke();
    }

    private void SetAllFolded(bool folded)
    {
        foreach (var e in _byId.Values.Where(e => !e.IsLeaf))
            SetFolded(e, folded);
    }

    private void SetFolded(Entry entry, bool folded)
    {
        entry.Folded = folded;
        entry.ChildBox.Visible = !folded;
        if (entry.Fold != null)
            entry.Fold.Text = folded ? "+" : "-";
    }

    /// <summary>Приводит галочки и счётчики в соответствие с выбором, не вызывая обработчики.</summary>
    private void Refresh()
    {
        _refreshing = true;
        foreach (var e in _byId.Values)
        {
            var leaves = Leaves(e, true);
            var all = Leaves(e, false);

            var on = leaves.Count(l => _selected.Contains(l.Id) || _inherited.Contains(l.Id));
            var free = leaves.Count(l => !_inherited.Contains(l.Id));

            e.Check.Pressed = leaves.Count > 0 && on == leaves.Count;
            e.Check.Disabled = leaves.Count == 0 || free == 0;
            e.Check.Modulate = e.Check.Disabled && free == 0 && leaves.Count > 0 ? Color.FromHex("#a0c8ff") : Color.White;

            if (!e.IsLeaf)
                e.Counter.Text = $"{on}/{leaves.Count}";
            else if (_inherited.Contains(e.Id))
                e.Counter.Text = Loc.GetString("duty-perm-ui-from-rank");
            else
                e.Counter.Text = string.Empty;

            if (e.Block != null)
            {
                e.Block.Pressed = all.Count > 0 && all.All(l => _blocked.Contains(l.Id));
            }
        }

        _refreshing = false;

        var total = _byId.Values.Count(e => e.IsLeaf);
        var chosen = _byId.Values.Count(e => e.IsLeaf && (_selected.Contains(e.Id) || _inherited.Contains(e.Id)));
        _summary.Text = Loc.GetString("duty-perm-ui-summary", ("chosen", chosen), ("total", total), ("blocked", _blocked.Count));
    }

    private void ApplyFilter(string text)
    {
        var q = text.Trim().ToLowerInvariant();
        if (q.Length == 0)
        {
            foreach (var e in _byId.Values)
            {
                e.Holder.Visible = true;
                SetFolded(e, e.Folded);
            }

            return;
        }

        foreach (var root in _roots)
            FilterNode(root, q, false);
    }

    /// <summary>Узел виден, если подошёл он сам, кто-то из предков (тогда видно всё внутри) или кто-то из потомков.</summary>
    private bool FilterNode(Entry entry, string q, bool ancestorMatched)
    {
        var self = entry.Search.Contains(q);
        var show = self || ancestorMatched;
        var anyChild = false;
        foreach (var c in entry.Children)
        {
            if (FilterNode(c, q, show))
                anyChild = true;
        }

        var visible = show || anyChild;
        entry.Holder.Visible = visible;
        if (!entry.IsLeaf)
        {
            // при поиске раскрываем всё найденное
            entry.ChildBox.Visible = visible;
            if (entry.Fold != null)
                entry.Fold.Text = visible ? "-" : "+";
        }

        return visible;
    }

    // ---- обмен с окном -----------------------------------------------------------------------------------------

    /// <summary>Загружает выданное: узлы раскрываются до листьев, неизвестные имена пропускаются.</summary>
    public void SetData(IEnumerable<string> pos, IEnumerable<string> neg, IEnumerable<string> inherited)
    {
        _selected.Clear();
        _blocked.Clear();
        _inherited = ExpandToLeaves(inherited);

        foreach (var id in ExpandToLeaves(pos))
        {
            if (!_inherited.Contains(id))
                _selected.Add(id);
        }

        foreach (var id in ExpandToLeaves(neg))
            _blocked.Add(id);

        Refresh();
    }

    /// <summary>Меняет права, пришедшие из ранга, не трогая личный выбор.</summary>
    public void SetInherited(IEnumerable<string> inherited)
    {
        _inherited = ExpandToLeaves(inherited);
        _selected.ExceptWith(_inherited);
        Refresh();
    }

    private HashSet<string> ExpandToLeaves(IEnumerable<string> grants)
    {
        var result = new HashSet<string>();
        foreach (var id in grants)
        {
            if (!_byId.TryGetValue(id, out var entry))
                continue;

            foreach (var leaf in Leaves(entry, false))
                result.Add(leaf.Id);
        }

        return result;
    }

    /// <summary>Права для сохранения: полностью выбранная область (и доступная редактору целиком) пишется одним id.</summary>
    public string[] CollectPositive()
    {
        var result = new List<string>();
        foreach (var root in _roots)
            Compact(root, _selected, true, result);
        return result.ToArray();
    }

    public string[] CollectNegative()
    {
        var result = new List<string>();
        foreach (var root in _roots)
            Compact(root, _blocked, false, result);
        return result.ToArray();
    }

    private void Compact(Entry entry, HashSet<string> set, bool requireHeld, List<string> into)
    {
        // Область «legacy» виртуальная: писать её id нельзя, только отдельные флаги.
        var leaves = Leaves(entry, false);
        var all = leaves.Count > 0 && leaves.All(l => set.Contains(l.Id));
        if (all && !entry.Legacy && (!requireHeld || entry.Held))
        {
            into.Add(entry.Id);
            return;
        }

        if (entry.IsLeaf)
        {
            if (set.Contains(entry.Id))
                into.Add(entry.Id);
            return;
        }

        foreach (var child in entry.Children)
            Compact(child, set, requireHeld, into);
    }
}
