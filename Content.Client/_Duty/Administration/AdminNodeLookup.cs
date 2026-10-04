// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared._Duty.Administration;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.Administration;

/// <summary>
/// Справочник «команда → узлы прав, которые её открывают» для подсказок и поиска в меню F7.
/// Строится лениво из прототипов и сбрасывается при их перезагрузке.
/// </summary>
public static class AdminNodeLookup
{
    private static Dictionary<string, List<AdminPermissionPrototype>>? _byCommand;
    private static bool _subscribed;

    private static Dictionary<string, List<AdminPermissionPrototype>> Build()
    {
        var proto = IoCManager.Resolve<IPrototypeManager>();
        if (!_subscribed)
        {
            _subscribed = true;
            proto.PrototypesReloaded += args =>
            {
                if (args.WasModified<AdminPermissionPrototype>())
                    _byCommand = null;
            };
        }

        var map = new Dictionary<string, List<AdminPermissionPrototype>>();
        foreach (var node in proto.EnumeratePrototypes<AdminPermissionPrototype>())
        {
            foreach (var cmd in node.Commands)
            {
                if (!map.TryGetValue(cmd, out var list))
                    map[cmd] = list = new List<AdminPermissionPrototype>();
                list.Add(node);
            }
        }

        return map;
    }

    /// <summary>Узлы, открывающие команду (пусто, если команда пока на старых флагах).</summary>
    public static IReadOnlyList<AdminPermissionPrototype> NodesFor(string command)
    {
        _byCommand ??= Build();
        return _byCommand.TryGetValue(command, out var list) ? list : Array.Empty<AdminPermissionPrototype>();
    }

    /// <summary>Строка вида «Выдача банов, Снятие банов» для подсказки, пустая если узлов нет.</summary>
    public static string NodeNames(string command)
    {
        return string.Join(", ", NodesFor(command).Select(n => Loc.GetString(n.NameLoc)));
    }
}
