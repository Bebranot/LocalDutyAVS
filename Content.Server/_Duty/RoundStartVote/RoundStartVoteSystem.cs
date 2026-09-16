using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Voting;
using Content.Server.Voting.Managers;
using Content.Shared._Duty.RoundStartVote;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Duty.RoundStartVote;

/// <summary>
/// _Duty: фреймворк голосований "да/нет" на старте раунда с игровым эффектом при победе.
/// Переиспользует ванильный <see cref="IVoteManager"/> (ту же голосовалку, что и ресерт/пресет/карта)
/// вместо своего UI — на старте раунда случайно выбирается один включённый
/// <see cref="RoundStartVoteEventPrototype"/> и запускается как обычный серверный голос.
/// При победе "Да" рассылается <see cref="RoundStartVoteEffectAppliedEvent"/> с EffectId
/// прототипа — конкретный эффект (например, дебаг-заряд СМЭС) реализует отдельная система.
/// </summary>
public sealed class RoundStartVoteSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IVoteManager _voteManager = default!;

    private const string YesOption = "yes";
    private const string NoOption = "no";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
    }

    private void OnRoundStarted(RoundStartedEvent ev)
    {
        if (!_cfg.GetCVar(DutyCCVars.RoundStartVoteEnabled))
            return;

        var candidates = _proto.EnumeratePrototypes<RoundStartVoteEventPrototype>()
            .Where(p => p.Enabled)
            .ToList();

        if (candidates.Count == 0)
            return;

        StartVote(_random.Pick(candidates));
    }

    private void StartVote(RoundStartVoteEventPrototype proto)
    {
        _chat.DispatchServerAnnouncement(Loc.GetString(proto.Description));

        var options = new VoteOptions
        {
            Title = Loc.GetString(proto.Question),
            Duration = proto.Duration,
            Options =
            {
                (Loc.GetString("duty-round-start-vote-yes"), YesOption),
                (Loc.GetString("duty-round-start-vote-no"), NoOption),
            },
        };
        options.SetInitiatorOrServer(null);

        var vote = _voteManager.CreateVote(options);
        vote.OnFinished += (_, _) => OnVoteFinished(vote, proto);
    }

    private void OnVoteFinished(IVoteHandle vote, RoundStartVoteEventPrototype proto)
    {
        var votesYes = vote.VotesPerOption.GetValueOrDefault(YesOption, 0);
        var votesNo = vote.VotesPerOption.GetValueOrDefault(NoOption, 0);

        if (votesYes <= votesNo)
        {
            _adminLogger.Add(LogType.Vote, LogImpact.Low,
                $"Round-start vote '{proto.ID}' failed: {votesYes}/{votesNo}");
            return;
        }

        _adminLogger.Add(LogType.Vote, LogImpact.Medium,
            $"Round-start vote '{proto.ID}' succeeded: {votesYes}/{votesNo}, applying effect '{proto.EffectId}'");

        RaiseLocalEvent(new RoundStartVoteEffectAppliedEvent(proto.EffectId));
    }
}
