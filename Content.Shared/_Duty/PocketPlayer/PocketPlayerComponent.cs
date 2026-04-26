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

// ── UI ключ ───────────────────────────────────────────────────

[Serializable, NetSerializable]
public enum PocketPlayerUiKey : byte
{
    Key,
}
