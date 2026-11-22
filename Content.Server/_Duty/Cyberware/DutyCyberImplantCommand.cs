// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared._Duty.Cyberware;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Implants;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._Duty.Cyberware;

/// <summary>
/// _Duty: админская выдача кибера без операции — для ивентов и проверки.
/// Обычный путь в игре — операционный набор; команда нужна, когда хирурга нет или некогда.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class DutyCyberImplantCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedSubdermalImplantSystem _implants = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    public override string Command => "dutycyberimplant";

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHint(Loc.GetString("cmd-dutycyberimplant-hint-target")),
            2 => CompletionResult.FromHintOptions(CyberPrototypes(), Loc.GetString("cmd-dutycyberimplant-hint-proto")),
            _ => CompletionResult.Empty,
        };
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        EntityUid target;
        if (args[0] == "self")
        {
            if (shell.Player?.AttachedEntity is not { } self)
            {
                shell.WriteError(Loc.GetString("shell-must-be-attached-to-entity"));
                return;
            }

            target = self;
        }
        else if (!NetEntity.TryParse(args[0], out var net) || !EntityManager.TryGetEntity(net, out var parsed))
        {
            shell.WriteError(Loc.GetString("shell-invalid-entity-id"));
            return;
        }
        else
        {
            target = parsed.Value;
        }

        if (!_proto.TryIndex<EntityPrototype>(args[1], out var proto) || !proto.TryGetComponent<DutyCyberwareComponent>(out _))
        {
            shell.WriteError(Loc.GetString("cmd-dutycyberimplant-bad-proto", ("proto", args[1])));
            return;
        }

        if (_implants.AddImplant(target, proto.ID) is not { } implant)
        {
            shell.WriteError(Loc.GetString("cmd-dutycyberimplant-failed"));
            return;
        }

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{shell.Player?.Name ?? "server"} вживил кибер {EntityManager.ToPrettyString(implant)} в {EntityManager.ToPrettyString(target)} командой");
        shell.WriteLine(Loc.GetString("cmd-dutycyberimplant-done", ("proto", proto.ID)));
    }

    private IEnumerable<string> CyberPrototypes()
    {
        return _proto.EnumeratePrototypes<EntityPrototype>()
            .Where(p => !p.Abstract && p.TryGetComponent<DutyCyberwareComponent>(out _))
            .Select(p => p.ID)
            .OrderBy(id => id);
    }
}
