using Content.Shared._Duty.HealthAnalyzer;
using Content.Shared._Duty.Heartbeat;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Client._Duty.HealthAnalyzer;

/// <summary>
/// _Duty: сердцебиение сканируемой цели в анализаторе здоровья. Слышит ТОЛЬКО сканирующий
/// (Filter.Local, не позиционно). Модель воспроизведения:
///  • вне крита — одиночный сэмпл кардиомонитора (heartthump-heavy-monitor.ogg) по таймеру,
///    интервал ≥ длины сэмпла (без нахлёста), ниже HP → чаще;
///  • в крите — ВМЕСТО ударов зацикленный критический тон (criticalloop.ogg): плавно
///    появляется (fade-in) и РЕЗКО обрывается при выходе из крита; на 0.5 дБ громче обычного;
///  • на грани смерти (HP &lt; ~10%) — зацикленная тревога панели (loop);
///  • при смерти цели — один раз flatline.ogg.
/// Все цикличные потоки явно останавливаются при выходе из анализатора / состояния / смерти.
/// </summary>
public sealed class HealthAnalyzerAudioSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private readonly SoundSpecifier _beat = new SoundCollectionSpecifier("DutyHeartbeatMonitor");
    private readonly SoundSpecifier _critLoop = new SoundCollectionSpecifier("DutyHeartbeatCriticalLoop");
    private readonly SoundSpecifier _alert = new SoundCollectionSpecifier("DutyHeartbeatPanelAlert");
    private readonly SoundSpecifier _flatline = new SoundCollectionSpecifier("DutyHeartbeatFlatline");

    // Интервалы ударов вне крита (сек). Не короче длины сэмпла монитора (≈ 0.77с).
    private const float NoneInterval = 1.6f;
    private const float LightInterval = 1.5f;
    private const float HeavyInterval = 1.2f;

    private const float BeatVolume = -4f;
    private const float AlertVolume = -6f;
    private const float FlatlineVolume = -3f;

    // Критический тон: плавный fade-in от «тишины» к цели (на 0.5 дБ громче удара).
    private const float CritStartVolume = -32f;
    private const float CritTargetVolume = BeatVolume + 0.5f;
    private const float CritFadeIn = 1.2f;

    private bool _active;
    private HeartbeatLevel _level;
    private bool _inCrit;
    private bool _nearDeath;
    private bool _flatlineState;
    private bool _wasFlatline;

    private float _beatAccum;
    private float _critFade;

    // Цикличные управляемые потоки (их нужно явно останавливать).
    private EntityUid? _critStream;
    private EntityUid? _alertStream;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<HealthAnalyzerAudioEvent>(OnAudioEvent);
        SubscribeNetworkEvent<HealthAnalyzerStopAudioEvent>(OnStopAudio);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        StopLoops();
    }

    private void OnAudioEvent(HealthAnalyzerAudioEvent ev)
    {
        _active = true;
        _level = ev.Level;
        _inCrit = ev.InCrit;
        _nearDeath = ev.NearDeath;
        _flatlineState = ev.Flatline;

        if (ev.ForceRestart)
        {
            _beatAccum = float.MaxValue; // ударить сразу на открытии сканирования

            // flatline играем ТОЛЬКО когда смерть происходит при активном просмотре.
            // Если на открытии скана цель уже мертва — считаем звук уже «сыгранным»,
            // чтобы он не повторялся при каждом повторном анализе трупа.
            _wasFlatline = ev.Flatline;
        }
    }

    private void OnStopAudio(HealthAnalyzerStopAudioEvent ev)
    {
        _active = false;
        _wasFlatline = false;
        StopLoops();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // Сканирование закрыто — молчим и глушим циклы.
        if (!_active)
        {
            StopLoops();
            return;
        }

        // Цель мертва — глушим циклы, один раз играем ровную линию.
        if (_flatlineState)
        {
            StopLoops();
            if (!_wasFlatline)
                Play(_flatline, FlatlineVolume);
            _wasFlatline = true;
            return;
        }
        _wasFlatline = false;

        // Тревога панели на грани смерти.
        UpdateLoop(ref _alertStream, _nearDeath, _alert, AlertVolume);

        // В крите: критический тон ВМЕСТО ударов — плавный fade-in, резкий стоп на выходе.
        if (_inCrit)
        {
            TickCritLoop(frameTime);
            _beatAccum = 0f;
            return;
        }

        StopCritLoop(); // вышел из крита — обрываем тон резко

        // Вне крита — обычные удары кардиомонитора по HP.
        var interval = _level switch
        {
            HeartbeatLevel.Heavy or HeartbeatLevel.Critical => HeavyInterval,
            HeartbeatLevel.Light => LightInterval,
            _ => NoneInterval,
        };

        _beatAccum += frameTime;
        if (_beatAccum >= interval)
        {
            _beatAccum = 0f;
            Play(_beat, BeatVolume);
        }
    }

    /// <summary>Стартует/тянет fade-in зацикленного критического тона.</summary>
    private void TickCritLoop(float frameTime)
    {
        if (_critStream == null || !Exists(_critStream.Value))
        {
            _critFade = 0f;
            _critStream = _audio.PlayGlobal(
                _critLoop,
                Filter.Local(),
                false,
                AudioParams.Default.WithLoop(true).WithVolume(CritStartVolume))?.Entity;
            return;
        }

        _critFade += frameTime;
        var t = Math.Clamp(_critFade / CritFadeIn, 0f, 1f);
        var vol = float.Lerp(CritStartVolume, CritTargetVolume, t);

        if (TryComp<AudioComponent>(_critStream, out var audio))
            _audio.SetVolume(_critStream, vol, audio);
    }

    private void StopCritLoop()
    {
        if (_critStream == null)
            return;

        _audio.Stop(_critStream);
        _critStream = null;
    }

    /// <summary>Держит зацикленный поток включённым ровно пока <paramref name="wanted"/>.</summary>
    private void UpdateLoop(ref EntityUid? stream, bool wanted, SoundSpecifier sound, float volume)
    {
        if (wanted)
        {
            if (stream != null && Exists(stream.Value))
                return;

            stream = _audio.PlayGlobal(
                sound,
                Filter.Local(),
                false,
                AudioParams.Default.WithLoop(true).WithVolume(volume))?.Entity;
        }
        else if (stream != null)
        {
            _audio.Stop(stream);
            stream = null;
        }
    }

    private void StopLoops()
    {
        StopCritLoop();

        if (_alertStream != null)
        {
            _audio.Stop(_alertStream);
            _alertStream = null;
        }
    }

    private void Play(SoundSpecifier sound, float volume)
    {
        _audio.PlayGlobal(sound, Filter.Local(), false, AudioParams.Default.WithVolume(volume));
    }
}
