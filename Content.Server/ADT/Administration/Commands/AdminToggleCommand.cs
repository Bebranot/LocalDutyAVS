using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using System.Linq;

namespace Content.Server.ADT.Administration.Commands;

[AdminCommand(AdminFlags.Permissions)]
public sealed class AdminToggleCommand : LocalizedCommands
{
    [Dependency] private readonly IPlayerLocator _locator = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    public override string Command => "admin_toggle";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteLine("Error: invalid arguments!");
            return;
        }

        var target = args[0];
        var located = await _locator.LookupIdByNameOrIdAsync(target);
        if (located == null)
        {
            shell.WriteError(Loc.GetString("cmd-admin_toggle-error-args"));
            return;
        }

        if (!_playerManager.TryGetSessionById(located.UserId, out var targetSession))
        {
            shell.WriteLine("Not session it's player!");
            return;
        }

        if (targetSession == null)
            return;

        var mgr = IoCManager.Resolve<IAdminManager>();

        // _Duty-start: нельзя трогать админа с большими правами; для не-админа DeAdmin бросал исключение из async void
        if (mgr.GetAdminData(targetSession, includeDeAdmin: true) == null)
        {
            shell.WriteError(Loc.GetString("duty-admin-toggle-not-admin"));
            return;
        }

        if (shell.Player is { } invoker && mgr.IsOutranked(invoker, targetSession))
        {
            shell.WriteError(Loc.GetString("duty-admin-toggle-outranked"));
            return;
        }

        if (args[1] != "deadmin" && args[1] != "readmin")
        {
            shell.WriteError(Loc.GetString("duty-admin-toggle-bad-action"));
            return;
        }

        IoCManager.Resolve<Content.Server.Administration.Logs.IAdminLogManager>().Add(
            Content.Shared.Database.LogType.AdminCommands,
            Content.Shared.Database.LogImpact.Extreme,
            $"{shell.Player} выполнил {args[1]} для {targetSession}");
        // _Duty-end

        if (args[1] == "deadmin")
        {
            mgr.DeAdmin(targetSession);
        }
        else
        {
            mgr.ReAdmin(targetSession);
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var options = _playerManager.Sessions.Select(c => c.Name).OrderBy(c => c).ToArray();
            return CompletionResult.FromHintOptions(
                options,
                LocalizationManager.GetString("cmd-ban-hint"));
        }
        if (args.Length == 2)
        {
            var durations = new CompletionOption[]
            {
                new("deadmin", LocalizationManager.GetString("cmd-admin_toggle-deadmin")),
                new("readmin", LocalizationManager.GetString("cmd-admin_toggle-readmin")),
            };
            return CompletionResult.FromHintOptions(durations, LocalizationManager.GetString("cmd-admin_toggle-hint-duration"));
        }
        return CompletionResult.Empty;
    }
}

/*
    ╔════════════════════════════════════╗
    ║   Schrödinger's Cat Code   🐾      ║
    ║   /\_/\\                           ║
    ║  ( o.o )  Meow!                    ║
    ║   > ^ <                            ║
    ╚════════════════════════════════════╝

*/
