// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Server._Duty.Trauma.Systems;
using Content.Server.Administration;
using Content.Shared._Duty.Trauma;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Duty.Trauma.Commands;

/// <summary>
/// _Duty (тест): <c>dutybreakbone [цель] [зона]</c> — форсирует ролл перелома. Без цели — на себе,
/// без зоны — обычный взвешенный выбор (как от настоящего удара). Идёт через реальный
/// <c>TraumaRollSystem</c>/<c>TraumaRolledEvent</c>, поэтому шинирование/эскалация/осмотр работают
/// как от настоящей травмы; шанс высокий, но не 100% — см. описание команды.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class DutyBreakBoneCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    public string Command => "dutybreakbone";
    public string Description => Loc.GetString("duty-trauma-debug-fracture-description");
    public string Help => Loc.GetString("duty-trauma-debug-fracture-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!TraumaDebugCommandHelper.TryResolveTarget(shell, _entMan, args, 0, out var target))
            return;

        if (!TraumaDebugCommandHelper.TryParseZone(shell, args, 1, out var zone))
            return;

        var result = _entMan.System<TraumaRollSystem>().DebugForceFracture(target, zone);
        TraumaDebugCommandHelper.Report(shell, "duty-trauma-debug-name-fracture", result);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) => args.Length switch
    {
        1 => CompletionResult.FromHint(Loc.GetString("duty-trauma-debug-hint-target")),
        2 => TraumaDebugCommandHelper.ZoneCompletion(),
        _ => CompletionResult.Empty,
    };
}

/// <summary>
/// _Duty (тест): <c>dutydislocate [цель] [зона]</c> — форсирует ролл вывиха. Зона обязана быть
/// суставной (рука/нога); без явной зоны — случайная среди доступных существу суставов.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class DutyDislocateCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    public string Command => "dutydislocate";
    public string Description => Loc.GetString("duty-trauma-debug-dislocation-description");
    public string Help => Loc.GetString("duty-trauma-debug-dislocation-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!TraumaDebugCommandHelper.TryResolveTarget(shell, _entMan, args, 0, out var target))
            return;

        if (!TraumaDebugCommandHelper.TryParseZone(shell, args, 1, out var zone))
            return;

        var result = _entMan.System<TraumaRollSystem>().DebugForceDislocation(target, zone);
        TraumaDebugCommandHelper.Report(shell, "duty-trauma-debug-name-dislocation", result);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) => args.Length switch
    {
        1 => CompletionResult.FromHint(Loc.GetString("duty-trauma-debug-hint-target")),
        2 => TraumaDebugCommandHelper.ZoneCompletion(),
        _ => CompletionResult.Empty,
    };
}

/// <summary>_Duty (тест): <c>dutybleedout [цель]</c> — форсирует ролл артериального кровотечения.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class DutyBleedOutCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    public string Command => "dutybleedout";
    public string Description => Loc.GetString("duty-trauma-debug-arterial-description");
    public string Help => Loc.GetString("duty-trauma-debug-arterial-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!TraumaDebugCommandHelper.TryResolveTarget(shell, _entMan, args, 0, out var target))
            return;

        var result = _entMan.System<TraumaRollSystem>().DebugForceArterial(target);
        TraumaDebugCommandHelper.Report(shell, "duty-trauma-debug-name-arterial", result);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) => args.Length == 1
        ? CompletionResult.FromHint(Loc.GetString("duty-trauma-debug-hint-target"))
        : CompletionResult.Empty;
}

/// <summary>
/// _Duty (тест): <c>dutyconcussion [цель]</c> — форсирует ролл перелома ЗОНЫ ГОЛОВА. Само
/// сотрясение не роллится напрямую (см. дизайн) — оно запускается автоматически через
/// <c>BrainTraumaSystem</c>, подписанную на перелом головы, ровно как от настоящего удара.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class DutyConcussionCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    public string Command => "dutyconcussion";
    public string Description => Loc.GetString("duty-trauma-debug-head-description");
    public string Help => Loc.GetString("duty-trauma-debug-head-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!TraumaDebugCommandHelper.TryResolveTarget(shell, _entMan, args, 0, out var target))
            return;

        var result = _entMan.System<TraumaRollSystem>().DebugForceFracture(target, BodyZone.Head);
        TraumaDebugCommandHelper.Report(shell, "duty-trauma-debug-name-head", result);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) => args.Length == 1
        ? CompletionResult.FromHint(Loc.GetString("duty-trauma-debug-hint-target"))
        : CompletionResult.Empty;
}

/// <summary>_Duty (тест): общий парсинг цели/зоны и единый вывод результата для duty*-команд травм.</summary>
internal static class TraumaDebugCommandHelper
{
    public static bool TryResolveTarget(IConsoleShell shell, IEntityManager entMan, string[] args, int targetArgIndex, out EntityUid target)
    {
        target = default;

        if (args.Length > targetArgIndex && !string.IsNullOrEmpty(args[targetArgIndex]))
        {
            if (!entMan.TryParseNetEntity(args[targetArgIndex], out var parsed) || !entMan.EntityExists(parsed))
            {
                shell.WriteLine(Loc.GetString("duty-trauma-debug-error-target", ("arg", args[targetArgIndex])));
                return false;
            }

            target = parsed.Value;
            return true;
        }

        if (shell.Player?.AttachedEntity is { Valid: true } playerEntity)
        {
            target = playerEntity;
            return true;
        }

        shell.WriteLine(Loc.GetString("duty-trauma-debug-error-no-player"));
        return false;
    }

    public static bool TryParseZone(IConsoleShell shell, string[] args, int zoneArgIndex, out BodyZone? zone)
    {
        zone = null;
        if (args.Length <= zoneArgIndex || string.IsNullOrEmpty(args[zoneArgIndex]))
            return true;

        if (!Enum.TryParse<BodyZone>(args[zoneArgIndex], true, out var parsed))
        {
            shell.WriteLine(Loc.GetString("duty-trauma-debug-error-zone", ("arg", args[zoneArgIndex])));
            return false;
        }

        zone = parsed;
        return true;
    }

    public static void Report(IConsoleShell shell, string traumaLocKey, TraumaDebugRollResult result)
    {
        if (!result.TargetValid)
        {
            shell.WriteLine(Loc.GetString("duty-trauma-debug-error-invalid-target"));
            return;
        }

        var traumaName = Loc.GetString(traumaLocKey);
        var zoneText = result.Zone is { } zone
            ? Loc.GetString(TraumaLoc.ZoneKey(zone))
            : Loc.GetString("duty-trauma-debug-zone-none");
        var chancePercent = (int)MathF.Round(result.Chance * 100);

        shell.WriteLine(Loc.GetString(
            result.Success ? "duty-trauma-debug-success" : "duty-trauma-debug-fail",
            ("trauma", traumaName),
            ("zone", zoneText),
            ("chance", chancePercent)));
    }

    public static CompletionResult ZoneCompletion()
    {
        var options = Enum.GetValues<BodyZone>().Select(z => new CompletionOption(z.ToString()));
        return CompletionResult.FromHintOptions(options, Loc.GetString("duty-trauma-debug-hint-zone"));
    }
}
