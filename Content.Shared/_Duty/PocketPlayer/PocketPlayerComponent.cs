// SPDX-FileCopyrightText: 2025 LocalDuty
// SPDX-License-Identifier: MIT

using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.PocketPlayer;

[NetworkedComponent, RegisterComponent, AutoGenerateComponentState(true)]
public sealed partial class PocketPlayerComponent : Component
{
    /// <summary>
    /// Текущий выбранный трек.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<DutyTrackPrototype>? SelectedTrackId;

    /// <summary>
    /// Сущность аудиопотока.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? AudioStream;

    /// <summary>
    /// Громкость плеера в единицах слайдера (см. <see cref="MinSlider"/>/<see cref="MaxSlider"/>).
    /// Сетевое поле — сервер авторитетно задаёт итоговую громкость аудиопотока
    /// (<see cref="Content.Shared._Duty.PocketPlayer.SharedPocketPlayerSystem.MapToRange"/>),
    /// поэтому громкость слышат одинаково все игроки рядом, а не только владелец плеера.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Volume = 40f;

    [DataField]
    public float MinVolume = -20f;

    [DataField]
    public float MaxVolume = 0f;

    [DataField]
    public float MinSlider = 0f;

    [DataField]
    public float MaxSlider = 100f;
}

// ── Сообщения UI ─────────────────────────────────────────────

[Serializable, NetSerializable]
public sealed class PocketPlayerPlayMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class PocketPlayerPauseMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class PocketPlayerStopMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class PocketPlayerSelectTrackMessage(ProtoId<DutyTrackPrototype> trackId) : BoundUserInterfaceMessage
{
    public ProtoId<DutyTrackPrototype> TrackId { get; } = trackId;
}

[Serializable, NetSerializable]
public sealed class PocketPlayerSetTimeMessage(float time) : BoundUserInterfaceMessage
{
    public float Time { get; } = time;
}

[Serializable, NetSerializable]
public sealed class PocketPlayerSetVolumeMessage(float volume) : BoundUserInterfaceMessage
{
    public float Volume { get; } = volume;
}

// ── UI ключ ───────────────────────────────────────────────────

[Serializable, NetSerializable]
public enum PocketPlayerUiKey : byte
{
    Key,
}
