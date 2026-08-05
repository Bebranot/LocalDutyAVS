// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Administration;
using Content.Shared._Duty.Parry;
using Content.Shared._Duty.Parry.Components;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Duty.Parry.Commands;

/// <summary>
/// _Duty (тест): <c>dutyduel &lt;цель&gt;</c> — запускает QTE-дуэль между вызвавшим и целью,
/// минуя обычный вход (блок → наказание → парирование → контратака). Нужна, чтобы проверять
/// саму катсцену в одиночку: цель может быть манекеном, она просто провалит своё QTE.
///
/// Внимание: цепочку входа команда НЕ проверяет — для неё нужен второй живой игрок.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class DutyDuelCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    public string Command => "dutyduel";
    public string Description => Loc.GetString("duty-parry-debug-duel-description");
    public string Help => Loc.GetString("duty-parry-debug-duel-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player?.AttachedEntity is not { Valid: true } player)
        {
            shell.WriteLine(Loc.GetString("duty-parry-debug-error-no-player"));
            return;
        }

        if (args.Length < 1 || string.IsNullOrEmpty(args[0]))
        {
            shell.WriteLine(Loc.GetString("duty-parry-debug-error-need-target"));
            return;
        }

        if (!_entMan.TryParseNetEntity(args[0], out var parsed) || !_entMan.EntityExists(parsed))
        {
            shell.WriteLine(Loc.GetString("duty-parry-debug-error-target", ("arg", args[0])));
            return;
        }

        var target = parsed.Value;

        if (target == player)
        {
            shell.WriteLine(Loc.GetString("duty-parry-debug-error-self"));
            return;
        }

        // Та же защита, что и в обычном входе: повторно затащить в дуэль уже участвующего нельзя.
        if (_entMan.HasComponent<QteParticipantComponent>(player) ||
            _entMan.HasComponent<QteParticipantComponent>(target))
        {
            shell.WriteLine(Loc.GetString("duty-parry-debug-error-busy"));
            return;
        }

        // Вызвавший встаёт стороной парировавшего — как если бы он честно поймал контратаку.
        var ev = new QteDuelStartRequestEvent(target, player);
        _entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);

        shell.WriteLine(Loc.GetString("duty-parry-debug-duel-started", ("target", _entMan.ToPrettyString(target))));
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) => args.Length switch
    {
        1 => CompletionResult.FromHint(Loc.GetString("duty-parry-debug-hint-target")),
        _ => CompletionResult.Empty,
    };
}
