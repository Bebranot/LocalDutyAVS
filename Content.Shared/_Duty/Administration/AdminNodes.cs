// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.Administration;

/// <summary>
/// Id узлов дерева прав, на которые ссылается код (а не только прототипы). Строки в одном месте: опечатка
/// или переименование узла иначе молча закрыли бы доступ. Тест проверяет, что каждая константа есть в дереве.
/// </summary>
public static class AdminNodes
{
    public const string PlayersPanel = "players_panel";
    public const string PlayersBanlist = "players_banlist";
    public const string PlayersKick = "players_kick";
    public const string PlayersBan = "players_ban";
    public const string PlayersNotesView = "players_notes_view";

    public const string ChatAnnounce = "chat_announce";
    public const string ChatAsay = "chat_asay";

    public const string RoundControl = "round_control";

    public const string EntsView = "ents_view";
    public const string EntsTeleport = "ents_teleport";
    public const string EntsVerbs = "ents_verbs";
    public const string EntsSpawn = "ents_spawn";
    public const string EntsDelete = "ents_delete";

    public const string RolesMind = "roles_mind";
    public const string RolesAntag = "roles_antag";
    public const string RolesLaws = "roles_laws";

    public const string FunHealth = "fun_health";
    public const string FunEffects = "fun_effects";

    public const string LogsView = "logs_view";

    public const string PermsEdit = "perms_edit";
    public const string PermsToggle = "perms_toggle";

    public const string HostScript = "host_script";
    public const string HostUpload = "host_upload";
    public const string HostSystem = "host_system";
}
