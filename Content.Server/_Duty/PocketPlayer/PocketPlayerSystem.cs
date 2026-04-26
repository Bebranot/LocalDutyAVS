// SPDX-FileCopyrightText: 2025 LocalDuty
// SPDX-License-Identifier: MIT

using Content.Shared._Duty.PocketPlayer;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Duty.PocketPlayer;

public sealed class PocketPlayerSystem : SharedPocketPlayerSystem
{
    [Dependency] private readonly IPrototypeManager _protoManager = default!;

    private const float MaxDistance = 14f;
    private const float BaseVolume = -12f;   // было -3f, снижено до -12f
    private const float RolloffFactor = 2.5f;
    private const float ReferenceDistance = 1.5f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PocketPlayerComponent, PocketPlayerSelectTrackMessage>(OnTrackSelected);
        SubscribeLocalEvent<PocketPlayerComponent, PocketPlayerPlayMessage>(OnPlay);
        SubscribeLocalEvent<PocketPlayerComponent, PocketPlayerPauseMessage>(OnPause);
        SubscribeLocalEvent<PocketPlayerComponent, PocketPlayerStopMessage>(OnStop);
        SubscribeLocalEvent<PocketPlayerComponent, PocketPlayerSetTimeMessage>(OnSetTime);
        SubscribeLocalEvent<PocketPlayerComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnTrackSelected(EntityUid uid, PocketPlayerComponent comp, PocketPlayerSelectTrackMessage args)
    {
        if (Audio.IsPlaying(comp.AudioStream))
            return;

        comp.SelectedTrackId = args.TrackId;
        comp.AudioStream = Audio.Stop(comp.AudioStream);
        Dirty(uid, comp);
    }

    private void OnPlay(EntityUid uid, PocketPlayerComponent comp, PocketPlayerPlayMessage args)
    {
        if (Exists(comp.AudioStream))
        {
            Audio.SetState(comp.AudioStream, AudioState.Playing);
        }
        else
        {
            comp.AudioStream = Audio.Stop(comp.AudioStream);

            if (string.IsNullOrEmpty(comp.SelectedTrackId) ||
                !_protoManager.TryIndex(comp.SelectedTrackId, out var trackProto))
                return;

            var audioParams = AudioParams.Default
                .WithMaxDistance(MaxDistance)
                .WithVolume(BaseVolume)
                .WithRolloffFactor(RolloffFactor)
                .WithReferenceDistance(ReferenceDistance);

            comp.AudioStream = Audio.PlayPvs(trackProto.Path, uid, audioParams)?.Entity;
            Dirty(uid, comp);
        }
    }

    private void OnPause(EntityUid uid, PocketPlayerComponent comp, PocketPlayerPauseMessage args)
    {
        Audio.SetState(comp.AudioStream, AudioState.Paused);
    }

    private void OnStop(EntityUid uid, PocketPlayerComponent comp, PocketPlayerStopMessage args)
    {
        Audio.SetState(comp.AudioStream, AudioState.Stopped);
        Dirty(uid, comp);
    }

    private void OnSetTime(EntityUid uid, PocketPlayerComponent comp, PocketPlayerSetTimeMessage args)
    {
        if (TryComp(args.Actor, out ActorComponent? actor))
        {
            var offset = actor.PlayerSession.Channel.Ping * 1.5f / 1000f;
            Audio.SetPlaybackPosition(comp.AudioStream, args.Time + offset);
        }
    }

    private void OnShutdown(EntityUid uid, PocketPlayerComponent comp, ComponentShutdown args)
    {
        comp.AudioStream = Audio.Stop(comp.AudioStream);
    }
}
