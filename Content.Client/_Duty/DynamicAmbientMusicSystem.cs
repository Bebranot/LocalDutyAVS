using Content.Client.Audio;
using Content.Client.Gameplay;
using Content.Shared.CCVar;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Duty.Audio;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.State;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client.Duty.Audio;

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

    private ContentAudioSystem _contentAudio = default!;

    private bool _wasInCombat;
    private bool _wasInCombatLow;
    private DutyMusicType _currentType = DutyMusicType.None;
    private HealthMusicState _currentHealthState = HealthMusicState.VeryGood;
    private MobState _lastMobState = MobState.Alive;
    private EntityUid? _currentStream;

    private TimeSpan _nextTrackTime = TimeSpan.Zero;
    private bool _trackPlaying;
    private bool _waitingForStateTransition;
    private TimeSpan _stateTransitionEndTime = TimeSpan.Zero;

    private bool _enabled;
    private float _volume;

    private const string PrototypeId = "DutyAmbientMusic";

    public override void Initialize()
    {
        base.Initialize();

        _contentAudio = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<ContentAudioSystem>();

        _config.OnValueChanged(DutyCCVars.DynamicAmbientMusicEnabled, OnEnabledChanged, true);
        _config.OnValueChanged(DutyCCVars.DynamicAmbientMusicVolume, OnVolumeChanged, true);

        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        PreloadTracks();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _config.UnsubValueChanged(DutyCCVars.DynamicAmbientMusicEnabled, OnEnabledChanged);
        _config.UnsubValueChanged(DutyCCVars.DynamicAmbientMusicVolume, OnVolumeChanged);
        StopCurrent(immediate: true);
    }

    private void OnEnabledChanged(bool value)
    {
        _enabled = value;
        if (!_enabled) StopCurrent(immediate: true);
    }

    private void OnVolumeChanged(float value)
    {
        _volume = value;
        if (_currentStream != null && TryComp(_currentStream, out Robust.Shared.Audio.Components.AudioComponent? comp))
            _audio.SetVolume(_currentStream, VolumeFromLinear(_volume), comp);
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
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted) return;

        if (!_enabled || _stateManager.CurrentState is not GameplayState)
        {
            if (_currentStream != null) StopCurrent(immediate: true);
            return;
        }

        var player = _playerManager.LocalSession?.AttachedEntity;
        if (player == null) { StopCurrent(immediate: true); return; }

        if (_volume <= 0f) { StopCurrent(immediate: true); return; }

        var mobState = GetMobState(player.Value);

        // Смерть
        if (mobState == MobState.Dead)
        {
            if (_lastMobState != MobState.Dead)
            {
                StopCurrent(immediate: false);
                PlayDeathSound();
            }
            _lastMobState = mobState;
            return;
        }

        // Призрак
        if (IsGhost(player.Value))
        {
            _lastMobState = mobState;
            _wasInCombat = false;
            _wasInCombatLow = false;
            UpdateGhostMusic();
            return;
        }

        // MobState.Critical - лежит без сознания
        if (mobState == MobState.Critical)
        {
            if (_lastMobState != MobState.Critical)
            {
                StopCurrent(immediate: false);
                _waitingForStateTransition = false;
                _trackPlaying = false;
                _nextTrackTime = TimeSpan.Zero;
            }
            _lastMobState = mobState;
            _wasInCombat = false;
            _wasInCombatLow = false;
            UpdateMobCritMusic();
            return;
        }

        _lastMobState = mobState;

        var inCombat = IsInCombatMode(player.Value);
        var hpPercent = GetHpPercent(player.Value);
        var proto = GetProto();
        var threshold = proto?.CombatLowHpThreshold ?? 10f;
        var inCombatLow = inCombat && hpPercent < threshold;

        // Боевой режим
        if (inCombat)
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

        // Вышли из боевого режима
        if (!inCombat && _wasInCombat)
        {
            if (_currentStream != null)
            {
                _contentAudio.FadeOut(_currentStream, duration: proto?.CombatFadeOutDuration ?? 1.5f);
                _currentStream = null;
                _currentType = DutyMusicType.None;
            }
            ScheduleNextTrack();
            _trackPlaying = false;
        }

        _wasInCombat = false;
        _wasInCombatLow = false;

        UpdateHealthMusic(player.Value);
    }

    private void UpdateGhostMusic()
    {
        if (_currentType == DutyMusicType.Calm && _currentStream != null)
        {
            if (!EntityManager.EntityExists(_currentStream.Value))
            {
                _currentStream = null;
                _currentType = DutyMusicType.None;
                _trackPlaying = false;
                ScheduleNextTrack();
            }
            return;
        }

        if (_timing.CurTime < _nextTrackTime || _trackPlaying) return;

        var proto = GetProto();
        if (proto == null) return;

        var ghostTracks = new List<SoundSpecifier>();
        ghostTracks.AddRange(proto.TracksVeryGood);
        ghostTracks.AddRange(proto.TracksGood);
        if (ghostTracks.Count == 0) return;

        var track = _random.Pick(ghostTracks);
        _currentStream = _audio.PlayGlobal(track, Filter.Local(), false,
            AudioParams.Default.WithVolume(VolumeFromLinear(_volume)))?.Entity;

        if (_currentStream != null)
        {
            _currentType = DutyMusicType.Calm;
            _trackPlaying = true;
            _contentAudio.FadeIn(_currentStream, duration: proto.CalmFadeInDuration);
        }
    }

    private void UpdateMobCritMusic()
    {
        if (_currentType == DutyMusicType.Calm && _currentStream != null)
        {
            if (!EntityManager.EntityExists(_currentStream.Value))
            {
                _currentStream = null;
                _currentType = DutyMusicType.None;
                _trackPlaying = false;
                ScheduleNextTrack();
            }
            return;
        }

        if (_timing.CurTime < _nextTrackTime || _trackPlaying) return;

        var proto = GetProto();
        if (proto == null || proto.TracksMobCritical.Count == 0) return;

        var track = _random.Pick(proto.TracksMobCritical);
        _currentStream = _audio.PlayGlobal(track, Filter.Local(), false,
            AudioParams.Default.WithVolume(VolumeFromLinear(_volume)))?.Entity;

        if (_currentStream != null)
        {
            _currentType = DutyMusicType.Calm;
            _trackPlaying = true;
            _contentAudio.FadeIn(_currentStream, duration: proto.CalmFadeInDuration);
        }
    }

    private void PlayDeathSound()
    {
        var proto = GetProto();
        if (proto?.DeathSound == null) return;

        _audio.PlayGlobal(proto.DeathSound, Filter.Local(), false,
            AudioParams.Default.WithVolume(VolumeFromLinear(_volume)));
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
                _contentAudio.FadeOut(_currentStream, duration: proto?.CalmFadeOutDuration ?? 3.5f);
                _currentStream = null;
                _currentType = DutyMusicType.None;
                _trackPlaying = false;

                _stateTransitionEndTime = _timing.CurTime + TimeSpan.FromSeconds(proto?.StateTransitionPause ?? 1.5f);
                _waitingForStateTransition = true;
                return;
            }
        }

        if (_waitingForStateTransition)
        {
            if (_timing.CurTime < _stateTransitionEndTime) return;
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
                _trackPlaying = false;
                ScheduleNextTrack();
            }
            return;
        }

        if (_timing.CurTime < _nextTrackTime || _trackPlaying) return;
        PlayHealthTrack();
    }

    private void PlayHealthTrack()
    {
        var proto = GetProto();
        if (proto == null) return;

        var tracks = GetTracksForState(_currentHealthState, proto);
        if (tracks.Count == 0) return;

        var track = _random.Pick(tracks);
        _currentStream = _audio.PlayGlobal(track, Filter.Local(), false,
            AudioParams.Default.WithVolume(VolumeFromLinear(_volume)))?.Entity;

        if (_currentStream != null)
        {
            _currentType = DutyMusicType.Calm;
            _trackPlaying = true;
            _contentAudio.FadeIn(_currentStream, duration: proto.CalmFadeInDuration);
        }
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
        if (proto == null || proto.CombatTracks.Count == 0) return;

        var track = _random.Pick(proto.CombatTracks);
        _currentStream = _audio.PlayGlobal(track, Filter.Local(), false,
            AudioParams.Default.WithVolume(VolumeFromLinear(_volume)).WithLoop(true))?.Entity;

        if (_currentStream != null) _currentType = DutyMusicType.Combat;
    }

    private void PlayCombatLowTrack()
    {
        var proto = GetProto();
        if (proto == null || proto.CombatLowTracks.Count == 0)
        {
            PlayCombatTrack();
            return;
        }

        var track = _random.Pick(proto.CombatLowTracks);
        _currentStream = _audio.PlayGlobal(track, Filter.Local(), false,
            AudioParams.Default.WithVolume(VolumeFromLinear(_volume)).WithLoop(true))?.Entity;

        if (_currentStream != null) _currentType = DutyMusicType.Combat;
    }

    private void StopCurrent(bool immediate = false)
    {
        if (_currentStream == null) return;

        if (immediate)
            _audio.Stop(_currentStream);
        else
        {
            var proto = GetProto();
            var duration = _currentType == DutyMusicType.Combat
                ? proto?.CombatFadeOutDuration ?? 1.5f
                : proto?.CalmFadeOutDuration ?? 3.5f;
            _contentAudio.FadeOut(_currentStream, duration: duration);
        }

        _currentStream = null;
        _currentType = DutyMusicType.None;
        _trackPlaying = false;
    }

    private void PreloadTracks()
    {
        var proto = GetProto();
        if (proto == null) return;

        var allLists = new List<List<SoundSpecifier>>
        {
            proto.TracksVeryGood, proto.TracksGood, proto.TracksMedium,
            proto.TracksBelowMedium, proto.TracksAwful, proto.TracksCritical,
            proto.TracksMobCritical, proto.CombatTracks, proto.CombatLowTracks
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

        if (proto.DeathSound is SoundPathSpecifier deathPath)
        {
            try { _resourceCache.GetResource<AudioResource>(deathPath.Path); }
            catch (Exception e)
            {
                Logger.Warning($"[DynamicAmbientMusic] Не удалось предзагрузить звук смерти '{deathPath.Path}': {e.Message}");
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
        if (!TryComp<MobThresholdsComponent>(player, out var thresholds)) return 100f;
        if (!TryComp<DamageableComponent>(player, out var damageable)) return 100f;

        var maxHp = 0f;
        foreach (var (damage, _) in thresholds.Thresholds)
            if (damage.Float() > maxHp) maxHp = damage.Float();

        if (maxHp <= 0f) return 100f;
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
            >= 5f  => HealthMusicState.Awful,
            _      => HealthMusicState.Critical
        };
    }

    private static List<SoundSpecifier> GetTracksForState(HealthMusicState state, DynamicAmbientMusicPrototype proto)
    {
        return state switch
        {
            HealthMusicState.VeryGood    => proto.TracksVeryGood,
            HealthMusicState.Good        => proto.TracksGood,
            HealthMusicState.Medium      => proto.TracksMedium,
            HealthMusicState.BelowMedium => proto.TracksBelowMedium,
            HealthMusicState.Awful       => proto.TracksAwful,
            HealthMusicState.Critical    => proto.TracksCritical,
            _                            => proto.TracksVeryGood
        };
    }

    private bool IsInCombatMode(EntityUid entity)
        => TryComp<CombatModeComponent>(entity, out var combat) && combat.IsInCombatMode;

    private DynamicAmbientMusicPrototype? GetProto()
    {
        if (_protoManager.TryIndex<DynamicAmbientMusicPrototype>(PrototypeId, out var proto)) return proto;
        Logger.Warning($"[DynamicAmbientMusic] Прототип '{PrototypeId}' не найден!");
        return null;
    }

    private static float VolumeFromLinear(float linear)
        => linear <= 0f ? -32f : 20f * MathF.Log10(linear);
}

public enum DutyMusicType { None, Calm, Combat }

public enum HealthMusicState
{
    VeryGood,    // 90-100%
    Good,        // 70-90%
    Medium,      // 40-70%
    BelowMedium, // 25-40%
    Awful,       // 5-25%
    Critical     // 0-5%
}
