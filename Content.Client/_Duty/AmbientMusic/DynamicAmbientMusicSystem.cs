using Content.Client.Audio;
using Content.Client.Gameplay;
using Content.Shared._Duty.AmbientMusic;
using Content.Shared._Duty.FireAgony;
using Content.Shared._Duty.FuryStimulator;
using Content.Shared.CCVar;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.State;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Effects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Duty.AmbientMusic;

/// <summary>
/// Динамическая фоновая музыка Duty: HP, бой, MobCritical, смерть.
/// </summary>
public sealed partial class DynamicAmbientMusicSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IStateManager _stateManager = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly Robust.Client.Audio.AudioSystem _clientAudio = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IAudioManager _audioManager = default!;
    [Dependency] private readonly ContentAudioSystem _contentAudio = default!;
    [Dependency] private readonly DutyMusicDirector _director = default!;

    private bool _wasInCombat;
    private bool _wasInCombatLow;
    private DutyMusicType _currentType = DutyMusicType.None;
    private DutyAmbientMusicLevel? _currentLevel;
    private HealthMusicState _currentHealthState = HealthMusicState.VeryGood;
    private MobState _lastMobState = MobState.Alive;
    private EntityUid? _currentStream;

    private EntityUid? _critStreamNext;
    private TimeSpan _critCurrentEndTime;
    private TimeSpan _critNextEndTime;
    private bool _critCrossfadeStarted;
    private bool _critPlaying;

    private EntityUid? _critEnterStream;
    private TimeSpan _critEnterReadyTime = TimeSpan.Zero;
    private static readonly TimeSpan CritEnterCooldown = TimeSpan.FromMinutes(2);
    private const float CritEnterFadeOutDuration = 0.5f;

    private TimeSpan _nextTrackTime = TimeSpan.Zero;
    private bool _trackPlaying;
    private bool _waitingForStateTransition;
    private TimeSpan _stateTransitionEndTime = TimeSpan.Zero;

    // Fury-16: пока идёт эффект стимулятора, динамическая музыка выключена (у Fury своя музыка фаз),
    // и возвращается только через FuryResumeDelay после окончания эффекта.
    private bool _furySuppressed;

    // _Duty: то же самое, но от арбитра музыки — объявления кодов и ванильный/лавалендский
    // эмбиент глушат динамическую музыку, пока звучат.
    private bool _directorSuppressed;
    private TimeSpan _furyResumeTime = TimeSpan.Zero;
    private static readonly TimeSpan FuryResumeDelay = TimeSpan.FromSeconds(20);

    // Предзагрузка треков. Раньше здесь грузился разом весь список из прототипа — 48 уникальных
    // треков, ~1.47 ГБ распакованного PCM в OpenAL на каждом клиенте, ещё и в Initialize, до того
    // как игрок вообще куда-то зашёл. Теперь держим прогретыми только те треки, которые реально
    // могут заиграть следующими; подробности в <see cref="DutyMusicCache"/>.
    private DutyMusicCache _musicCache = default!;

    /// <summary>Что нельзя выгружать. Порядок не важен, важен только состав.</summary>
    private readonly HashSet<ResPath> _keepWarm = new();

    /// <summary>То же самое, но по убыванию приоритета прогрева — см. <see cref="UpdateWarmup"/>.</summary>
    private readonly List<ResPath> _warmOrder = new();

    private TimeSpan _nextWarmupCheck = TimeSpan.Zero;

    // Вне раунда (главное меню, лобби) греемся чаще: там просадка кадра никому не мешает, зато к
    // началу раунда всё нужное уже в памяти. В бою наоборот — редко и по одному треку.
    private static readonly TimeSpan WarmupIntervalInGame = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WarmupIntervalIdle = TimeSpan.FromSeconds(0.2);

    private SoundSpecifier? _pendingCalmTrack;
    private HealthMusicState _pendingCalmState;
    private SoundSpecifier? _pendingCombatTrack;
    private SoundSpecifier? _pendingCombatLowTrack;
    private DutyCritTrack? _pendingCritTrack;

    /// <summary>Пути того, что играет прямо сейчас — их нельзя выгружать из-под AL.</summary>
    private ResPath? _currentTrackPath;
    private ResPath? _critNextTrackPath;

    private bool _enabled = true;
    private bool _peacefulDisabled;
    private bool _combatDisabled;

    private float _critDuck;

    // _Duty: приглушение всего звука на время сцены «Агонии от огня» — ещё один фактор
    // master gain рядом с крит-даком (иначе две системы дрались бы за SetMasterGain).
    private float _agonyDuck;
    private const float AgonyDuckGain = 0.35f;
    private const float AgonyDuckFadeSeconds = 0.5f;

    private float _lastAppliedMasterGain = -1f;
    private EntityUid? _critAuxUid;
    private EntityUid? _critEffectUid;

    private const string PrototypeId = "DutyAmbientMusic";

    /// <summary>Сколько раз пробуем вытянуть трек, отличный от играющего, прежде чем сдаться.</summary>
    private const int PickRetries = 4;

    private static readonly DutyAmbientMusicLevel[] AllLevels = Enum.GetValues<DutyAmbientMusicLevel>();

    // Громкости читаются в HasAnyAudibleVolume на каждом тике — это было 22 GetCVar и свежий
    // массив от Enum.GetValues в кадр. На все эти cvar'ы мы и так подписаны, так что держим
    // последние значения у себя.
    private readonly float[] _levelVolume = new float[AllLevels.Length];
    private float _globalVolumeMultiplier = 1f;
    private float _critExtraBoostDb;
    private float _masterVolume;
    private float _critDuckGain;
    private float _critDuckFadeSeconds;

    private DynamicAmbientMusicPrototype? _proto;
    private bool _protoMissingLogged;

    private Action<float> _onMasterVolumeChanged = default!;
    private Action<float> _onGlobalVolumeChanged = default!;
    private Action<float> _onCritBoostChanged = default!;
    private readonly Dictionary<DutyAmbientMusicLevel, Action<float>> _onLevelVolumeChanged = new();

    public override void Initialize()
    {
        base.Initialize();

        _onMasterVolumeChanged = value =>
        {
            _masterVolume = value;
            UpdateMasterGain();
        };
        _onGlobalVolumeChanged = value =>
        {
            _globalVolumeMultiplier = value;
            OnAnyVolumeChanged();
        };
        _onCritBoostChanged = value =>
        {
            _critExtraBoostDb = value;
            OnAnyVolumeChanged();
        };

        _config.OnValueChanged(DutyCCVars.DynamicAmbientMusicEnabled, OnEnabledChanged, true);
        _config.OnValueChanged(DutyCCVars.DynamicAmbientMusicPeacefulDisabled, OnPeacefulDisabledChanged, true);
        _config.OnValueChanged(DutyCCVars.DynamicAmbientMusicCombatDisabled, OnCombatDisabledChanged, true);
        _config.OnValueChanged(CCVars.AudioMasterVolume, _onMasterVolumeChanged, true);

        foreach (var level in AllLevels)
        {
            var index = (int) level;
            Action<float> handler = value =>
            {
                _levelVolume[index] = value;
                OnAnyVolumeChanged();
            };
            _onLevelVolumeChanged[level] = handler;
            _config.OnValueChanged(DutyAmbientMusicCVar.GetVolumeCVar(level), handler, true);
        }

        _config.OnValueChanged(DutyCCVars.DynamicAmbientMusicVolume, _onGlobalVolumeChanged, true);
        _config.OnValueChanged(DutyCCVars.DynamicAmbientMusicCritExtraBoostDb, _onCritBoostChanged, true);
        _config.OnValueChanged(DutyCCVars.CritAudioDuckGain, OnCritDuckGainChanged, true);
        _config.OnValueChanged(DutyCCVars.CritAudioDuckFadeSeconds, OnCritDuckFadeChanged, true);

        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        _musicCache = new DutyMusicCache(_resourceCache, _audioManager, Log);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        UnsubscribeConfig();
        StopCurrent(immediate: true);
        StopCritStreams();
        DeleteCritReverbChain();
        _critDuck = 0f;
        _agonyDuck = 0f;
        UpdateMasterGain(force: true);
        _musicCache.Clear(IsStreamAlive);
    }

    private void DeleteCritReverbChain()
    {
        if (_critAuxUid != null)
        {
            Del(_critAuxUid.Value);
            _critAuxUid = null;
        }

        if (_critEffectUid != null)
        {
            Del(_critEffectUid.Value);
            _critEffectUid = null;
        }
    }

    private void UnsubscribeConfig()
    {
        _config.UnsubValueChanged(DutyCCVars.DynamicAmbientMusicEnabled, OnEnabledChanged);
        _config.UnsubValueChanged(DutyCCVars.DynamicAmbientMusicPeacefulDisabled, OnPeacefulDisabledChanged);
        _config.UnsubValueChanged(DutyCCVars.DynamicAmbientMusicCombatDisabled, OnCombatDisabledChanged);
        _config.UnsubValueChanged(CCVars.AudioMasterVolume, _onMasterVolumeChanged);
        _config.UnsubValueChanged(DutyCCVars.DynamicAmbientMusicVolume, _onGlobalVolumeChanged);
        _config.UnsubValueChanged(DutyCCVars.DynamicAmbientMusicCritExtraBoostDb, _onCritBoostChanged);
        _config.UnsubValueChanged(DutyCCVars.CritAudioDuckGain, OnCritDuckGainChanged);
        _config.UnsubValueChanged(DutyCCVars.CritAudioDuckFadeSeconds, OnCritDuckFadeChanged);

        foreach (var (level, handler) in _onLevelVolumeChanged)
            _config.UnsubValueChanged(DutyAmbientMusicCVar.GetVolumeCVar(level), handler);

        _onLevelVolumeChanged.Clear();
    }

    private void OnEnabledChanged(bool value)
    {
        _enabled = value;
        if (!_enabled)
            StopCurrent(immediate: true);
    }

    private void OnPeacefulDisabledChanged(bool value) => _peacefulDisabled = value;
    private void OnCombatDisabledChanged(bool value) => _combatDisabled = value;
    private void OnCritDuckGainChanged(float value) => _critDuckGain = value;
    private void OnCritDuckFadeChanged(float value) => _critDuckFadeSeconds = value;
    private void OnAnyVolumeChanged() => RefreshActiveStreamVolume();

    /// <summary>
    /// Прототип кэшируется (его дёргают по несколько раз за тик), так что после хот-релоада
    /// кэш и уже выбранные кандидаты на прогрев надо выбросить — они могут ссылаться на треки,
    /// которых в прототипе больше нет.
    /// </summary>
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<DynamicAmbientMusicPrototype>())
            return;

        _proto = null;
        _pendingCalmTrack = null;
        _pendingCombatTrack = null;
        _pendingCombatLowTrack = null;
        _pendingCritTrack = null;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        StopCurrent(immediate: true);
        _wasInCombat = false;
        _wasInCombatLow = false;
        _trackPlaying = false;
        _waitingForStateTransition = false;
        _nextTrackTime = TimeSpan.Zero;
        _currentHealthState = HealthMusicState.VeryGood;
        _lastMobState = MobState.Alive;
        _critPlaying = false;
        _critCrossfadeStarted = false;
        if (_critStreamNext != null)
        {
            ClearCritReverb(_critStreamNext);
            _audio.Stop(_critStreamNext);
            _critStreamNext = null;
        }
        _critDuck = 0f;
        _currentLevel = null;
        if (_critEnterStream != null)
        {
            _audio.Stop(_critEnterStream);
            _critEnterStream = null;
        }
        _critEnterReadyTime = TimeSpan.Zero;
        _furySuppressed = false;
        _furyResumeTime = TimeSpan.Zero;
        _directorSuppressed = false;
        _critCurrentEndTime = TimeSpan.Zero;
        _critNextEndTime = TimeSpan.Zero;
        _currentTrackPath = null;
        _critNextTrackPath = null;
        _pendingCalmTrack = null;
        _pendingCombatTrack = null;
        _pendingCombatLowTrack = null;
        _pendingCritTrack = null;
        DeleteCritReverbChain();
        UpdateMasterGain(force: true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        if (_timing.CurTime >= _nextWarmupCheck)
        {
            var idle = _stateManager.CurrentState is not GameplayState;
            _nextWarmupCheck = _timing.CurTime + (idle ? WarmupIntervalIdle : WarmupIntervalInGame);
            UpdateWarmup();
        }

        var inGameplay = _enabled && _stateManager.CurrentState is GameplayState;

        // Все три выхода раньше глушили только _currentStream. Второй крит-поток (кроссфейд) и
        // стингер входа в крит оставались играть, а _critPlaying/_critCurrentEndTime — протухшими:
        // при возврате в игру крит-музыка сразу лезла в кроссфейд по времени из прошлой жизни.
        if (!inGameplay)
        {
            StopEverything();
            UpdateCritAudioDuck(frameTime, inCrit: false);
            return;
        }

        var player = _playerManager.LocalSession?.AttachedEntity;
        if (player == null)
        {
            StopEverything();
            UpdateCritAudioDuck(frameTime, inCrit: false);
            return;
        }

        if (!HasAnyAudibleVolume())
        {
            StopEverything();
            UpdateCritAudioDuck(frameTime, inCrit: false);
            return;
        }

        if (UpdateFurySuppression(player.Value, frameTime))
            return;

        if (UpdateDirectorSuppression(player.Value, frameTime))
            return;

        var mobState = GetMobState(player.Value);

        if (mobState == MobState.Dead)
        {
            UpdateCritAudioDuck(frameTime, inCrit: false);
            if (_lastMobState != MobState.Dead)
            {
                StopCurrent(immediate: false);
                StopCritStreams();
                PlayDeathSound();
            }
            _lastMobState = mobState;
            return;
        }

        if (IsGhost(player.Value))
        {
            UpdateCritAudioDuck(frameTime, inCrit: false);
            _lastMobState = mobState;
            _wasInCombat = false;
            _wasInCombatLow = false;
            // В призрака можно попасть и минуя MobState.Dead (гиб, смена тела) — тогда
            // зациклённый боевой трек остался бы играть, а призрачная музыка легла бы поверх.
            if (_currentType == DutyMusicType.Combat)
                StopCurrent(immediate: false);
            if (!_peacefulDisabled)
                UpdateGhostMusic();
            else if (_currentStream != null)
                StopCurrent(immediate: false);
            return;
        }

        if (mobState == MobState.Critical)
        {
            if (_lastMobState != MobState.Critical)
            {
                StopCurrent(immediate: false);
                _waitingForStateTransition = false;
                _trackPlaying = false;
                _nextTrackTime = TimeSpan.Zero;
                _critPlaying = false;
                _critCrossfadeStarted = false;
                _critStreamNext = null;
                PlayCritEnterSound();
            }
            _lastMobState = mobState;
            _wasInCombat = false;
            _wasInCombatLow = false;
            UpdateCritAudioDuck(frameTime, inCrit: true);
            UpdateMobCritMusic();
            return;
        }

        if (_lastMobState == MobState.Critical)
        {
            StopCurrent(immediate: false);
            StopCritStreams();
            _trackPlaying = false;
            _nextTrackTime = TimeSpan.Zero;
        }

        UpdateCritAudioDuck(frameTime, inCrit: false);
        _lastMobState = mobState;

        var inCombat = IsInCombatMode(player.Value);
        var hpPercent = GetHpPercent(player.Value);
        var proto = GetProto();
        var threshold = proto?.CombatLowHpThreshold ?? 10f;
        var inCombatLow = inCombat && hpPercent < threshold;

        // Боевая музыка зациклена, и ветка «вышел из боя» ниже срабатывает только при !inCombat.
        // Если игрок выключил боевую музыку, не выходя из боевого режима, трек продолжал крутиться
        // вечно: мирная логика ниже просто перезаписывала _currentStream поверх него.
        if (inCombat && _combatDisabled && _currentType == DutyMusicType.Combat)
        {
            StopCurrent(immediate: false);
            _wasInCombat = false;
            _wasInCombatLow = false;
        }

        if (inCombat && !_combatDisabled)
        {
            if (!_wasInCombat)
            {
                StopCurrent(immediate: true);
                _waitingForStateTransition = false;
                if (inCombatLow)
                    PlayCombatLowTrack();
                else
                    PlayCombatTrack();
                _wasInCombatLow = inCombatLow;
            }
            else if (inCombatLow && !_wasInCombatLow)
            {
                StopCurrent(immediate: true);
                PlayCombatLowTrack();
                _wasInCombatLow = true;
            }
            else if (!inCombatLow && _wasInCombatLow)
            {
                StopCurrent(immediate: true);
                PlayCombatTrack();
                _wasInCombatLow = false;
            }

            _wasInCombat = true;
            return;
        }

        if (!inCombat && _wasInCombat)
        {
            FadeOutCurrent(proto?.CombatFadeOutDuration ?? 1.5f);
            if (!_peacefulDisabled)
                ScheduleNextTrack();
            _trackPlaying = false;
        }

        _wasInCombat = false;
        _wasInCombatLow = false;

        if (!_peacefulDisabled)
            UpdateHealthMusic(player.Value);
        else if (_currentStream != null && _currentType == DutyMusicType.Calm)
            StopCurrent(immediate: false);
    }

    /// <summary>
    /// Пока на игроке активен эффект Fury-16 (и 20 c после его окончания) глушим динамическую
    /// музыку — у Fury своя персональная музыка фаз. Возвращает <c>true</c>, если музыка сейчас
    /// подавлена и остальную логику <see cref="Update"/> надо пропустить.
    /// </summary>
    private bool UpdateFurySuppression(EntityUid player, float frameTime)
    {
        var furyActive = HasComp<FuryStimulatorComponent>(player);

        if (furyActive)
        {
            _furySuppressed = true;
            _furyResumeTime = _timing.CurTime + FuryResumeDelay;
        }

        if (!_furySuppressed)
            return false;

        // Эффект закончился и таймер возврата истёк — снимаем подавление, музыка возвращается сразу.
        if (!furyActive && _timing.CurTime >= _furyResumeTime)
        {
            _furySuppressed = false;
            _trackPlaying = false;
            _waitingForStateTransition = false;
            _nextTrackTime = _timing.CurTime;
            return false;
        }

        if (_currentStream != null)
            StopCurrent(immediate: false);
        StopCritStreams();
        UpdateCritAudioDuck(frameTime, inCrit: false);
        _lastMobState = GetMobState(player);
        return true;
    }

    /// <summary>
    /// Глушит динамическую музыку, пока играет что-то приоритетнее: объявление кода, ванильный
    /// или лавалендский эмбиент. Раньше все три слоя звучали одновременно.
    /// Возвращает <c>true</c>, если музыка сейчас подавлена и остальную логику
    /// <see cref="Update"/> надо пропустить.
    /// </summary>
    private bool UpdateDirectorSuppression(EntityUid player, float frameTime)
    {
        if (_director.CanPlay(DutyMusicDirector.DynamicMusicPriority))
        {
            if (!_directorSuppressed)
                return false;

            // Приоритетный звук закончился — возвращаем музыку сразу, без ожидания трека.
            _directorSuppressed = false;
            _trackPlaying = false;
            _waitingForStateTransition = false;
            _nextTrackTime = _timing.CurTime;
            return false;
        }

        _directorSuppressed = true;

        if (_currentStream != null)
            StopCurrent(immediate: false);
        StopCritStreams();
        UpdateCritAudioDuck(frameTime, inCrit: false);
        _lastMobState = GetMobState(player);
        return true;
    }

    private void UpdateCritAudioDuck(float frameTime, bool inCrit)
    {
        var target = inCrit ? 1f : 0f;
        var fadeSec = _critDuckFadeSeconds;

        if (fadeSec <= 0f)
            _critDuck = target;
        else
        {
            var step = frameTime / fadeSec;
            _critDuck = inCrit
                ? Math.Min(target, _critDuck + step)
                : Math.Max(target, _critDuck - step);
        }

        // _Duty: параллельно ведём дак «Агонии от огня» (свой фейд ~0.5с).
        var agonyTarget = IsLocalAgony() ? 1f : 0f;
        var agonyStep = AgonyDuckFadeSeconds <= 0f ? 1f : frameTime / AgonyDuckFadeSeconds;
        _agonyDuck = agonyTarget > _agonyDuck
            ? Math.Min(agonyTarget, _agonyDuck + agonyStep)
            : Math.Max(agonyTarget, _agonyDuck - agonyStep);

        UpdateMasterGain();
    }

    /// <summary>_Duty: активна ли сейчас сцена агонии у локального игрока.</summary>
    private bool IsLocalAgony()
    {
        return _playerManager.LocalEntity is { } local
            && TryComp<FireAgonyComponent>(local, out var agony)
            && agony.Active;
    }

    private void UpdateMasterGain(bool force = false)
    {
        // _Duty: cvar уже хранит финальный (после ×Scale слайдера) gain — см. OptionSliderFloatCVar.Value
        // и AudioTab.OnMasterVolumeSliderChanged. Повторное умножение на MasterVolumeMultiplier тут было
        // багом: раздувало мастер-гейн в 3 раза каждый раз, когда крит/агони-дак пересчитывался и пробивал
        // эпсилон-кэш ниже — снаружи это выглядело как "звук стал громче настроенного после сцены агонии".
        var gain = _masterVolume * float.Lerp(1f, _critDuckGain, _critDuck) * float.Lerp(1f, AgonyDuckGain, _agonyDuck);

        // Мастер-гейн глобальный, и пишем в него не только мы (ADTBossMusicSystem, ползунок в
        // настройках). Пока наш дак активен, переписываем каждый кадр: иначе чужая запись съедала
        // бы приглушение до конца сцены — кэш ниже решил бы, что нужное значение уже выставлено.
        var ducking = _critDuck > 0f || _agonyDuck > 0f;

        if (!force && !ducking && MathF.Abs(gain - _lastAppliedMasterGain) < 0.001f)
            return;

        _audioManager.SetMasterGain(gain);
        _lastAppliedMasterGain = gain;
    }

    private void EnsureCritReverbChain()
    {
        if (_critEffectUid != null && Exists(_critEffectUid.Value))
            return;

        var (effectEnt, effectComp) = _audio.CreateEffect();
        _audio.SetEffectPreset(effectEnt, effectComp, ReverbPresets.Cave);

        var (auxEnt, auxComp) = _audio.CreateAuxiliary();
        _audio.SetEffect(auxEnt, auxComp, effectEnt);
        _critEffectUid = effectEnt;
        _critAuxUid = auxEnt;
    }

    private void ApplyCritReverb(EntityUid? stream)
    {
        if (stream == null || !TryComp<AudioComponent>(stream, out var comp))
            return;

        EnsureCritReverbChain();
        _audio.SetAuxiliary(stream.Value, comp, _critAuxUid);
    }

    private void ClearCritReverb(EntityUid? stream)
    {
        if (stream == null || !TryComp<AudioComponent>(stream, out var comp))
            return;

        _audio.SetAuxiliary(stream.Value, comp, null);
    }

    private void UpdateGhostMusic()
    {
        if (_currentType == DutyMusicType.Calm && _currentStream != null)
        {
            if (!Exists(_currentStream.Value))
            {
                _currentStream = null;
                _currentType = DutyMusicType.None;
                _currentLevel = null;
                _trackPlaying = false;
                ScheduleNextTrack();
            }
            return;
        }

        if (_timing.CurTime < _nextTrackTime || _trackPlaying)
            return;

        var proto = GetProto();
        if (proto == null)
            return;

        var ghostTracks = new List<SoundSpecifier>();
        ghostTracks.AddRange(proto.TracksVeryGood);
        ghostTracks.AddRange(proto.TracksGood);
        if (ghostTracks.Count == 0)
            return;

        PlayCalmTrack(_random.Pick(ghostTracks), DutyAmbientMusicLevel.VeryGood, proto);
    }

    private void UpdateMobCritMusic()
    {
        var proto = GetProto();
        if (proto == null || proto.TracksMobCritical.Count == 0)
            return;

        if (GetVolumeLinear(DutyAmbientMusicLevel.MobCritical) <= 0f)
            return;

        var crossfade = proto.MobCritCrossfadeDuration;
        var volume = GetVolumeDb(DutyAmbientMusicLevel.MobCritical);

        if (!_critPlaying)
        {
            StopCurrent(immediate: false);

            var entry = _pendingCritTrack ?? PickCritTrack(proto.TracksMobCritical);
            _pendingCritTrack = null;
            _currentStream = PlayMusic(entry.Sound, AudioParams.Default.WithVolume(volume));

            if (_currentStream != null)
            {
                _currentTrackPath = GetTrackPath(entry.Sound);
                _currentType = DutyMusicType.Calm;
                _currentLevel = DutyAmbientMusicLevel.MobCritical;
                _critPlaying = true;
                _critCrossfadeStarted = false;
                _critStreamNext = null;
                _critCurrentEndTime = _timing.CurTime + TimeSpan.FromSeconds(entry.Duration);
                _critNextEndTime = TimeSpan.Zero;
                ApplyCritReverb(_currentStream);
                _contentAudio.FadeIn(_currentStream, duration: crossfade);
            }
            return;
        }

        var timeLeft = (_critCurrentEndTime - _timing.CurTime).TotalSeconds;

        if (!_critCrossfadeStarted && timeLeft <= crossfade)
        {
            _critCrossfadeStarted = true;

            var fadeOut = (float) Math.Max(timeLeft, 0.5);

            if (_currentStream != null)
                SafeFadeOut(_currentStream, fadeOut);

            var next = _pendingCritTrack ?? PickCritTrack(proto.TracksMobCritical);
            _pendingCritTrack = null;
            _critStreamNext = PlayMusic(next.Sound, AudioParams.Default.WithVolume(volume));

            if (_critStreamNext != null)
            {
                _critNextTrackPath = GetTrackPath(next.Sound);
                ApplyCritReverb(_critStreamNext);
                _contentAudio.FadeIn(_critStreamNext, duration: crossfade);
                _critNextEndTime = _timing.CurTime + TimeSpan.FromSeconds(next.Duration);
            }
        }

        if (_critCrossfadeStarted && _timing.CurTime >= _critCurrentEndTime)
        {
            if (_critStreamNext != null)
            {
                if (_currentStream != null)
                {
                    ClearCritReverb(_currentStream);
                    _audio.Stop(_currentStream);
                }
                _currentStream = _critStreamNext;
                _currentTrackPath = _critNextTrackPath;
                _critNextTrackPath = null;
                _critStreamNext = null;
                _critCurrentEndTime = _critNextEndTime;
                _critCrossfadeStarted = false;
            }
        }
    }

    private void PlayDeathSound()
    {
        var proto = GetProto();
        if (proto == null || proto.DeathSounds.Count == 0 || GetVolumeLinear(DutyAmbientMusicLevel.Death) <= 0f)
            return;

        var sound = _random.Pick(proto.DeathSounds);
        _audio.PlayGlobal(sound, Filter.Local(), false,
            AudioParams.Default.WithVolume(GetVolumeDb(DutyAmbientMusicLevel.Death)));
    }

    private void PlayCritEnterSound()
    {
        var proto = GetProto();
        if (proto == null || proto.CritEnterSounds.Count == 0 || GetVolumeLinear(DutyAmbientMusicLevel.CritEnter) <= 0f)
            return;

        if (_timing.CurTime < _critEnterReadyTime)
            return;

        _critEnterReadyTime = _timing.CurTime + CritEnterCooldown;

        var sound = _random.Pick(proto.CritEnterSounds);
        _critEnterStream = _audio.PlayGlobal(sound, Filter.Local(), false,
            AudioParams.Default.WithVolume(GetVolumeDb(DutyAmbientMusicLevel.CritEnter)))?.Entity;
    }

    private void StopCritEnterSound()
    {
        if (_critEnterStream == null)
            return;

        SafeFadeOut(_critEnterStream, CritEnterFadeOutDuration);
        _critEnterStream = null;
    }

    /// <summary>
    /// Полностью гасит крит-музыку (включая поток кроссфейда и звук входа) и сбрасывает флаги.
    /// Вызывается при выходе из крита и при смерти — иначе при смерти посреди кроссфейда
    /// второй крит-поток оставался играть "осиротевшим".
    /// </summary>
    private void StopCritStreams()
    {
        if (_critStreamNext != null)
        {
            ClearCritReverb(_critStreamNext);
            _audio.Stop(_critStreamNext);
            _critStreamNext = null;
            _critNextTrackPath = null;
        }

        _critPlaying = false;
        _critCrossfadeStarted = false;
        StopCritEnterSound();
    }

    private void UpdateHealthMusic(EntityUid player)
    {
        var newState = GetHealthState(player);

        if (newState != _currentHealthState)
        {
            _currentHealthState = newState;

            if (_currentStream != null)
            {
                var proto = GetProto();
                FadeOutCurrent(proto?.CalmFadeOutDuration ?? 3.5f);

                _stateTransitionEndTime = _timing.CurTime + TimeSpan.FromSeconds(proto?.StateTransitionPause ?? 1.5f);
                _waitingForStateTransition = true;
                return;
            }
        }

        if (_waitingForStateTransition)
        {
            if (_timing.CurTime < _stateTransitionEndTime)
                return;
            _waitingForStateTransition = false;
            _trackPlaying = false;
            _nextTrackTime = TimeSpan.Zero;
        }

        if (_currentType == DutyMusicType.Calm && _currentStream != null)
        {
            if (!Exists(_currentStream.Value))
            {
                _currentStream = null;
                _currentType = DutyMusicType.None;
                _currentLevel = null;
                _trackPlaying = false;
                ScheduleNextTrack();
            }
            return;
        }

        if (_timing.CurTime < _nextTrackTime || _trackPlaying)
            return;

        PlayHealthTrack();
    }

    private void PlayHealthTrack()
    {
        var proto = GetProto();
        if (proto == null)
            return;

        var level = DutyAmbientMusicCVar.FromHealthState(_currentHealthState);
        if (GetVolumeLinear(level) <= 0f)
        {
            ScheduleNextTrack();
            return;
        }

        var tracks = GetTracksForState(_currentHealthState, proto);
        if (tracks.Count == 0)
            return;

        PlayCalmTrack(TakePending(ref _pendingCalmTrack, tracks), level, proto);
    }

    /// <summary>
    /// Проигрывает музыкальный трек через наш кэш, а не через SoundSpecifier. Обычный путь
    /// (<see cref="SharedAudioSystem.PlayGlobal(SoundSpecifier, Filter, bool, AudioParams?)"/>)
    /// кладёт буфер в кэш ресурсов движка, откуда его уже никогда не выгрузить — из-за этого
    /// клиент и держал в памяти все 48 треков разом. Подробности в <see cref="DutyMusicCache"/>.
    /// </summary>
    private EntityUid? PlayMusic(SoundSpecifier track, AudioParams audioParams)
    {
        // Не путь, а коллекция звуков: сами резолвить не будем, отдаём движку как есть.
        if (GetTrackPath(track) is not { } path)
            return _audio.PlayGlobal(track, Filter.Local(), false, audioParams)?.Entity;

        if (_musicCache.Get(path) is not { } stream)
            return null;

        var uid = _clientAudio.PlayGlobal(stream, new ResolvedPathSpecifier(path), audioParams)?.Entity;

        if (uid != null)
            _musicCache.NoteUser(path, uid.Value);

        return uid;
    }

    private void PlayCalmTrack(SoundSpecifier track, DutyAmbientMusicLevel level, DynamicAmbientMusicPrototype proto)
    {
        // Все Play* присваивают _currentStream напрямую, так что предыдущий поток обязан быть
        // уже погашен. Страховка от новых веток, которые про это забудут.
        StopCurrent(immediate: false);

        _currentStream = PlayMusic(track, AudioParams.Default.WithVolume(GetVolumeDb(level)));

        if (_currentStream == null)
            return;

        _currentTrackPath = GetTrackPath(track);
        _currentType = DutyMusicType.Calm;
        _currentLevel = level;
        _trackPlaying = true;
        _contentAudio.FadeIn(_currentStream, duration: proto.CalmFadeInDuration);
    }

    private void ScheduleNextTrack()
    {
        var proto = GetProto();
        _nextTrackTime = _timing.CurTime + TimeSpan.FromSeconds(
            _random.NextFloat(proto?.CalmMinInterval ?? 5f, proto?.CalmMaxInterval ?? 50f));
    }

    private void PlayCombatTrack()
    {
        var proto = GetProto();
        if (proto == null || proto.CombatTracks.Count == 0 || GetVolumeLinear(DutyAmbientMusicLevel.Combat) <= 0f)
            return;

        StopCurrent(immediate: false);

        var track = TakeWarmPending(ref _pendingCombatTrack, proto.CombatTracks);
        _currentStream = PlayMusic(track,
            AudioParams.Default.WithVolume(GetVolumeDb(DutyAmbientMusicLevel.Combat)).WithLoop(true));

        if (_currentStream != null)
        {
            _currentTrackPath = GetTrackPath(track);
            _currentType = DutyMusicType.Combat;
            _currentLevel = DutyAmbientMusicLevel.Combat;
            // combatFadeInDuration был объявлен в прототипе и выставлен в yml, но нигде не
            // применялся — бой начинался рывком на полной громкости.
            if (proto.CombatFadeInDuration > 0f)
                _contentAudio.FadeIn(_currentStream, duration: proto.CombatFadeInDuration);
        }
    }

    private void PlayCombatLowTrack()
    {
        var proto = GetProto();
        if (proto == null)
        {
            PlayCombatTrack();
            return;
        }

        if (proto.CombatLowTracks.Count == 0 || GetVolumeLinear(DutyAmbientMusicLevel.CombatLow) <= 0f)
        {
            PlayCombatTrack();
            return;
        }

        StopCurrent(immediate: false);

        var track = TakeWarmPending(ref _pendingCombatLowTrack, proto.CombatLowTracks);
        _currentStream = PlayMusic(track,
            AudioParams.Default.WithVolume(GetVolumeDb(DutyAmbientMusicLevel.CombatLow)).WithLoop(true));

        if (_currentStream != null)
        {
            _currentTrackPath = GetTrackPath(track);
            _currentType = DutyMusicType.Combat;
            _currentLevel = DutyAmbientMusicLevel.CombatLow;
            if (proto.CombatFadeInDuration > 0f)
                _contentAudio.FadeIn(_currentStream, duration: proto.CombatFadeInDuration);
        }
    }

    /// <summary>
    /// Безопасный фейд-аут: не дёргает <see cref="ContentAudioSystem.FadeOut"/> на уже исчезнувшем
    /// потоке (иначе Resolve логирует ошибку "Can't resolve AudioComponent" со стектрейсом —
    /// одноразовые звуки, например звук входа в крит, доигрывают и удаляются, а ссылка остаётся).
    /// </summary>
    private void SafeFadeOut(EntityUid? stream, float duration)
    {
        if (stream == null || !Exists(stream.Value) || !HasComp<AudioComponent>(stream.Value))
            return;

        _contentAudio.FadeOut(stream, duration: duration);
    }

    private void StopCurrent(bool immediate = false)
    {
        if (_currentStream == null)
            return;

        if (!immediate)
        {
            var proto = GetProto();
            FadeOutCurrent(_currentType == DutyMusicType.Combat
                ? proto?.CombatFadeOutDuration ?? 1.5f
                : proto?.CalmFadeOutDuration ?? 3.5f);
            return;
        }

        ClearCritReverb(_currentStream);
        _audio.Stop(_currentStream);
        ForgetCurrent();
    }

    /// <summary>
    /// Гасит текущий поток фейдом. Буфер трека при этом не выгрузится из-под ещё доигрывающего
    /// источника: <see cref="DutyMusicCache"/> сам помнит, какой аудио-сущности его отдал
    /// (<see cref="DutyMusicCache.NoteUser"/>), и <see cref="DutyMusicCache.Trim"/> ждёт, пока
    /// она умрёт — таймеры вроде отдельного «остывающего» слота тут не нужны.
    /// </summary>
    private void FadeOutCurrent(float duration)
    {
        if (_currentStream == null)
            return;

        ClearCritReverb(_currentStream);
        SafeFadeOut(_currentStream, duration);
        ForgetCurrent();
    }

    private void ForgetCurrent()
    {
        _currentStream = null;
        _currentTrackPath = null;
        _currentType = DutyMusicType.None;
        _currentLevel = null;
        _trackPlaying = false;
    }

    /// <summary>
    /// Полная тишина: и обычный поток, и оба крит-потока, и тайминги крита. Нужна на выходах,
    /// после которых мы вообще не знаем, сколько времени пройдёт до возврата — лобби, отвал
    /// от тела, выкрученные в ноль ползунки.
    /// </summary>
    private void StopEverything()
    {
        StopCurrent(immediate: true);
        StopCritStreams();
        _critCurrentEndTime = TimeSpan.Zero;
        _critNextEndTime = TimeSpan.Zero;
        _lastMobState = MobState.Alive;
    }

    private void RefreshActiveStreamVolume()
    {
        // Второй крит-поток живёт только во время кроссфейда, но ползунок могут двинуть и тогда.
        if (_critStreamNext != null && TryComp<AudioComponent>(_critStreamNext, out var nextComp))
            _audio.SetVolume(_critStreamNext.Value, GetVolumeDb(DutyAmbientMusicLevel.MobCritical), nextComp);

        if (_currentStream == null || _currentLevel == null)
            return;

        // Ползунок уровня увели в ноль — трек надо гасить, а не выставлять ему «тихую» громкость:
        // VolumeFromLinear(0) это -32 dB, и после буста уровня (+2…+10 dB) он прекрасно слышен.
        if (GetVolumeLinear(_currentLevel.Value) <= 0f)
        {
            StopCurrent(immediate: false);
            return;
        }

        if (!TryComp<AudioComponent>(_currentStream, out var comp))
            return;

        _audio.SetVolume(_currentStream, GetVolumeDb(_currentLevel.Value), comp);
    }

    private float GetVolumeLinear(DutyAmbientMusicLevel level)
    {
        var linear = _levelVolume[(int) level];
        // Ноль здесь означает «множитель не задан», а не «тишина» — cvar устаревший.
        if (_globalVolumeMultiplier > 0f)
            linear *= Math.Clamp(_globalVolumeMultiplier, 0f, 1f);
        return MathF.Max(linear, 0f);
    }

    private float GetVolumeDb(DutyAmbientMusicLevel level)
    {
        var db = VolumeFromLinear(GetVolumeLinear(level));

        var proto = GetProto();

        // Критмод, смерть и крит-стингер имеют собственные независимые бусты и НЕ участвуют
        // в общем бусте музыки (VolumeBoostDb) — их громкость не меняется при усилении музыки.
        switch (level)
        {
            case DutyAmbientMusicLevel.MobCritical:
                db += proto?.MobCritVolumeBoost ?? 0f;
                db += _critExtraBoostDb;
                return db;
            case DutyAmbientMusicLevel.Death:
                db += proto?.DeathVolumeBoost ?? 0f;
                return db;
            case DutyAmbientMusicLevel.CritEnter:
                db += proto?.CritEnterVolumeBoost ?? 0f;
                return db;
            default:
                // Общий буст ко всем «музыкальным» категориям (HP-уровни, бой).
                db += proto?.VolumeBoostDb ?? 0f;
                return db;
        }
    }

    private bool HasAnyAudibleVolume()
    {
        foreach (var level in AllLevels)
        {
            if (GetVolumeLinear(level) > 0f)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Держит прогретыми только те треки, которые могут заиграть следующими, и выгружает
    /// остальные. Вызывается по таймеру, а не каждый кадр: выбор трека — это Pick по списку,
    /// а Trim перебирает наш словарь.
    /// </summary>
    private void UpdateWarmup()
    {
        var proto = GetProto();
        if (proto == null)
            return;

        // Музыку выключили — новых кандидатов не выбираем, старые выгрузятся ниже.
        // Именно выгрузятся, а не Clear(): то, что ещё доигрывает, трогать нельзя.
        var musicWanted = _enabled && HasAnyAudibleVolume();

        if (musicWanted)
        {
            PickPendingTracks(proto);
        }
        else
        {
            _pendingCalmTrack = null;
            _pendingCombatTrack = null;
            _pendingCombatLowTrack = null;
            _pendingCritTrack = null;
        }

        // Трек мог доиграть сам, без StopCurrent — тогда защищать его буфер больше не от чего.
        // Проверяем здесь, а не в каждой ветке, которая обнуляет поток: так не забудется.
        if (!IsStreamAlive(_currentStream))
            _currentTrackPath = null;

        if (!IsStreamAlive(_critStreamNext))
            _critNextTrackPath = null;

        // Стингер входа в крит — одноразовый звук: движок удаляет его сам, а ссылка висела бы
        // до конца раунда и уехала бы в SafeFadeOut на несуществующей сущности.
        if (_critEnterStream != null && !Exists(_critEnterStream.Value))
            _critEnterStream = null;

        // Порядок = приоритет прогрева, и он не случаен. Вход в бой — единственный переход,
        // который случается внезапно и посреди стрельбы, поэтому боевые треки греются первыми.
        // Спокойный трек может позволить себе подождать: он включается по таймеру, из тишины.
        _keepWarm.Clear();
        _warmOrder.Clear();
        AddWarmPath(GetTrackPath(_pendingCombatTrack));
        AddWarmPath(GetTrackPath(_pendingCombatLowTrack));
        AddWarmPath(GetTrackPath(_pendingCritTrack?.Sound));
        AddWarmPath(_currentTrackPath);
        AddWarmPath(_critNextTrackPath);
        AddWarmPath(GetTrackPath(_pendingCalmTrack));

        // Строго по одному треку за проход: декодирование ogg стоит от 0.3 до 1.9 с (десятиминутный
        // SAW_2), и складывать несколько штук в один кадр — гарантированная просадка.
        //
        // Пока хоть что-то ещё не прогрето, ничего не выгружаем: иначе, поменяв боевого кандидата,
        // мы бы выбросили старый трек до того, как загрузился новый, и внезапный бой в эту секунду
        // упёрся бы в холодную загрузку.
        if (!_musicCache.WarmNext(_warmOrder))
            _musicCache.Trim(_keepWarm, IsStreamAlive);
    }

    /// <summary>
    /// Жив ли ещё источник. Stop и фейд-аут удаляют аудио-сущность отложенно (QueueDel), так что
    /// «уже остановленный» поток вполне может доигрывать до конца тика — и его буфер в это время
    /// трогать нельзя.
    /// </summary>
    private bool IsStreamAlive(EntityUid? stream)
        => stream != null && Exists(stream.Value);

    private bool IsStreamAlive(EntityUid stream)
        => Exists(stream);

    private void AddWarmPath(ResPath? path)
    {
        if (path != null && _keepWarm.Add(path.Value))
            _warmOrder.Add(path.Value);
    }

    /// <summary>
    /// Выбирает по одному кандидату на каждый контекст. Крит-трек прогреваем только когда игрок
    /// уже плох — иначе он занимал бы память весь раунд ради события, которого может не случиться.
    /// </summary>
    private void PickPendingTracks(DynamicAmbientMusicPrototype proto)
    {
        if (_pendingCalmTrack == null || _pendingCalmState != _currentHealthState)
        {
            var tracks = GetTracksForState(_currentHealthState, proto);
            _pendingCalmTrack = tracks.Count > 0 ? PickTrack(tracks) : null;
            _pendingCalmState = _currentHealthState;
        }

        if (_pendingCombatTrack == null && proto.CombatTracks.Count > 0)
            _pendingCombatTrack = PickTrack(proto.CombatTracks);

        if (_pendingCombatLowTrack == null && proto.CombatLowTracks.Count > 0)
            _pendingCombatLowTrack = PickTrack(proto.CombatLowTracks);

        var critLikely = _lastMobState == MobState.Critical
                         || _currentHealthState is HealthMusicState.Awful or HealthMusicState.Critical;

        if (critLikely)
        {
            if (_pendingCritTrack == null && proto.TracksMobCritical.Count > 0)
                _pendingCritTrack = PickCritTrack(proto.TracksMobCritical);
        }
        else if (_lastMobState != MobState.Critical)
        {
            _pendingCritTrack = null;
        }
    }

    /// <summary>
    /// То же, что <see cref="TakePending"/>, но для боя: если предвыбранный кандидат ещё не
    /// прогрет, играем любой уже лежащий в памяти трек из того же списка, а кандидата оставляем
    /// догреваться. Вход в бой — единственный внезапный переход, и холодная загрузка обходится
    /// там в 0.3–1.9 с замершего кадра; повтор трека не стоит ничего.
    /// </summary>
    private SoundSpecifier TakeWarmPending(ref SoundSpecifier? pending, List<SoundSpecifier> pool)
    {
        if (GetTrackPath(pending) is { } pendingPath && _musicCache.IsWarm(pendingPath))
            return TakePending(ref pending, pool);

        foreach (var candidate in pool)
        {
            if (GetTrackPath(candidate) is { } path && _musicCache.IsWarm(path))
                return candidate;
        }

        // Ничего тёплого нет вообще — деваться некуда, грузим на месте.
        return TakePending(ref pending, pool);
    }

    /// <summary>Берёт предвыбранный трек и сбрасывает слот, чтобы прогрелся следующий.</summary>
    private SoundSpecifier TakePending(ref SoundSpecifier? pending, List<SoundSpecifier> fallback)
    {
        var track = pending ?? PickTrack(fallback);
        pending = null;
        return track;
    }

    /// <summary>
    /// Случайный трек, по возможности не тот, что звучит прямо сейчас. Плейлисты пересекаются
    /// (один и тот же файл лежит в нескольких списках, а в tracksMobCritical есть прямой дубль),
    /// и без этого кроссфейд трека сам в себя звучит как заедание.
    /// </summary>
    private SoundSpecifier PickTrack(List<SoundSpecifier> tracks)
    {
        if (tracks.Count <= 1 || _currentTrackPath == null)
            return _random.Pick(tracks);

        for (var i = 0; i < PickRetries; i++)
        {
            var track = _random.Pick(tracks);
            if (GetTrackPath(track) != _currentTrackPath)
                return track;
        }

        return _random.Pick(tracks);
    }

    /// <inheritdoc cref="PickTrack"/>
    private DutyCritTrack PickCritTrack(List<DutyCritTrack> tracks)
    {
        if (tracks.Count <= 1 || _currentTrackPath == null)
            return _random.Pick(tracks);

        for (var i = 0; i < PickRetries; i++)
        {
            var track = _random.Pick(tracks);
            if (GetTrackPath(track.Sound) != _currentTrackPath)
                return track;
        }

        return _random.Pick(tracks);
    }

    private static ResPath? GetTrackPath(SoundSpecifier? track)
    {
        return track is SoundPathSpecifier path ? path.Path : null;
    }

    private MobState GetMobState(EntityUid player)
    {
        if (TryComp<MobStateComponent>(player, out var mobState))
            return mobState.CurrentState;
        return MobState.Alive;
    }

    private bool IsGhost(EntityUid player)
        => HasComp<Content.Shared.Ghost.GhostComponent>(player);

    private float GetHpPercent(EntityUid player)
    {
        if (!TryComp<MobThresholdsComponent>(player, out var thresholds))
            return 100f;
        if (!TryComp<DamageableComponent>(player, out var damageable))
            return 100f;

        var maxHp = 0f;
        foreach (var (damage, _) in thresholds.Thresholds)
            if (damage.Float() > maxHp)
                maxHp = damage.Float();

        if (maxHp <= 0f)
            return 100f;
        return Math.Clamp(100f * (1f - _damageable.GetTotalDamage((player, damageable)).Float() / maxHp), 0f, 100f);
    }

    private HealthMusicState GetHealthState(EntityUid player)
    {
        var hpPercent = GetHpPercent(player);
        return hpPercent switch
        {
            >= 90f => HealthMusicState.VeryGood,
            >= 70f => HealthMusicState.Good,
            >= 40f => HealthMusicState.Medium,
            >= 25f => HealthMusicState.BelowMedium,
            >= 5f => HealthMusicState.Awful,
            _ => HealthMusicState.Critical
        };
    }

    private static List<SoundSpecifier> GetTracksForState(HealthMusicState state, DynamicAmbientMusicPrototype proto)
    {
        return state switch
        {
            HealthMusicState.VeryGood => proto.TracksVeryGood,
            HealthMusicState.Good => proto.TracksGood,
            HealthMusicState.Medium => proto.TracksMedium,
            HealthMusicState.BelowMedium => proto.TracksBelowMedium,
            HealthMusicState.Awful => proto.TracksAwful,
            HealthMusicState.Critical => proto.TracksCritical,
            _ => proto.TracksVeryGood
        };
    }

    private bool IsInCombatMode(EntityUid entity)
        => TryComp<CombatModeComponent>(entity, out var combat) && combat.IsInCombatMode;

    /// <summary>
    /// Прототип спрашивают по несколько раз за тик (GetVolumeDb, StopCurrent, ScheduleNextTrack),
    /// поэтому держим его у себя. Кэш сбрасывает <see cref="OnPrototypesReloaded"/>.
    /// </summary>
    private DynamicAmbientMusicPrototype? GetProto()
    {
        if (_proto != null)
            return _proto;

        if (_protoManager.TryIndex<DynamicAmbientMusicPrototype>(PrototypeId, out var proto))
        {
            _proto = proto;
            return _proto;
        }

        // Раньше это был Logger.Warning из Update — 60 одинаковых строк в секунду.
        if (!_protoMissingLogged)
        {
            _protoMissingLogged = true;
            Log.Warning($"[DynamicAmbientMusic] Прототип '{PrototypeId}' не найден!");
        }

        return null;
    }

    private static float VolumeFromLinear(float linear)
        => linear <= 0f ? -32f : 20f * MathF.Log10(linear);
}

public enum DutyMusicType { None, Calm, Combat }
