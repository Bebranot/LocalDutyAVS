// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.GameTicking;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._Duty.Cyberware.Mercenary;

/// <summary>
/// _Duty: <c>contract &lt;ckey&gt;</c> — заказать персонажа игрока наёмнику.
/// В космосе у станции цели появляется гост-роль «Наёмник»; тело создаётся, только когда роль кто-то взял (как у ниндзя).
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class DutyContractCommand : LocalizedEntityCommands
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private DutyMercenaryRuleSystem _rule = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    private static readonly EntProtoId RuleProto = "DutyMercenaryContract";

    public override string Command => "contract";

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        // Только те, кого реально можно заказать: подключены и в живом теле.
        var options = _players.Sessions
            .Where(s => s.AttachedEntity is { } ent
                        && EntityManager.HasComponent<MobStateComponent>(ent)
                        && !_mobState.IsDead(ent))
            .Select(s => s.Name)
            .OrderBy(n => n);

        return CompletionResult.FromHintOptions(options, Loc.GetString("cmd-contract-hint"));
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (_ticker.RunLevel != GameRunLevel.InRound)
        {
            shell.WriteError(Loc.GetString("cmd-contract-not-in-round"));
            return;
        }

        if (!_players.TryGetSessionByUsername(args[0], out var session))
        {
            shell.WriteError(Loc.GetString("cmd-contract-no-player", ("ckey", args[0])));
            return;
        }

        if (session.AttachedEntity is not { } body
            || !EntityManager.HasComponent<MobStateComponent>(body)
            || !_mind.TryGetMind(session, out var mindId, out var mind)
            || mind.OwnedEntity != body)
        {
            shell.WriteError(Loc.GetString("cmd-contract-no-body", ("ckey", session.Name)));
            return;
        }

        if (_mobState.IsDead(body))
        {
            shell.WriteError(Loc.GetString("cmd-contract-dead", ("ckey", session.Name)));
            return;
        }

        var targetName = mind.CharacterName ?? EntityManager.GetComponent<MetaDataComponent>(body).EntityName;

        var rule = _ticker.AddGameRule(RuleProto);
        _rule.SetupContract(rule, mindId, body, targetName);

        if (!_ticker.StartGameRule(rule))
        {
            shell.WriteError(Loc.GetString("cmd-contract-failed"));
            return;
        }

        _adminLog.Add(LogType.EventStarted, LogImpact.High,
            $"{shell.Player?.Name ?? "server"} заказал наёмнику {targetName} ({session.Name}, {EntityManager.ToPrettyString(body)})");
        shell.WriteLine(Loc.GetString("cmd-contract-done", ("target", targetName), ("ckey", session.Name)));
    }
}
