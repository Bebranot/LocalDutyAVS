using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Verbs;
using Robust.Server.Player;
using Robust.Shared.Toolshed;
using Robust.Shared.Toolshed.Syntax;
using Robust.Shared.Toolshed.TypeParsers;

namespace Content.Server.Toolshed.Commands.Verbs;

[ToolshedCommand, AdminCommand(AdminFlags.Moderator)]
public sealed class RunVerbAsCommand : ToolshedCommand
{
    [Dependency] private readonly IPlayerManager _players = default!; // _Duty
    [Dependency] private readonly IAdminManager _admins = default!; // _Duty

    private SharedVerbSystem? _verb;

    [CommandImplementation]
    public IEnumerable<EntityUid> RunVerbAs(
            IInvocationContext ctx,
            [PipedArgument] IEnumerable<EntityUid> input,
            EntityUid runner,
            string verb
        )
    {
        _verb ??= GetSys<SharedVerbSystem>();
        verb = verb.ToLowerInvariant();

        // _Duty: нельзя запускать вербы от имени админа, у которого прав больше, чем у тебя
        if (ctx.Session is { } invoker
            && _players.TryGetSessionByEntity(runner, out var runnerSession)
            && _admins.IsOutranked(invoker, runnerSession))
        {
            ctx.ReportError(new DutyVerbOutrankedError());
            yield break;
        }

        foreach (var eId in input)
        {
            if (EntityManager.Deleted(runner) && runner.IsValid())
                ctx.ReportError(new DeadEntity(runner));

            if (ctx.GetErrors().Any())
                yield break;

            var verbs = _verb.GetLocalVerbs(eId, runner, Verb.VerbTypes, true);

            // if the "verb name" is actually a verb-type, try run any verb of that type.
            var verbType = Verb.VerbTypes.FirstOrDefault(x => x.Name == verb);
            if (verbType != null)
            {
                var verbTy = verbs.FirstOrDefault(v => v.GetType() == verbType);
                if (verbTy != null)
                {
                    _verb.ExecuteVerb(verbTy, runner, eId, forced: true);
                    yield return eId;
                }
            }

            foreach (var verbTy in verbs)
            {
                if (verbTy.Text.ToLowerInvariant() == verb)
                {
                    _verb.ExecuteVerb(verbTy, runner, eId, forced: true);
                    yield return eId;
                }
            }
        }
    }
}

// _Duty: ошибка Toolshed, когда runverbas пытаются выполнить от имени админа с большими правами
public record struct DutyVerbOutrankedError : Robust.Shared.Toolshed.Errors.IConError
{
    public Robust.Shared.Utility.FormattedMessage DescribeInner()
    {
        return Robust.Shared.Utility.FormattedMessage.FromUnformatted(Loc.GetString("duty-verb-run-as-outranked"));
    }

    public string? Expression { get; set; }
    public Robust.Shared.Maths.Vector2i? IssueSpan { get; set; }
    public System.Diagnostics.StackTrace? Trace { get; set; }
}
