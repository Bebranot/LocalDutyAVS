using Content.Server.Database;
using Content.Server.EUI;
using Content.Server.GameTicking;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared._Duty.Welcome;
using Content.Shared.CCVar;
using Content.Shared.Players;
using Content.Shared.Players.PlayTimeTracking;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._Duty.Welcome;

/// <summary>
/// _Duty: открывает окно приветствия/чейнджлога новым и вернувшимся после рестарта игрокам.
///
/// Правила показа:
/// - игрок с суммарным наигранным временем меньше <see cref="DutyWelcomeShared.NewPlayerPlaytimeThreshold"/>
///   считается новичком и видит окно при каждом входе в лобби, пока не отметит «не показывать снова»;
/// - опытный игрок видит окно один раз за запуск сервера (флаг живёт в <see cref="ContentPlayerData"/>,
///   который сбрасывается только рестартом процесса — см. <see cref="Robust.Shared.Player.SessionData"/>);
/// - галочка «не показывать снова» подавляет показ до следующего рестарта независимо от критерия выше;
/// - вся фича целиком выключается CVar'ом <see cref="DutyCCVars.WelcomeEnabled"/>.
/// </summary>
public sealed class DutyWelcomeSystem : EntitySystem
{
    [Dependency] private readonly EuiManager _euiManager = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playTime = default!;
    [Dependency] private readonly UserDbDataManager _userDb = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);
    }

    private async void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        var session = ev.PlayerSession;

        try
        {
            // Дожидаемся окончания загрузки данных игрока из БД (в т.ч. наигранного времени) —
            // на момент SessionStatus.InGame она может быть ещё не завершена.
            await _userDb.WaitLoadComplete(session);
        }
        catch (OperationCanceledException)
        {
            // Игрок отключился или загрузка отменена — открывать окно некому.
            return;
        }

        if (session.Status == SessionStatus.Disconnected)
            return;

        TryShowWelcome(session);
    }

    private void TryShowWelcome(ICommonSession session)
    {
        if (!_cfg.GetCVar(DutyCCVars.WelcomeEnabled))
            return;

        var data = session.ContentData();
        if (data == null || data.DutyWelcomeDismissed)
            return;

        var isNewPlayer = !_playTime.TryGetTrackerTime(session, PlayTimeTrackingShared.TrackerOverall, out var playtime)
            || playtime < DutyWelcomeShared.NewPlayerPlaytimeThreshold;

        // Опытный игрок уже видел окно в этом запуске сервера — не спамим на каждый вход в лобби.
        if (!isNewPlayer && data.DutyWelcomeShown)
            return;

        data.DutyWelcomeShown = true;

        _euiManager.OpenEui(new DutyWelcomeEui(isNewPlayer), session);
    }
}
