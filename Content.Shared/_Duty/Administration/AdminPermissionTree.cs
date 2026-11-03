// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared.Administration;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Administration;

/// <summary>
/// Снимок дерева <see cref="AdminPermissionPrototype"/>: потомки, раскрытие выданных узлов, разбор строк из БД.
/// Строится из прототипов один раз на загрузку или перезагрузку прототипов.
/// </summary>
public sealed class AdminPermissionTree
{
    private readonly Dictionary<string, AdminPermissionPrototype> _byId = new();
    private readonly Dictionary<string, List<AdminPermissionPrototype>> _children = new();
    private readonly List<AdminPermissionPrototype> _roots = new();

    /// <summary>Узел и все потомки, посчитанные заранее, чтобы раскрытие прав не обходило дерево каждый раз.</summary>
    private readonly Dictionary<string, HashSet<string>> _descendants = new();

    public IReadOnlyList<AdminPermissionPrototype> Roots => _roots;
    public IEnumerable<AdminPermissionPrototype> All => _byId.Values;

    /// <summary>Ошибки структуры (неизвестный родитель, цикл) для лога и теста.</summary>
    public List<string> Errors { get; } = new();

    public AdminPermissionTree(IPrototypeManager proto)
    {
        foreach (var p in proto.EnumeratePrototypes<AdminPermissionPrototype>())
        {
            _byId[p.ID] = p;
        }

        foreach (var p in _byId.Values)
        {
            if (p.Parent == null)
            {
                _roots.Add(p);
                continue;
            }

            if (!_byId.ContainsKey(p.Parent))
            {
                Errors.Add($"Узел права '{p.ID}' ссылается на неизвестного родителя '{p.Parent}'");
                _roots.Add(p);
                continue;
            }

            if (!_children.TryGetValue(p.Parent, out var list))
                _children[p.Parent] = list = new List<AdminPermissionPrototype>();

            list.Add(p);
        }

        Sort(_roots);
        foreach (var list in _children.Values)
            Sort(list);

        foreach (var id in _byId.Keys)
        {
            var set = new HashSet<string>();
            if (!Collect(id, set, new HashSet<string>()))
                Errors.Add($"В дереве прав цикл через '{id}'");
            _descendants[id] = set;
        }
    }

    private static void Sort(List<AdminPermissionPrototype> list)
    {
        list.Sort((a, b) =>
        {
            var c = a.Order.CompareTo(b.Order);
            return c != 0 ? c : string.CompareOrdinal(a.ID, b.ID);
        });
    }

    private bool Collect(string id, HashSet<string> into, HashSet<string> path)
    {
        if (!path.Add(id))
            return false;

        into.Add(id);
        var ok = true;
        if (_children.TryGetValue(id, out var kids))
        {
            foreach (var k in kids)
            {
                if (!Collect(k.ID, into, path))
                    ok = false;
            }
        }

        path.Remove(id);
        return ok;
    }

    public bool TryGet(string id, out AdminPermissionPrototype proto) => _byId.TryGetValue(id, out proto!);

    public bool Exists(string id) => _byId.ContainsKey(id);

    public IReadOnlyList<AdminPermissionPrototype> ChildrenOf(string id)
    {
        return _children.TryGetValue(id, out var l) ? l : Array.Empty<AdminPermissionPrototype>();
    }

    /// <summary>Сам узел и все его потомки (пусто, если такого узла нет).</summary>
    public IReadOnlySet<string> SelfAndDescendants(string id)
    {
        return _descendants.TryGetValue(id, out var s) ? s : new HashSet<string>();
    }

    /// <summary>Раскрывает выданные узлы: родитель включает всех вложенных.</summary>
    public HashSet<string> Expand(IEnumerable<string> granted)
    {
        var result = new HashSet<string>();
        foreach (var id in granted)
        {
            if (_descendants.TryGetValue(id, out var s))
                result.UnionWith(s);
        }

        return result;
    }

    /// <summary>Старые флаги, которые включают переданные (уже раскрытые) узлы.</summary>
    public AdminFlags LegacyFor(IEnumerable<string> expanded)
    {
        var flags = AdminFlags.None;
        foreach (var id in expanded)
        {
            if (_byId.TryGetValue(id, out var p))
                flags |= p.LegacyFlags;
        }

        return flags;
    }

    /// <summary>Идентификаторы всех узлов.</summary>
    public IEnumerable<string> AllIds() => _byId.Keys;

    /// <summary>
    /// Разбивает строки из БД на старые флаги и узлы. Неизвестное возвращает отдельно,
    /// а не бросает исключение: устаревшее имя в БД не должно ронять вход админа.
    /// </summary>
    public void Split(IEnumerable<string> names, out AdminFlags flags, out List<string> nodes, out List<string> unknown)
    {
        flags = AdminFlags.None;
        nodes = new List<string>();
        unknown = new List<string>();
        foreach (var n in names)
        {
            if (AdminFlagsHelper.TryNameToFlag(n, out var f))
                flags |= f;
            else if (_byId.ContainsKey(n))
                nodes.Add(n);
            else
                unknown.Add(n);
        }
    }

    /// <summary>Команды (консольные и toolshed) узла без потомков.</summary>
    public IEnumerable<string> CommandsOf(string id)
    {
        return _byId.TryGetValue(id, out var p) ? p.Commands.Concat(p.Toolshed) : Enumerable.Empty<string>();
    }
}
