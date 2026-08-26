// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Administration;
using Content.Server.AlertLevel;
using Content.Shared._Duty.CodeAlpha;
using Content.Shared.Administration;
using Content.Shared.Station;
using Robust.Shared.Console;

namespace Content.Server._Duty.CodeAlpha;

/// <summary>
/// _Duty: единственный способ включить и выключить протокол «Альфа».
///
/// Автотриггера по объявлению войны нет: код включает админ, когда решит, что раунд того стоит.
/// Выключать тоже лучше отсюда, а не одним <c>setalertlevel green</c>:
/// 1. <c>AlertLevelSystem.SetLevel</c> молча выходит, если станция уже на запрошенном уровне, —
///    значит <c>setalertlevel green</c> при уже зелёном коде не поднимет событие и протокол
///    останется висеть;
/// 2. <c>setalertlevel</c> требует, чтобы админ был привязан к сущности на станции, — из агоста
///    в космосе он не работает.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed class DutyCodeAlphaCommand : LocalizedEntityCommands
{
    [Dependency] private readonly DutyCodeAlphaSystem _alpha = default!;
    [Dependency] private readonly AlertLevelSystem _alertLevel = default!;
    [Dependency] private readonly SharedStationSystem _station = default!;

    public override string Command => "dutycodealpha";

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(["on", "off"], Loc.GetString("cmd-dutycodealpha-hint"))
            : CompletionResult.Empty;
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "on":
                ExecuteOn(shell);
                break;
            case "off":
                ExecuteOff(shell);
                break;
            default:
                shell.WriteError(Loc.GetString("cmd-dutycodealpha-bad-arg"));
                break;
        }
    }

    private void ExecuteOn(IConsoleShell shell)
    {
        if (_alpha.IsActive)
        {
            shell.WriteError(Loc.GetString("cmd-dutycodealpha-already-on"));
            return;
        }

        if (!TryResolveStation(shell, out var station))
            return;

        if (!_alpha.Activate(station))
        {
            shell.WriteError(Loc.GetString("cmd-dutycodealpha-failed"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-dutycodealpha-on"));
    }

    private void ExecuteOff(IConsoleShell shell)
    {
        if (!_alpha.IsActive)
        {
            shell.WriteError(Loc.GetString("cmd-dutycodealpha-already-off"));
            return;
        }

        // Станцию берём у самого протокола, а не под админом: Deactivate её обнулит, а команду
        // могут звать из агоста в космосе, где GetOwningStation вернёт null.
        var station = _alpha.ActiveStation;

        // Сначала снимаем доступы, потом двигаем уровень: SetLevel поднимет
        // AlertLevelChangedEvent, но к этому моменту снимать уже нечего.
        _alpha.Deactivate();

        if (station is { } target)
        {
            _alertLevel.SetLevel(
                target,
                DutyCodeAlphaVisuals.GreenLevel,
                playSound: true,
                announce: true,
                force: true);
        }

        shell.WriteLine(Loc.GetString("cmd-dutycodealpha-off"));
    }

    /// <summary>
    /// Станция под админом, а если он вне станции (агост в космосе) — первая станция с уровнем
    /// тревоги.
    /// </summary>
    private bool TryResolveStation(IConsoleShell shell, out EntityUid station)
    {
        station = EntityUid.Invalid;

        if (shell.Player?.AttachedEntity is { } attached
            && _station.GetOwningStation(attached) is { } owning)
        {
            station = owning;
            return true;
        }

        foreach (var candidate in _station.GetStations())
        {
            if (!EntityManager.HasComponent<AlertLevelComponent>(candidate))
                continue;

            station = candidate;
            return true;
        }

        shell.WriteError(Loc.GetString("cmd-dutycodealpha-no-station"));
        return false;
    }
}
