using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Aiming.Events;

/// <summary>
/// Raised on the client to indicate it'd like to stop aiming.
/// </summary>
[Serializable, NetSerializable]
public sealed class RequestStopAimEvent : EntityEventArgs;
