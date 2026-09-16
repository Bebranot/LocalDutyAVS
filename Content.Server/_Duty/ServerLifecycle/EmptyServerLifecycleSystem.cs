using System.Threading;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server._Duty.ServerLifecycle;

/// <summary>
/// _Duty: когда на сервере не остаётся ни одного подключённого игрока, карты текущего раунда
/// сразу ставятся на паузу (не тикают вхолостую). Если за настраиваемое время (duty CVar)
/// так никто и не подключится — раунд принудительно завершается и сервер уходит в лобби.
/// Подключение любого игрока в любой момент отменяет отсчёт и штатно снимает паузу.
/// </summary>
public sealed class EmptyServerLifecycleSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;

    private CancellationTokenSource _restartTimerCancel = new();
    private bool _timerActive;
    private bool _mapsPausedForEmptyServer;

    public override void Initialize()
    {
        base.Initialize();

        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
        _restartTimerCancel.Cancel();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        CheckPlayerCount();
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent args)
    {
        // Раунд мог быть запущен вручную (админ-команда, тест) при уже пустом сервере —
        // в этом случае отсчёт/пауза иначе никогда бы не включились, т.к. подключений не было.
        if (args.New == GameRunLevel.InRound)
            CheckPlayerCount();
    }

    private void CheckPlayerCount()
    {
        if (!_cfg.GetCVar(DutyCCVars.EmptyServerAutoRestartEnabled))
            return;

        if (_playerManager.PlayerCount == 0)
            OnServerBecameEmpty();
        else
            OnServerNoLongerEmpty();
    }

    private void OnServerBecameEmpty()
    {
        if (_timerActive)
            return;

        _timerActive = true;
        _restartTimerCancel = new CancellationTokenSource();

        var delay = TimeSpan.FromSeconds(_cfg.GetCVar(DutyCCVars.EmptyServerAutoRestartDelaySeconds));

        if (_gameTicker.RunLevel == GameRunLevel.InRound)
        {
            SetMapsPaused(true);
            Log.Info($"_Duty: сервер опустел, раунд поставлен на паузу. Если за {delay.TotalMinutes:0.#} мин. никто не подключится — раунд будет принудительно завершён и сервер уйдёт в лобби.");
        }
        else
        {
            Log.Info($"_Duty: сервер опустел (в лобби, активного раунда нет). Через {delay.TotalMinutes:0.#} мин. будет выполнена проверка на рестарт.");
        }

        Timer.Spawn(delay, OnEmptyTimerFired, _restartTimerCancel.Token);
    }

    private void OnServerNoLongerEmpty()
    {
        if (!_timerActive)
            return;

        _timerActive = false;
        _restartTimerCancel.Cancel();

        if (_mapsPausedForEmptyServer)
        {
            SetMapsPaused(false);
            Log.Info("_Duty: на сервер подключился игрок — раунд снят с паузы, отсчёт до принудительного рестарта отменён.");
        }
    }

    private void OnEmptyTimerFired()
    {
        _timerActive = false;

        // Подстраховка: таймер мог сработать в узком окне гонки с уже обработанным подключением.
        if (_playerManager.PlayerCount != 0)
            return;

        if (_gameTicker.RunLevel != GameRunLevel.InRound)
            return;

        Log.Info("_Duty: сервер оставался пуст всё отведённое время — принудительно завершаем раунд и уходим в лобби.");

        SetMapsPaused(false);
        _gameTicker.RestartRound();
    }

    private void SetMapsPaused(bool paused)
    {
        if (_mapsPausedForEmptyServer == paused)
            return;

        _mapsPausedForEmptyServer = paused;

        foreach (var mapId in _mapManager.GetAllMapIds())
        {
            if (mapId == MapId.Nullspace)
                continue;

            _mapManager.SetMapPaused(mapId, paused);
        }
    }
}
