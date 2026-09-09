// SPDX-FileCopyrightText: 2026 LocalDuty
// SPDX-License-Identifier: MIT

using Robust.Shared.Console;

namespace Content.Client._Duty.SpawnMenu;

/// <summary>
/// Открывает меню выдачи предметов. Команда клиентская и доступна всем — список
/// предметов и сам спавн сервер отдаёт только тем сикеям, что перечислены в YAML.
/// </summary>
public sealed class DutySpawnMenuCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "dutyspawn";
    public string Description => Loc.GetString("duty-spawn-menu-command-description");
    public string Help => $"Usage: {Command}";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _entities.System<DutySpawnMenuSystem>().ToggleWindow();
    }
}
