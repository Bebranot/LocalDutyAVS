using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Duty.ERT;

/// <summary>
/// _Duty: <c>ertcallnow [вариант]</c> — вызывает ОБР немедленно. Без аргумента — шаттл <c>default</c>.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class ErtCallNowCommand : IConsoleCommand
{
    [Dependency] private readonly IEntitySystemManager _sysMan = default!;

    public string Command => "ertcallnow";
    public string Description => Loc.GetString("duty-ert-now-description");
    public string Help => Loc.GetString("duty-ert-now-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var variant = args.Length >= 1 ? args[0] : "default";
        _sysMan.GetEntitySystem<DutyErtSystem>().TryCallErt(shell, shell.Player, variant, delay: null);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        => ErtCompletion.ForVariant(args);
}

/// <summary>
/// _Duty: <c>ertcall5min [вариант]</c> — уведомление и код угрозы сразу, шаттл прибывает через 5 минут.
/// Без аргумента — шаттл <c>default</c>.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class ErtCall5MinCommand : IConsoleCommand
{
    [Dependency] private readonly IEntitySystemManager _sysMan = default!;

    public string Command => "ertcall5min";
    public string Description => Loc.GetString("duty-ert-5min-description");
    public string Help => Loc.GetString("duty-ert-5min-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var variant = args.Length >= 1 ? args[0] : "default";
        _sysMan.GetEntitySystem<DutyErtSystem>().TryCallErt(shell, shell.Player, variant, DutyErtSystem.FiveMinutes);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        => ErtCompletion.ForVariant(args);
}

internal static class ErtCompletion
{
    public static CompletionResult ForVariant(string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        var options = DutyErtSystem.VariantNames
            .Select(v => new CompletionOption(v, Loc.GetString($"duty-ert-hint-{v}")));
        return CompletionResult.FromHintOptions(options, Loc.GetString("duty-ert-hint-variant"));
    }
}
