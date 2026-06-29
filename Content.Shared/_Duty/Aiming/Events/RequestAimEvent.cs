using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Aiming.Events;

/// <summary>
/// Raised on the client to indicate it'd like to start aiming with the given gun, at the given coordinates
/// (used server-side for the minimum-distance check).
/// </summary>
[Serializable, NetSerializable]
public sealed class RequestAimEvent : EntityEventArgs
{
    public NetEntity Gun = default!;
    public NetCoordinates Coordinates;
}
