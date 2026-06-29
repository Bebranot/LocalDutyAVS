using Content.Client.Audio;
using Content.Client.Gameplay;
using Content.Shared._Duty;
using Content.Shared.CCVar;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Duty.Audio;
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

namespace Content.Client.Duty.Audio;

/// <summary>
/// Динамическая фоновая музыка Duty: HP, бой, MobCritical, смерть.
/// </summary>
public sealed class DynamicAmbientMusicSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IStateManager _stateManager = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IAudioManager _audioManager = default!;
    [Dependency] private readonly ContentAudioSystem _contentAudio = default!;

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

    private bool _enabled = true;
    private bool _peacefulDisabled;
    private bool _combatDisabled;

    private float _critDuck;
    private float _lastAppliedMasterGain = -1f;
    private EntityUid? _critAuxUid;
    private EntityUid? _critEffectUid;

    private const string PrototypeId = "DutyAmbientMusic";

    private Action<float> _onMasterVolumeChanged = default!;
    private Action<float> _onAnyVolumeChanged = default!;
    private readonly Dictionary<DutyAmbientMusicLevel, Action<float>> _onLevelVolumeChanged = new();

    public override void Initialize()
    {
        base.Initialize();

        _onMasterVolumeChanged = _ => UpdateMasterGain();
        _onAnyVolumeChanged = _ => OnAnyVolumeChanged();

        _config.OnValueChanged(DutyCCVars.DynamicAmbientMusicEnabled, OnEnabledChanged, true);
        _config.OnValueChanged(DutyCCVars.DynamicAmbientMusicPeacefulDisabled, OnPeacefulDisabledChanged, true);
        _config.OnValueChanged(DutyCCVars.DynamicAmbientMusicCombatDisabled, OnCombatDisabledChanged, true);
        _config.OnValueChanged(CCVars.AudioMasterVolume, _onMasterVolumeChanged, true);

        foreach (DutyAmbientMusicLevel level in Enum.GetValues<DutyAmbientMusicLevel>())
        {
            Action<float> handler = _ => OnAnyVolumeChanged();
            _onLevelVolumeChanged[level] = handler;
            _config.OnValueChanged(DutyAmbientMusicCVar.GetVolumeCVar(level), handler, true);
        }

        _config.OnValueChanged(DutyCCVars.DynamicAmbientMusicVolume, _onAnyVolumeChanged, true);
        _config.OnValueChanged(DutyCCVars.DynamicAmbientMusicCritExtraBoostDb, _onAnyVolumeChanged, true);

        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        PreloadTracks();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        UnsubscribeConfig();
        StopCurrent(immediate: true);
        DeleteCritReverbChain();
        _critDuck = 0f;
        UpdateMasterGain(force: true);
    }

    private void DeleteCritReverbChain()
    {
        if (_critAuxUid != null)
        {
            EntityManager.DeleteEntity(_critAuxUid.Value);
            _critAuxUid = null;
        }

        if (_critEffectUid != null)
        {
            EntityManager.DeleteEntity(_critEffectUid.Value);
            _critEffectUid = null;
        }
    }

    private void UnsubscribeConfig()
    {
        _config.UnsubValueChanged(DutyCCVars.DynamicAmbientMusicEnabled, OnEnabledChanged);
        _config.UnsubValueChanged(DutyCCVars.DynamicAmbientMusicPeacefulDisabled, OnPeacefulDisabledChanged);
        _config.UnsubValueChanged(DutyCCVars.DynamicAmbientMusicCombatDisabled, OnCombatDisabledChanged);
        _config.UnsubValueChanged(CCVars.AudioMasterVolume, _onMasterVolumeChanged);
        _config.UnsubValueChanged(DutyCCVars.DynamicAmbientMusicVolume, _onAnyVolumeChanged);
        _config.UnsubValueChanged(DutyCCVars.DynamicAmbientMusicCritExtraBoostDb, _onAnyVolumeChanged);

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
    private void OnAnyVolumeChanged() => RefreshActiveStreamVolume();

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
        _critStreamNext = null;
        _critDuck = 0f;
        _currentLevel = null;
        if (_critEnterStream != null)
        {
            _audio.Stop(_critEnterStream);
            _critEnterStream = null;
        }
        _critEnterReadyTime = TimeSpan.Zero;
        DeleteCritReverbChain();
        UpdateMasterGain(force: true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        var inGameplay = _enabled && _stateManager.CurrentState is GameplayState;

        if (!inGameplay)
        {
            if (_currentStream != null)
                StopCurrent(immediate: true);
            UpdateCritAudioDuck(frameTime, inCrit: false);
            return;
        }

        var player = _playerManager.LocalSession?.AttachedEntity;
        if (player == null)
        {
            StopCurrent(immediate: true);
            UpdateCritAudioDuck(frameTime, inCrit: false);
            return;
        }

        if (!HasAnyAudibleVolume())
        {
            StopCurrent(immediate: true);
            UpdateCritAudioDuck(frameTime, inCrit: false);
            return;
        }

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
            if (_currentStream != null)
            {
                SafeFadeOut(_currentStream, proto?.CombatFadeOutDuration ?? 1.5f);
                _currentStream = null;
                _currentType = DutyMusicType.None;
                _currentLevel = null;
            }
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

    private void UpdateCritAudioDuck(float frameTime, bool inCrit)
    {
        var target = inCrit ? 1f : 0f;
        var fadeSec = _config.GetCVar(DutyCCVars.CritAudioDuckFadeSeconds);

        if (fadeSec <= 0f)
            _critDuck = target;
        else
        {
            var step = frameTime / fadeSec;
            _critDuck = inCrit
                ? Math.Min(target, _critDuck + step)
                : Math.Max(target, _critDuck - step);
        }

        UpdateMasterGain();
    }

    private void UpdateMasterGain(bool force = false)
    {
        var baseGain = _config.GetCVar(CCVars.AudioMasterVolume) * ContentAudioSystem.MasterVolumeMultiplier;
        var duckGain = _config.GetCVar(DutyCCVars.CritAudioDuckGain);
        var gain = baseGain * float.Lerp(1f, duckGain, _critDuck);

        if (!force && MathF.Abs(gain - _lastAppliedMasterGain) < 0.001f)
            return;

        _audioManager.SetMasterGain(gain);
        _lastAppliedMasterGain = gain;
    }

    private void EnsureCritReverbChain()
    {
        if (_critEffectUid != null && EntityManager.EntityExists(_critEffectUid.Value))
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
            if (!EntityManager.EntityExists(_currentStream.Value))
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
            var entry = _random.Pick(proto.TracksMobCritical);
            _currentStream = _audio.PlayGlobal(entry.Sound, Filter.Local(), false,
                AudioParams.Default.WithVolume(volume))?.Entity;

            if (_currentStream != null)
            {
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

            if (_currentStream != null)
                SafeFadeOut(_currentStream, (float)Math.Max(timeLeft, 0.5));

            var next = _random.Pick(proto.TracksMobCritical);
            _critStreamNext = _audio.PlayGlobal(next.Sound, Filter.Local(), false,
                AudioParams.Default.WithVolume(volume))?.Entity;

            if (_critStreamNext != null)
            {
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
                SafeFadeOut(_currentStream, proto?.CalmFadeOutDuration ?? 3.5f);
                _currentStream = null;
                _currentType = DutyMusicType.None;
                _currentLevel = null;
                _trackPlaying = false;

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
            if (!EntityManager.EntityExists(_currentStream.Value))
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

        PlayCalmTrack(_random.Pick(tracks), level, proto);
    }

    private void PlayCalmTrack(SoundSpecifier track, DutyAmbientMusicLevel level, DynamicAmbientMusicPrototype proto)
    {
        _currentStream = _audio.PlayGlobal(track, Filter.Local(), false,
            AudioParams.Default.WithVolume(GetVolumeDb(level)))?.Entity;

        if (_currentStream == null)
            return;

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

        var track = _random.Pick(proto.CombatTracks);
        _currentStream = _audio.PlayGlobal(track, Filter.Local(), false,
            AudioParams.Default.WithVolume(GetVolumeDb(DutyAmbientMusicLevel.Combat)).WithLoop(true))?.Entity;

        if (_currentStream != null)
        {
            _currentType = DutyMusicType.Combat;
            _currentLevel = DutyAmbientMusicLevel.Combat;
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

        var track = _random.Pick(proto.CombatLowTracks);
        _currentStream = _audio.PlayGlobal(track, Filter.Local(), false,
            AudioParams.Default.WithVolume(GetVolumeDb(DutyAmbientMusicLevel.CombatLow)).WithLoop(true))?.Entity;

        if (_currentStream != null)
        {
            _currentType = DutyMusicType.Combat;
            _currentLevel = DutyAmbientMusicLevel.CombatLow;
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

        ClearCritReverb(_currentStream);

        if (immediate)
            _audio.Stop(_currentStream);
        else
        {
            var proto = GetProto();
            var duration = _currentType == DutyMusicType.Combat
                ? proto?.CombatFadeOutDuration ?? 1.5f
                : proto?.CalmFadeOutDuration ?? 3.5f;
            SafeFadeOut(_currentStream, duration);
        }

        _currentStream = null;
        _currentType = DutyMusicType.None;
        _currentLevel = null;
        _trackPlaying = false;
    }

    private void RefreshActiveStreamVolume()
    {
        if (_currentStream == null || _currentLevel == null)
            return;

        if (!TryComp<AudioComponent>(_currentStream, out var comp))
            return;

        _audio.SetVolume(_currentStream, GetVolumeDb(_currentLevel.Value), comp);
    }

    private float GetVolumeLinear(DutyAmbientMusicLevel level)
    {
        var linear = _config.GetCVar(DutyAmbientMusicCVar.GetVolumeCVar(level));
        var global = _config.GetCVar(DutyCCVars.DynamicAmbientMusicVolume);
        if (global > 0f)
            linear *= Math.Clamp(global, 0f, 1f);
        return linear;
    }

    private float GetVolumeDb(DutyAmbientMusicLevel level)
    {
        var db = VolumeFromLinear(GetVolumeLinear(level));

        var proto = GetProto();

        // Общий буст ко всем категориям динамической музыки/эмбиента.
        db += proto?.VolumeBoostDb ?? 0f;

        if (level != DutyAmbientMusicLevel.MobCritical)
            return db;

        db += proto?.MobCritVolumeBoost ?? 0f;
        db += _config.GetCVar(DutyCCVars.DynamicAmbientMusicCritExtraBoostDb);
        return db;
    }

    private bool HasAnyAudibleVolume()
    {
        foreach (DutyAmbientMusicLevel level in Enum.GetValues<DutyAmbientMusicLevel>())
        {
            if (GetVolumeLinear(level) > 0f)
                return true;
        }
        return false;
    }

    private void PreloadTracks()
    {
        var proto = GetProto();
        if (proto == null)
            return;

        var allLists = new List<List<SoundSpecifier>>
        {
            proto.TracksVeryGood, proto.TracksGood, proto.TracksMedium,
            proto.TracksBelowMedium, proto.TracksAwful, proto.TracksCritical,
            proto.CombatTracks, proto.CombatLowTracks, proto.DeathSounds, proto.CritEnterSounds
        };

        foreach (var list in allLists)
        foreach (var track in list)
        {
            if (track is SoundPathSpecifier path)
            {
                try { _resourceCache.GetResource<AudioResource>(path.Path); }
                catch (Exception e)
                {
                    Logger.Warning($"[DynamicAmbientMusic] Не удалось предзагрузить '{path.Path}': {e.Message}");
                }
            }
        }

        foreach (var critTrack in proto.TracksMobCritical)
        {
            if (critTrack.Sound is SoundPathSpecifier critPath)
            {
                try { _resourceCache.GetResource<AudioResource>(critPath.Path); }
                catch (Exception e)
                {
                    Logger.Warning($"[DynamicAmbientMusic] Не удалось предзагрузить крит-трек '{critPath.Path}': {e.Message}");
                }
            }
        }

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
        return Math.Clamp(100f * (1f - damageable.TotalDamage.Float() / maxHp), 0f, 100f);
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

    private DynamicAmbientMusicPrototype? GetProto()
    {
        if (_protoManager.TryIndex<DynamicAmbientMusicPrototype>(PrototypeId, out var proto))
            return proto;
        Logger.Warning($"[DynamicAmbientMusic] Прототип '{PrototypeId}' не найден!");
        return null;
    }

    private static float VolumeFromLinear(float linear)
        => linear <= 0f ? -32f : 20f * MathF.Log10(linear);
}

public enum DutyMusicType { None, Calm, Combat }
