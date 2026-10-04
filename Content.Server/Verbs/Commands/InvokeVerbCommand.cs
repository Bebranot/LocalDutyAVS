using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Verbs;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server.Verbs.Commands
{
    [AdminCommand(AdminFlags.Admin)]
    public sealed class InvokeVerbCommand : IConsoleCommand
    {
        [Dependency] private readonly IEntityManager _entManager = default!;
        [Dependency] private readonly IPlayerManager _players = default!; // _Duty
        [Dependency] private readonly IAdminManager _admins = default!; // _Duty

        public string Command => "invokeverb";
        public string Description => Loc.GetString("invoke-verb-command-description");
        public string Help => Loc.GetString("invoke-verb-command-help");

        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length != 3)
            {
                shell.WriteLine(Loc.GetString("invoke-verb-command-invalid-args"));
                return;
            }

            var verbSystem = _entManager.System<SharedVerbSystem>();

            // get the 'player' entity (defaulting to command user, otherwise uses a uid)
            EntityUid? playerEntity = null;
            if (!int.TryParse(args[0], out var intPlayerUid))
            {
                if (args[0] == "self" && shell.Player?.AttachedEntity != null)
                {
                    playerEntity = shell.Player.AttachedEntity.Value;
                }
                else
                {
                    shell.WriteError(Loc.GetString("invoke-verb-command-invalid-player-uid"));
                    return;
                }
            }
            else
            {
                _entManager.TryGetEntity(new NetEntity(intPlayerUid), out playerEntity);
            }

            // gets the target entity
            if (!int.TryParse(args[1], out var intUid))
            {
                shell.WriteError(Loc.GetString("invoke-verb-command-invalid-target-uid"));
                return;
            }

            if (playerEntity == null)
            {
                shell.WriteError(Loc.GetString("invoke-verb-command-invalid-player-entity"));
                return;
            }

            // _Duty: нельзя вызывать вербы (с forced) от имени админа, у которого прав больше, чем у тебя
            if (shell.Player is { } invoker
                && _players.TryGetSessionByEntity(playerEntity.Value, out var runnerSession)
                && _admins.IsOutranked(invoker, runnerSession))
            {
                shell.WriteError(Loc.GetString("duty-verb-run-as-outranked"));
                return;
            }

            var targetNet = new NetEntity(intUid);

            if (!_entManager.TryGetEntity(targetNet, out var target))
            {
                shell.WriteError(Loc.GetString("invoke-verb-command-invalid-target-entity"));
                return;
            }

            var verbName = args[2].ToLowerInvariant();
            var verbs = verbSystem.GetLocalVerbs(target.Value, playerEntity.Value, Verb.VerbTypes, true);

            // if the "verb name" is actually a verb-type, try run any verb of that type.
            var verbType = Verb.VerbTypes.FirstOrDefault(x => x.Name == verbName);
            if (verbType != null)
            {
                var verb = verbs.FirstOrDefault(v => v.GetType() == verbType);
                if (verb != null)
                {
                    verbSystem.ExecuteVerb(verb, playerEntity.Value, target.Value, forced: true);
                    shell.WriteLine(Loc.GetString("invoke-verb-command-success", ("verb", verbName), ("target", target), ("player", playerEntity)));
                    return;
                }
            }

            foreach (var verb in verbs)
            {
                if (verb.Text.ToLowerInvariant() == verbName)
                {
                    verbSystem.ExecuteVerb(verb, playerEntity.Value, target.Value, forced: true);
                    shell.WriteLine(Loc.GetString("invoke-verb-command-success", ("verb", verb.Text), ("target", target), ("player", playerEntity)));
                    return;
                }
            }

            // found nothing
            shell.WriteError(Loc.GetString("invoke-verb-command-verb-not-found", ("verb", verbName), ("target", target)));
        }
    }
}
