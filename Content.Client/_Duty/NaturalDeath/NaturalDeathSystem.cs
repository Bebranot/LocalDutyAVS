using Content.Client.Audio;
using Content.Client.Gameplay;
using Content.Shared._Duty.NaturalDeath;
using Content.Shared.GameTicking;
using Content.Shared.Ghost.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Client._Duty.NaturalDeath;

/// <summary>
/// _Duty: клиентское затухание восприятия при естественной смерти — плавный уход экрана в полную
/// темноту без белой вспышки, затухающий шум/звон в ушах и последние замедляющиеся удары сердца,
/// с гарантированным сбросом при реанимации, выходе в призрака, смене сущности или рестарте.
/// </summary>
public sealed class NaturalDeathSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayMan = default!;
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IStateManager _stateManager = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ContentAudioSystem _contentAudio = default!;

    private enum ScenePhase : byte
    {
        Idle = 0,
        FadingToBlack = 1,
        HoldingBlack = 2,
        FadingOutRevive = 3,
    }

    private const float DefaultFadeDuration = 2.8f;
    private const float ReviveFadeOutDuration = 0.6f;

    // Последние два глухих удара сердца (время в секундах от начала смерти).
    private const float Beat1Delay = 0.20f;
    private const float Beat1VolumeDb = -6f;
    private const float Beat1Pitch = 0.85f;

    private const float Beat2Delay = 1.40f;
    private const float Beat2VolumeDb = -13f;
    private const float Beat2Pitch = 0.72f;

    // Тихий звон в ушах, затухающий в тишину до конца фейда.
    private const float TinnitusStartDb = -18f;
    private const float TinnitusEndDb = -36f;

    private NaturalDeathOverlay? _overlay;
    private ScenePhase _phase = ScenePhase.Idle;
    private EntityUid? _dyingEntity;
    private float _elapsed;
    private float _fadeDuration = DefaultFadeDuration;
    private float _reviveStartAlpha;
    private float _reviveElapsed;

    private SoundSpecifier? _finalBeatSound;
    private bool _beat1Played;
    private bool _beat2Played;
    private bool _sawClientDead;
    private EntityUid? _tinnitusStream;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<NaturalDeathStartedEvent>(OnNaturalDeathStarted);
        SubscribeNetworkEvent<NaturalDeathCancelledEvent>(OnNaturalDeathCancelled);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnLocalPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnLocalPlayerDetached);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        ImmediateReset();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        ImmediateReset();
    }

    private void OnLocalPlayerAttached(LocalPlayerAttachedEvent ev)
    {
        if (_dyingEntity != null && ev.Entity != _dyingEntity)
            ImmediateReset();
    }

    private void OnLocalPlayerDetached(LocalPlayerDetachedEvent ev)
    {
        ImmediateReset();
    }

    private void OnNaturalDeathStarted(NaturalDeathStartedEvent ev)
    {
        var target = GetEntity(ev.Target);
        var local = _player.LocalEntity;
        if (local == null || local != target || HasComp<GhostComponent>(local.Value))
            return;

        ImmediateReset();

        _dyingEntity = target;
        _phase = ScenePhase.FadingToBlack;
        _elapsed = 0f;
        _fadeDuration = MathF.Max(ev.FadeDuration, 0.5f);
        _finalBeatSound = ev.FinalBeatSound;
        _beat1Played = false;
        _beat2Played = false;
        _sawClientDead = TryComp<MobStateComponent>(target, out var mob) && mob.CurrentState == MobState.Dead;

        EnsureOverlay();
        _overlay!.Alpha = 0f;

        _tinnitusStream = _audio.PlayGlobal(
            ev.TinnitusSound,
            Filter.Local(),
            false,
            AudioParams.Default.WithLoop(true).WithVolume(TinnitusStartDb))?.Entity;
    }

    private void OnNaturalDeathCancelled(NaturalDeathCancelledEvent ev)
    {
        if (_phase == ScenePhase.Idle)
            return;

        if (!ev.FadeOut)
        {
            ImmediateReset();
            return;
        }

        StartReviveFadeOut();
    }

    private void StartReviveFadeOut()
    {
        StopAudio(fadeOut: true);
        _finalBeatSound = null;

        if (_overlay == null || _overlay.Alpha <= 0.001f)
        {
            ImmediateReset();
            return;
        }

        _phase = ScenePhase.FadingOutRevive;
        _reviveStartAlpha = _overlay.Alpha;
        _reviveElapsed = 0f;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_phase == ScenePhase.Idle)
            return;

        var local = _player.LocalEntity;
        if (_stateManager.CurrentState is not GameplayState
            || local == null
            || HasComp<GhostComponent>(local.Value)
            || (_dyingEntity != null && local != _dyingEntity))
        {
            ImmediateReset();
            return;
        }

        // Страховка: если тело уже прошло через Dead и вышло из него (реанимация/рейджув),
        // или за 1с состояние на клиенте так и не стало Dead — плавно возвращаем зрение.
        if (_phase is ScenePhase.FadingToBlack or ScenePhase.HoldingBlack
            && TryComp<MobStateComponent>(local.Value, out var mob))
        {
            if (mob.CurrentState == MobState.Dead)
                _sawClientDead = true;
            else if (_sawClientDead || _elapsed > 1.0f)
                StartReviveFadeOut();
        }

        switch (_phase)
        {
            case ScenePhase.FadingToBlack:
                UpdateFadeToBlack(frameTime);
                break;

            case ScenePhase.HoldingBlack:
                if (_overlay != null)
                    _overlay.Alpha = 1f;
                break;

            case ScenePhase.FadingOutRevive:
                UpdateReviveFadeOut(frameTime);
                break;
        }
    }

    private void UpdateFadeToBlack(float frameTime)
    {
        _elapsed += frameTime;
        var progress = Math.Clamp(_elapsed / _fadeDuration, 0f, 1f);

        // Плавное затухание восприятия до полной темноты без резких рывков.
        if (_overlay != null)
            _overlay.Alpha = SmoothStep(progress);

        UpdateDeathAudio(progress);

        if (progress >= 1f)
        {
            _phase = ScenePhase.HoldingBlack;
            StopAudio(fadeOut: false);
        }
    }

    private void UpdateDeathAudio(float progress)
    {
        if (!_beat1Played && _elapsed >= Beat1Delay)
        {
            _beat1Played = true;
            if (_finalBeatSound != null)
            {
                _audio.PlayGlobal(
                    _finalBeatSound,
                    Filter.Local(),
                    false,
                    AudioParams.Default.WithVolume(Beat1VolumeDb).WithPitchScale(Beat1Pitch));
            }
        }

        if (!_beat2Played && _elapsed >= Beat2Delay)
        {
            _beat2Played = true;
            if (_finalBeatSound != null)
            {
                _audio.PlayGlobal(
                    _finalBeatSound,
                    Filter.Local(),
                    false,
                    AudioParams.Default.WithVolume(Beat2VolumeDb).WithPitchScale(Beat2Pitch));
            }
        }

        if (_tinnitusStream != null)
        {
            if (!Exists(_tinnitusStream.Value))
            {
                _tinnitusStream = null;
            }
            else if (TryComp<AudioComponent>(_tinnitusStream.Value, out var audioComp))
            {
                var db = MathHelper.Lerp(TinnitusStartDb, TinnitusEndDb, SmoothStep(progress));
                _audio.SetVolume(_tinnitusStream, db, audioComp);
            }
        }
    }

    private void UpdateReviveFadeOut(float frameTime)
    {
        _reviveElapsed += frameTime;
        var progress = Math.Clamp(_reviveElapsed / ReviveFadeOutDuration, 0f, 1f);

        if (_overlay != null)
            _overlay.Alpha = _reviveStartAlpha * (1f - SmoothStep(progress));

        if (progress >= 1f)
            ImmediateReset();
    }

    private void EnsureOverlay()
    {
        if (_overlay == null)
            _overlay = new NaturalDeathOverlay(_clyde);

        if (!_overlayMan.HasOverlay<NaturalDeathOverlay>())
            _overlayMan.AddOverlay(_overlay);
    }

    private void ImmediateReset()
    {
        _phase = ScenePhase.Idle;
        _dyingEntity = null;
        _elapsed = 0f;
        _reviveElapsed = 0f;
        _finalBeatSound = null;
        _beat1Played = false;
        _beat2Played = false;
        _sawClientDead = false;

        StopAudio(fadeOut: false);

        if (_overlay != null)
        {
            _overlay.Alpha = 0f;
            if (_overlayMan.HasOverlay<NaturalDeathOverlay>())
                _overlayMan.RemoveOverlay(_overlay);
        }
    }

    private void StopAudio(bool fadeOut)
    {
        if (_tinnitusStream == null)
            return;

        if (Exists(_tinnitusStream.Value))
        {
            if (fadeOut && HasComp<AudioComponent>(_tinnitusStream.Value))
                _contentAudio.FadeOut(_tinnitusStream, duration: 0.4f);
            else
                _audio.Stop(_tinnitusStream);
        }

        _tinnitusStream = null;
    }

    private static float SmoothStep(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }
}
