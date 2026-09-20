namespace Content.Shared._Duty;

/// <summary>
/// Marker component for tail segments of the Devourer that forwards damage to the head.
/// </summary>
[RegisterComponent]
public sealed partial class DutyDevourerSegmentComponent : Component { }

/// <summary>
/// Marker component for the Devourer head: instantly kills any living creature it physically rams.
/// </summary>
[RegisterComponent]
public sealed partial class DutyDevourerRammingComponent : Component { }