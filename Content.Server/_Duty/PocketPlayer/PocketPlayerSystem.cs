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
        SubscribeLocalEvent<PocketPlayerComponent, PocketPlayerSetVolumeMessage>(OnSetVolume);
        SubscribeLocalEvent<PocketPlayerComponent, ComponentShutdown>(OnShutdown);
    }

    /// <summary>
    /// Если аудиопоток уже удаляется (например, доиграл до конца сам), чистим ссылку,
    /// чтобы не словить PVS-ошибку при обращении к умирающей сущности.
    /// </summary>
    private bool ClearIfTerminating(EntityUid uid, PocketPlayerComponent comp)
    {
        if (comp.AudioStream.HasValue && TerminatingOrDeleted(comp.AudioStream.Value))
        {
            comp.AudioStream = null;
            Dirty(uid, comp);
            return true;
        }

        return false;
    }

    private void OnTrackSelected(EntityUid uid, PocketPlayerComponent comp, PocketPlayerSelectTrackMessage args)
    {
        ClearIfTerminating(uid, comp);

        if (Audio.IsPlaying(comp.AudioStream))
            return;

        comp.SelectedTrackId = args.TrackId;
        comp.AudioStream = Audio.Stop(comp.AudioStream);
        Dirty(uid, comp);
    }

    private void OnPlay(EntityUid uid, PocketPlayerComponent comp, PocketPlayerPlayMessage args)
    {
        if (ClearIfTerminating(uid, comp))
            return;

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
                .WithVolume(GetVolumeDb(comp.Volume, comp))
                .WithRolloffFactor(RolloffFactor)
                .WithReferenceDistance(ReferenceDistance);

            comp.AudioStream = Audio.PlayPvs(trackProto.Path, uid, audioParams)?.Entity;
            Dirty(uid, comp);
        }
    }

    private void OnPause(EntityUid uid, PocketPlayerComponent comp, PocketPlayerPauseMessage args)
    {
        if (ClearIfTerminating(uid, comp))
            return;

        Audio.SetState(comp.AudioStream, AudioState.Paused);
    }

    private void OnStop(EntityUid uid, PocketPlayerComponent comp, PocketPlayerStopMessage args)
    {
        // Audio.SetState(..., Stopped) только тормозит поток, а не удаляет сущность —
        // удаляем явно через Audio.Stop(), иначе аудио-сущность утекает навсегда.
        comp.AudioStream = Audio.Stop(comp.AudioStream);
        Dirty(uid, comp);
    }

    private void OnSetTime(EntityUid uid, PocketPlayerComponent comp, PocketPlayerSetTimeMessage args)
    {
        if (ClearIfTerminating(uid, comp))
            return;

        if (TryComp(args.Actor, out ActorComponent? actor))
        {
            var offset = actor.PlayerSession.Channel.Ping * 1.5f / 1000f;
            Audio.SetPlaybackPosition(comp.AudioStream, args.Time + offset);
        }
    }

    /// <summary>
    /// Громкость авторитетно хранится на сервере и применяется к сетевому AudioComponent,
    /// поэтому изменение слышат все игроки рядом, а не только владелец плеера.
    /// </summary>
    private void OnSetVolume(EntityUid uid, PocketPlayerComponent comp, PocketPlayerSetVolumeMessage args)
    {
        if (ClearIfTerminating(uid, comp))
            return;

        comp.Volume = Math.Clamp(args.Volume, comp.MinSlider, comp.MaxSlider);
        Dirty(uid, comp);

        if (!comp.AudioStream.HasValue)
            return;

        Audio.SetVolume(comp.AudioStream, GetVolumeDb(comp.Volume, comp));
    }

    private void OnShutdown(EntityUid uid, PocketPlayerComponent comp, ComponentShutdown args)
    {
        comp.AudioStream = Audio.Stop(comp.AudioStream);
    }

    /// <summary>
    /// ClearIfTerminating чистит AudioStream только в обработчиках UI-сообщений — если трек
    /// доигрывает сам по себе (сущность аудиопотока самоуничтожается по таймеру) и никто
    /// не трогает плеер после этого, ссылка виснет навсегда и PVS долбит ошибками
    /// "Can't resolve MetaDataComponent" на каждый тик, пока плеер кому-то виден.
    /// Тот же паттерн уже чинили в JukeboxSystem.Update — переносим сюда.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<PocketPlayerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            ClearIfTerminating(uid, comp);
        }
    }
}
