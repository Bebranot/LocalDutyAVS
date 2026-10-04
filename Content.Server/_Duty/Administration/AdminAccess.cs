// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Robust.Shared.Player;

namespace Content.Server._Duty.Administration;

/// <summary>
/// Серверная проверка «узел дерева прав либо любой из прежних флагов, выданных напрямую».
/// Заменяет голое <c>IsAdmin</c>/<c>HasAdminFlag(Admin)</c>, которое давало доступ любому, у кого есть хоть какое-то право.
/// </summary>
public static class AdminAccess
{
    public static bool Can(this IAdminManager admins, ICommonSession session, string node, AdminFlags legacy)
    {
        var data = admins.GetAdminData(session);
        // legacy — «любой из этих флагов»: можно передать Admin | Spawn, если команда раньше открывалась любым из них
        return data != null && data.Active && (data.HasNode(node) || (data.DirectFlags & legacy) != 0);
    }
}
