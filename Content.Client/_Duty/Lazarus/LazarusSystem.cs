using Content.Client.Audio;
using Content.Shared._Duty.Lazarus;
using Content.Shared.Mobs.Systems;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Lazarus;

/// <summary>
/// Клиентская часть эффекта Лазаруса. Ловит <see cref="LazarusTriggeredEvent"/> от
/// сервера (приходит только тому, у кого сработало), запускает полноэкранную
/// кинематику (<see cref="LazarusOverlay"/> + <see cref="LazarusVignetteOverlay"/>)
/// и проигрывает звуки. По завершении анимации оверлеи снимаются.
///
/// <see cref="LazarusCancelledEvent"/> обрывает сцену: персонаж умер или его вытащили
/// из крита раньше, чем сработало «вставание».
/// </summary>
public sealed partial class LazarusSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IResourceCache _cache = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ContentAudioSystem _contentAudio = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    /// <summary>За сколько гаснет сцена при обрыве — быстро, но не рывком.</summary>
    private const float AbortFadeSeconds = 0.4f;

    /// <summary>За сколько уводятся звуки сцены при обрыве.</summary>
    private const float AudioFadeSeconds = 1.2f;

    private LazarusOverlay? _black;
    private LazarusVignetteOverlay? _vignette;

    /// <summary>Множитель непрозрачности сцены: 1 в норме, едет к 0 при обрыве.</summary>
    private float _fade = 1f;
    private bool _aborting;
    private bool _activeForced;

    // Живые звуки сцены. Держим ссылки, чтобы гасить их при обрыве и чтобы
    // DynamicAmbientMusicSystem знал, что сцена ещё звучит.
    private EntityUid? _heartbeatStream;
    private EntityUid? _lastStandStream;

    // Отложенное вступление основного звука (наезжает на сердцебиение).
    private SoundSpecifier? _pendingLastStand;
    private float _pendingLastStandVolume;
    private TimeSpan _lastStandTime;

    /// <summary>
    /// Идёт ли сейчас сцена «второй жизни» у локального игрока — кинематика или её звуки.
    /// Читает <c>DynamicAmbientMusicSystem</c>, чтобы не наваливать крит-музыку поверх
    /// Last Standing: пульс на время сцены глушится специально, а фоновая музыка раньше
    /// продолжала играть третьим слоем.
    /// </summary>
    public bool SceneActive => _black != null
                               || _pendingLastStand != null
                               || IsStreamAlive(_heartbeatStream)
                               || IsStreamAlive(_lastStandStream);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<LazarusTriggeredEvent>(OnTriggered);
        SubscribeNetworkEvent<LazarusCancelledEvent>(OnCancelled);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        StopScene(fadeAudio: false);
    }

    private void OnTriggered(LazarusTriggeredEvent ev)
    {
        // Если событие не принудительное (duty_lazarus_test), а игрок на клиенте уже не в крите
        // (например, админ вылечил через Rejuvenate или вытащили за тот же тик), сцену не запускаем.
        if (!ev.Forced && (_player.LocalEntity is not { } local || !_mobState.IsCritical(local)))
            return;

        // Перекрытие предыдущей кинематики (на всякий случай) — начинаем заново.
        StopScene(fadeAudio: false);

        _fade = 1f;
        _aborting = false;
        _activeForced = ev.Forced;

        _black = new LazarusOverlay(
            _clyde,
            _timing,
            _cache,
            PickPhrase(),
            ev.BlackoutFadeIn,
            ev.BlackoutHold,
            ev.BlackoutFadeOut);

        // Виньетка проявляется ровно тогда, когда начинает отступать чернота.
        _vignette = new LazarusVignetteOverlay(
            _timing,
            appearStart: ev.BlackoutFadeIn + ev.BlackoutHold,
            appearDuration: ev.BlackoutFadeOut,
            hold: ev.VignetteDuration,
            fadeOut: ev.VignetteFadeOut);

        _overlay.AddOverlay(_black);
        _overlay.AddOverlay(_vignette);

        // Сердцебиение — сразу (подводка на затемнении).
        if (ev.Heartbeat != null)
            _heartbeatStream = _audio.PlayGlobal(ev.Heartbeat, Filter.Local(), false,
                AudioParams.Default.WithVolume(ev.HeartbeatVolume))?.Entity;

        // Основной звук — с задержкой, чтобы наехать на сердцебиение.
        _pendingLastStand = ev.LastStand;
        _pendingLastStandVolume = ev.LastStandVolume;
        _lastStandTime = _timing.RealTime + TimeSpan.FromSeconds(MathF.Max(ev.LastStandDelay, 0f));
    }

    /// <summary>
    /// Сцена оборвана сервером. Доигрывать её над трупом или над уже поднятым бойцом
    /// незачем: гасим оверлеи и уводим звуки (при Immediate — гасим мгновенно).
    /// </summary>
    private void OnCancelled(LazarusCancelledEvent ev)
    {
        _pendingLastStand = null;
        _activeForced = false;

        if (ev.Immediate || (_black == null && _vignette == null))
        {
            StopScene(fadeAudio: !ev.Immediate);
            return;
        }

        _aborting = true;
        FadeOutAudio();
    }

    // FrameUpdate, а не Update: клиентский Update при повторной симуляции зовётся по нескольку раз
    // за тик, и затухание при обрыве шло бы тем быстрее, чем выше пинг.
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // Игрок отцепился от тела (гиб, уход в призрака, рестарт раунда) — сцена больше
        // ни к чему не привязана, а трек Last Standing длиннее её самой.
        if (_player.LocalEntity is not { } local)
        {
            if (SceneActive)
                StopScene(fadeAudio: false);
            return;
        }

        if (_pendingLastStand != null)
        {
            if (_aborting || (!_activeForced && !_mobState.IsCritical(local)))
            {
                _pendingLastStand = null;
            }
            else if (_timing.RealTime >= _lastStandTime)
            {
                _lastStandStream = _audio.PlayGlobal(_pendingLastStand, Filter.Local(), false,
                    AudioParams.Default.WithVolume(_pendingLastStandVolume))?.Entity;
                _pendingLastStand = null;
            }
        }

        if (_aborting && _fade > 0f)
        {
            _fade = MathF.Max(0f, _fade - frameTime / AbortFadeSeconds);

            if (_black != null)
                _black.Fade = _fade;
            if (_vignette != null)
                _vignette.Fade = _fade;

            if (_fade <= 0f)
                ClearOverlays();

            return;
        }

        if (_black is { Finished: true })
        {
            _overlay.RemoveOverlay(_black);
            _black = null;
        }

        if (_vignette is { Finished: true })
        {
            _overlay.RemoveOverlay(_vignette);
            _vignette = null;
        }
    }

    /// <summary>Снимает оверлеи и глушит звуки — без плавного обрыва.</summary>
    private void StopScene(bool fadeAudio)
    {
        ClearOverlays();
        _pendingLastStand = null;
        _activeForced = false;

        if (fadeAudio)
            FadeOutAudio();
        else
            StopAudio();
    }

    private void ClearOverlays()
    {
        if (_black != null)
        {
            _overlay.RemoveOverlay(_black);
            _black = null;
        }

        if (_vignette != null)
        {
            _overlay.RemoveOverlay(_vignette);
            _vignette = null;
        }

        _aborting = false;
        _fade = 1f;
    }

    private void FadeOutAudio()
    {
        FadeOutStream(ref _heartbeatStream);
        FadeOutStream(ref _lastStandStream);
    }

    private void FadeOutStream(ref EntityUid? stream)
    {
        if (IsStreamAlive(stream))
            _contentAudio.FadeOut(stream, duration: AudioFadeSeconds);

        stream = null;
    }

    private void StopAudio()
    {
        StopStream(ref _heartbeatStream);
        StopStream(ref _lastStandStream);
    }

    private void StopStream(ref EntityUid? stream)
    {
        if (IsStreamAlive(stream))
            _audio.Stop(stream);

        stream = null;
    }

    /// <summary>
    /// Одноразовые звуки доигрывают и удаляются сами, а ссылка на них остаётся. Дёргать
    /// <c>FadeOut</c>/<c>Stop</c> по мёртвой ссылке — ошибка резолва в лог со стектрейсом.
    /// </summary>
    private bool IsStreamAlive(EntityUid? stream)
    {
        return stream is { } uid && !TerminatingOrDeleted(uid) && HasComp<AudioComponent>(uid);
    }

    /// <summary>Случайная фраза из локали duty-lazarus-phrase-1..N.</summary>
    private string PickPhrase()
    {
        var variants = new List<string>();
        var i = 1;
        while (Loc.TryGetString($"duty-lazarus-phrase-{i}", out var phrase))
        {
            variants.Add(phrase);
            i++;
        }

        return variants.Count > 0 ? _random.Pick(variants) : string.Empty;
    }
}
