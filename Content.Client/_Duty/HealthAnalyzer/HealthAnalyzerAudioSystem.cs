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
///  • вне крита — ТИШИНА. Раньше здесь бил одиночный сэмпл кардиомонитора, но сэмпла
///    (heartthump-heavy-monitor.ogg) в ассетах нет, сервер ругался «file does not exist»,
///    и по решению по геймплею стук монитора убран совсем;
///  • в крите — зацикленный критический тон (criticalloop.ogg): плавно появляется (fade-in)
///    и РЕЗКО обрывается при выходе из крита;
///  • на грани смерти (HP &lt; ~10%) — зацикленная тревога панели (loop);
///  • при смерти цели — один раз flatline.ogg.
/// Все цикличные потоки явно останавливаются при выходе из анализатора / состояния / смерти.
/// </summary>
public sealed class HealthAnalyzerAudioSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private readonly SoundSpecifier _critLoop = new SoundCollectionSpecifier("DutyHeartbeatCriticalLoop");
    private readonly SoundSpecifier _alert = new SoundCollectionSpecifier("DutyHeartbeatPanelAlert");
    private readonly SoundSpecifier _flatline = new SoundCollectionSpecifier("DutyHeartbeatFlatline");

    private const float AlertVolume = -6f;
    private const float FlatlineVolume = -3f;

    // Критический тон: плавный fade-in от «тишины» к цели.
    private const float CritStartVolume = -32f;
    private const float CritTargetVolume = -3.5f;
    private const float CritFadeIn = 1.2f;

    private bool _active;
    private bool _inCrit;
    private bool _nearDeath;
    private bool _flatlineState;

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
        _inCrit = ev.InCrit;
        _nearDeath = ev.NearDeath;
        _flatlineState = ev.Flatline;

        // Ровную линию проигрываем строго по одноразовому импульсу от сервера. Сервер сам
        // решает, кому и когда её слать (один раз на зрителя, см. HealthAnalyzerSystem), поэтому
        // ни пауза вне радиуса, ни повторный анализ трупа звук здесь не переигрывают.
        if (ev.PlayFlatline)
        {
            StopLoops();
            Play(_flatline, FlatlineVolume);
        }
    }

    private void OnStopAudio(HealthAnalyzerStopAudioEvent ev)
    {
        _active = false;
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

        // Цель мертва — тишина, глушим все циклы. Сам звук ровной линии — одноразовый
        // импульс в OnAudioEvent, здесь не проигрываем (иначе повтор при ре-синке).
        if (_flatlineState)
        {
            StopLoops();
            return;
        }

        // Тревога панели на грани смерти.
        UpdateLoop(ref _alertStream, _nearDeath, _alert, AlertVolume);

        // В крите: критический тон — плавный fade-in, резкий стоп на выходе.
        if (_inCrit)
        {
            TickCritLoop(frameTime);
            return;
        }

        // Вне крита анализатор молчит: стук монитора убран совсем (см. коммент класса).
        StopCritLoop();
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
