// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared._Duty.Administration;
using Content.Shared.Administration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Administration.Managers;

// Дерево прав поверх старых флагов: узлы (adminPermission) открывают свои команды и включают вложенные узлы.
public sealed partial class AdminManager
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private AdminPermissionTree? _tree;

    // команда -> узлы, любой из которых её открывает (отдельно консоль и toolshed: имена могут совпадать)
    private Dictionary<string, string[]> _nodeCommands = new();
    private Dictionary<string, string[]> _nodeToolshed = new();

    public AdminPermissionTree PermissionTree => EnsureTree();

    private void InitializePermissionTree()
    {
        _proto.PrototypesReloaded += args =>
        {
            if (args.WasModified<AdminPermissionPrototype>())
                _tree = null;
        };
    }

    private readonly object _treeLock = new();

    private AdminPermissionTree EnsureTree()
    {
        // Зовётся и из async-продолжений (баны через API, резервные слоты), поэтому под замком
        lock (_treeLock)
        {
            return _tree ??= BuildTree();
        }
    }

    private AdminPermissionTree BuildTree()
    {
        var tree = new AdminPermissionTree(_proto);
        foreach (var error in tree.Errors)
        {
            _sawmill.Error(error);
        }

        var console = new Dictionary<string, List<string>>();
        var toolshed = new Dictionary<string, List<string>>();
        foreach (var node in tree.All)
        {
            foreach (var cmd in node.Commands)
            {
                if (!console.TryGetValue(cmd, out var l))
                    console[cmd] = l = new List<string>();
                l.Add(node.ID);

                if (!_commandPermissions.AdminCommands.ContainsKey(cmd) && !_commandPermissions.AnyCommands.Contains(cmd))
                    _sawmill.Warning($"Узел права '{node.ID}' ссылается на неизвестную консольную команду '{cmd}'");
            }

            foreach (var cmd in node.Toolshed)
            {
                if (!toolshed.TryGetValue(cmd, out var l))
                    toolshed[cmd] = l = new List<string>();
                l.Add(node.ID);

                if (!_toolshedCommandPermissions.AdminCommands.ContainsKey(cmd) && !_toolshedCommandPermissions.AnyCommands.Contains(cmd))
                    _sawmill.Warning($"Узел права '{node.ID}' ссылается на неизвестную toolshed-команду '{cmd}'");
            }
        }

        _nodeCommands = console.ToDictionary(p => p.Key, p => p.Value.ToArray());
        _nodeToolshed = toolshed.ToDictionary(p => p.Key, p => p.Value.ToArray());
        return tree;
    }

    /// <summary>
    /// Собирает права из строк БД: сначала всё выданное рангом и лично, потом вычитаются запреты.
    /// Неизвестные имена пропускаются с предупреждением.
    /// </summary>
    private void ResolveGrants(
        IEnumerable<string> rankGrants,
        IEnumerable<(string Name, bool Negative)> personal,
        out AdminFlags directFlags,
        out HashSet<string> nodes,
        out AdminFlags effectiveFlags)
    {
        var tree = EnsureTree();

        var pos = rankGrants.ToList();
        var neg = new List<string>();
        foreach (var (name, negative) in personal)
        {
            if (negative)
                neg.Add(name);
            else
                pos.Add(name);
        }

        tree.Split(pos, out var posFlags, out var posNodes, out var unknownPos);
        tree.Split(neg, out var negFlags, out var negNodes, out var unknownNeg);

        foreach (var name in unknownPos.Concat(unknownNeg))
        {
            _sawmill.Warning($"В правах админа неизвестное имя '{name}', пропускаю");
        }

        directFlags = posFlags & ~negFlags;
        nodes = tree.Expand(posNodes);
        nodes.ExceptWith(tree.Expand(negNodes));

        // Host из БД (флаг HOST) — владелец: у него есть все узлы, как у входа через консоль
        if ((directFlags & AdminFlags.Host) != 0)
            nodes = new HashSet<string>(tree.AllIds());
        effectiveFlags = directFlags | tree.LegacyFor(nodes);
    }

    public (AdminFlags Direct, HashSet<string> Nodes) ResolveDatabaseAdmin(Database.Admin dbAdmin)
    {
        ResolveGrants(
            dbAdmin.AdminRank?.Flags.Select(p => p.Flag) ?? Enumerable.Empty<string>(),
            dbAdmin.Flags.Select(f => (f.Flag, f.Negative)),
            out var direct,
            out var nodes,
            out _);
        return (direct, nodes);
    }

    public bool IsProtectedFromBan(Database.Admin? dbAdmin)
    {
        if (dbAdmin == null || dbAdmin.Suspended)
            return false;

        var (direct, nodes) = ResolveDatabaseAdmin(dbAdmin);
        return (direct & (AdminFlags.Permissions | AdminFlags.Host)) != 0
               || nodes.Contains("perms_edit")
               || nodes.Contains("perms_toggle")
               || nodes.Contains("host_script");
    }

    public bool IsOutranked(ICommonSession invoker, ICommonSession target)
    {
        if (invoker == target)
            return false;

        var targetData = GetAdminData(target, includeDeAdmin: true);
        if (targetData == null)
            return false;

        var invokerData = GetAdminData(invoker);
        if (invokerData == null)
            return true;

        // Host не может быть «ниже» кого-либо
        if ((invokerData.DirectFlags & AdminFlags.Host) != 0)
            return false;

        if ((targetData.DirectFlags & ~invokerData.DirectFlags) != 0)
            return true;

        // Узел цели покрыт, если он есть у invoker либо открыт его старыми флагами (флаги и узлы — две модели одних прав)
        var tree = EnsureTree();
        foreach (var nodeId in targetData.Nodes)
        {
            if (invokerData.Nodes.Contains(nodeId))
                continue;

            if (!tree.TryGet(nodeId, out var node) || !NodeCoveredByFlags(node, invokerData.DirectFlags))
                return true;
        }

        return false;
    }

    /// <summary>Открывают ли старые флаги всё, что открывает узел: его флаги в legacy либо каждую его команду.</summary>
    private bool NodeCoveredByFlags(AdminPermissionPrototype node, AdminFlags direct)
    {
        var legacy = node.LegacyFlags;
        if (legacy != AdminFlags.None && (direct & legacy) == legacy)
            return true;

        if (node.Commands.Count == 0 && node.Toolshed.Count == 0)
            return false;

        foreach (var cmd in node.Commands)
        {
            if (!_commandPermissions.AdminCommands.TryGetValue(cmd, out var req) || !req.Any(f => (direct & f) == f))
                return false;
        }

        foreach (var cmd in node.Toolshed)
        {
            if (!_toolshedCommandPermissions.AdminCommands.TryGetValue(cmd, out var req) || !req.Any(f => (direct & f) == f))
                return false;
        }

        return true;
    }

    public bool HasHostFlag(Database.Admin? dbAdmin)
    {
        if (dbAdmin == null || dbAdmin.Suspended)
            return false;

        var (direct, _) = ResolveDatabaseAdmin(dbAdmin);
        return (direct & AdminFlags.Host) != 0;
    }

    /// <summary>Открывает ли команду набор прав: узел из списка команд либо старый флаг, выданный напрямую.</summary>
    private static bool CommandAllowed(AdminData data, string cmdName, AdminFlags[] flagsReq, Dictionary<string, string[]> nodeMap)
    {
        if (nodeMap.TryGetValue(cmdName, out var nodes))
        {
            foreach (var node in nodes)
            {
                if (data.HasNode(node))
                    return true;
            }
        }

        foreach (var flag in flagsReq)
        {
            if (data.HasDirectFlag(flag))
                return true;
        }

        return false;
    }

    private bool CommandAllowedForConsole(AdminData data, string cmdName, AdminFlags[] flagsReq)
    {
        EnsureTree();
        return CommandAllowed(data, cmdName, flagsReq, _nodeCommands);
    }

    private bool CommandAllowedForToolshed(AdminData data, string cmdName, AdminFlags[] flagsReq)
    {
        EnsureTree();
        return CommandAllowed(data, cmdName, flagsReq, _nodeToolshed);
    }
}
