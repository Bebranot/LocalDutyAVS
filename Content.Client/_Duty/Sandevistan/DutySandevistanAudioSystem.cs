// SPDX-FileCopyrightText: 2026 LocalDuty
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client.Audio;
using Content.Shared._Duty.Sandevistan;
using Content.Shared.GameTicking;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Sandevistan;

/// <summary>
/// _Duty: 3-фазный клиентский аудиоконтроллер Sandevistan в стиле Cyberpunk: Edgerunners.
/// t=0.00s: sande_activation.ogg -> t=3.07s: sande_idle.ogg (loop) ->
/// при выключении: +0.40s удержания idle -> быстрый fade_out (0.20s) + sande_exit.ogg.
/// </summary>
public sealed class DutySandevistanAudioSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ContentAudioSystem _contentAudio = default!;

    private static readonly SoundSpecifier ActivationSound = new SoundPathSpecifier("/Audio/_Duty/Sandevistan/sande_activation.ogg");
    private static readonly SoundSpecifier IdleSound = new SoundPathSpecifier("/Audio/_Duty/Sandevistan/sande_idle.ogg");
    private static readonly SoundSpecifier ExitSound = new SoundPathSpecifier("/Audio/_Duty/Sandevistan/sande_exit.ogg");

    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(3.07);
    private static readonly TimeSpan ExitHoldDelay = TimeSpan.FromSeconds(0.40);
    private const float FadeOutDuration = 0.20f;

    private readonly Dictionary<EntityUid, AudioState> _states = new();
    private readonly List<EntityUid> _toRemove = new();

    private enum AudioPhase : byte
    {
        Active,
        Deactivating,
    }

    private sealed class AudioState
    {
        public AudioPhase Phase;
        public EntityUid? ActivationStream;
        public EntityUid? IdleStream;
        public EntityUid? ExitStream;
        public TimeSpan? IdleStartAt;
        public TimeSpan? FadeOutAt;
    }

    public override void Initialize()
    {
        base.Initialize();

        UpdatesOutsidePrediction = true;
        SubscribeNetworkEvent<SandevistanAudioEvent>(OnSandevistanAudio);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        StopAllImmediate();
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        StopAllImmediate();
    }

    private void OnSandevistanAudio(SandevistanAudioEvent ev)
    {
        if (!TryGetEntity(ev.User, out var uid))
            return;

        switch (ev.Phase)
        {
            case SandevistanAudioPhase.Activate:
                HandleActivate(uid.Value);
                break;
            case SandevistanAudioPhase.Deactivate:
                HandleDeactivate(uid.Value);
                break;
            case SandevistanAudioPhase.StopImmediate:
                StopEntityImmediate(uid.Value);
                break;
        }
    }

    private void HandleActivate(EntityUid uid)
    {
        // Очищаем возможные старые потоки при быстром перезапуске.
        StopEntityImmediate(uid);

        if (!Exists(uid) || TerminatingOrDeleted(uid))
            return;

        var state = new AudioState
        {
            Phase = AudioPhase.Active,
            IdleStartAt = _timing.CurTime + IdleDelay,
            ActivationStream = _audio.PlayEntity(ActivationSound, Filter.Local(), uid, false)?.Entity,
        };

        _states[uid] = state;
    }

    private void HandleDeactivate(EntityUid uid)
    {
        if (!_states.TryGetValue(uid, out var state))
        {
            if (Exists(uid) && !TerminatingOrDeleted(uid))
                _audio.PlayEntity(ExitSound, Filter.Local(), uid, false);
            return;
        }

        // Если уже деактивируется — не перезапускаем хвост повторно.
        if (state.Phase == AudioPhase.Deactivating)
            return;

        state.Phase = AudioPhase.Deactivating;
        state.IdleStartAt = null;

        if (state.IdleStream != null && Exists(state.IdleStream.Value))
        {
            // Держим idle ещё 0.4s, затем fade_out + exit.
            state.FadeOutAt = _timing.CurTime + ExitHoldDelay;
        }
        else
        {
            // Выключили до 3.07s: плавно гасим activation и сразу играем exit.
            FadeOutStream(state.ActivationStream);
            state.ActivationStream = null;
            if (Exists(uid) && !TerminatingOrDeleted(uid))
                state.ExitStream = _audio.PlayEntity(ExitSound, Filter.Local(), uid, false)?.Entity;
            state.FadeOutAt = null;
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_states.Count == 0)
            return;

        var curTime = _timing.CurTime;
        _toRemove.Clear();

        foreach (var (uid, state) in _states)
        {
            if (!Exists(uid) || TerminatingOrDeleted(uid))
            {
                StopStreams(state);
                _toRemove.Add(uid);
                continue;
            }

            if (state.Phase == AudioPhase.Active && state.IdleStartAt != null && curTime >= state.IdleStartAt.Value)
            {
                state.IdleStartAt = null;
                state.IdleStream = _audio.PlayEntity(
                    IdleSound,
                    Filter.Local(),
                    uid,
                    false,
                    AudioParams.Default.WithLoop(true))?.Entity;
            }
            else if (state.Phase == AudioPhase.Deactivating)
            {
                if (state.FadeOutAt != null)
                {
                    if (curTime >= state.FadeOutAt.Value)
                    {
                        state.FadeOutAt = null;
                        FadeOutStream(state.IdleStream);
                        state.IdleStream = null;
                        state.ExitStream = _audio.PlayEntity(ExitSound, Filter.Local(), uid, false)?.Entity;
                    }
                }
                else if (state.ExitStream == null || !Exists(state.ExitStream.Value))
                {
                    _toRemove.Add(uid);
                }
            }
        }

        foreach (var uid in _toRemove)
        {
            _states.Remove(uid);
        }
    }

    private void FadeOutStream(EntityUid? stream)
    {
        if (stream == null || !Exists(stream.Value) || TerminatingOrDeleted(stream.Value))
            return;

        _contentAudio.FadeOut(stream, duration: FadeOutDuration);
    }

    private void StopEntityImmediate(EntityUid uid)
    {
        if (!_states.Remove(uid, out var state))
            return;

        StopStreams(state);
    }

    private void StopAllImmediate()
    {
        foreach (var state in _states.Values)
        {
            StopStreams(state);
        }
        _states.Clear();
    }

    private void StopStreams(AudioState state)
    {
        if (state.ActivationStream != null && Exists(state.ActivationStream.Value))
            _audio.Stop(state.ActivationStream);
        if (state.IdleStream != null && Exists(state.IdleStream.Value))
            _audio.Stop(state.IdleStream);
        if (state.ExitStream != null && Exists(state.ExitStream.Value))
            _audio.Stop(state.ExitStream);

        state.ActivationStream = null;
        state.IdleStream = null;
        state.ExitStream = null;
    }
}
