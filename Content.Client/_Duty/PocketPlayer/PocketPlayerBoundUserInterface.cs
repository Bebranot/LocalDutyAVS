// SPDX-FileCopyrightText: 2025 LocalDuty
// SPDX-License-Identifier: MIT

using Content.Shared._Duty.PocketPlayer;
using Robust.Client.Audio;
using Robust.Client.UserInterface;
using Robust.Shared.Audio.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.PocketPlayer;

public sealed class PocketPlayerBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IPrototypeManager _protoManager = default!;

    private PocketPlayerMenu? _menu;

    public PocketPlayerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<PocketPlayerMenu>();

        _menu.OnPlayPressed += playing =>
            SendMessage(playing ? new PocketPlayerPlayMessage() : new PocketPlayerPauseMessage());

        _menu.OnStopPressed += () =>
            SendMessage(new PocketPlayerStopMessage());

        _menu.OnTrackSelected += trackId =>
        {
            // Сразу обновляем UI на клиенте — не ждём ответа сервера
            if (_protoManager.TryIndex(trackId, out var proto))
            {
                var length = EntMan.System<AudioSystem>().GetAudioLength(proto.Path.Path.ToString());
                _menu?.SetSelectedTrack(proto.Name, (float) length.TotalSeconds);
            }
            SendMessage(new PocketPlayerSelectTrackMessage(trackId));
        };

        _menu.SetTime += time =>
            SendMessage(new PocketPlayerSetTimeMessage(time));

        PopulateTracks();
        Reload();
    }

    public void PopulateTracks()
    {
        _menu?.Populate(_protoManager.EnumeratePrototypes<DutyTrackPrototype>());
    }

    public void Reload()
    {
        if (_menu == null || !EntMan.TryGetComponent(Owner, out PocketPlayerComponent? player))
            return;

        _menu.SetAudioStream(player.AudioStream);

        if (_protoManager.TryIndex(player.SelectedTrackId, out var trackProto))
        {
            var length = EntMan.System<AudioSystem>().GetAudioLength(trackProto.Path.Path.ToString());
            _menu.SetSelectedTrack(trackProto.Name, (float) length.TotalSeconds);
        }
        else
        {
            _menu.SetSelectedTrack(string.Empty, 0f);
        }
    }
}
