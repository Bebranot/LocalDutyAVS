// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Login;

/// <summary>
/// Звук при входе в игру.
///
/// <see cref="TickerJoinGameEvent"/> приходит в самом начале подключения, когда клиент ещё
/// докачивает и разворачивает карту. Звук в движке синхронизирован с игровым временем: если
/// запустить его во время загрузочных фризов, после фриза источник перематывается вперёд —
/// и игрок слышит только хвост. Поэтому ждём, пока у игрока появится тело/призрак и кадры
/// некоторое время подряд идут без подвисаний, и только тогда играем.
/// </summary>
public sealed class LoginSoundSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private static readonly SoundPathSpecifier LoginSound = new("/Audio/_Duty/UI/login.ogg");

    /// <summary>Сколько подряд кадры должны идти ровно, чтобы считать загрузку законченной.</summary>
    private static readonly TimeSpan SmoothPeriod = TimeSpan.FromSeconds(1.5);

    /// <summary>Кадр длиннее этого — клиент ещё грузится (разворачивает сущности, текстуры).</summary>
    private static readonly TimeSpan HitchFrameTime = TimeSpan.FromSeconds(0.1);

    /// <summary>Страховка на слабых машинах, где мелкие фризы не прекращаются: играем всё равно.</summary>
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(10);

    private bool _played;
    private bool _pending;
    private TimeSpan _pendingSince;
    private TimeSpan _smoothSince;
    private TimeSpan _lastFrame;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<TickerJoinGameEvent>(OnJoinGame);
    }

    private void OnJoinGame(TickerJoinGameEvent ev)
    {
        if (_played || _pending)
            return;

        if (!_cfg.GetCVar(DutyCCVars.LoginSoundEnabled))
        {
            _played = true;
            return;
        }

        var now = _timing.RealTime;
        _pending = true;
        _pendingSince = now;
        _smoothSince = now;
        _lastFrame = now;
    }

    // FrameUpdate, а не Update: клиентский Update при re-prediction зовётся по нескольку раз за тик,
    // а здесь нужно мерить реальное время между кадрами.
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!_pending)
            return;

        var now = _timing.RealTime;
        var frameDelta = now - _lastFrame;
        _lastFrame = now;

        if (now - _pendingSince >= MaxWait)
        {
            Play();
            return;
        }

        // Пока нет своей сущности, карта ещё не применена; фриз — загрузка продолжается.
        if (_player.LocalEntity == null || frameDelta > HitchFrameTime)
        {
            _smoothSince = now;
            return;
        }

        if (now - _smoothSince >= SmoothPeriod)
            Play();
    }

    private void Play()
    {
        _pending = false;
        _played = true;
        _audio.PlayGlobal(LoginSound, Filter.Local(), false, AudioParams.Default);
    }
}
