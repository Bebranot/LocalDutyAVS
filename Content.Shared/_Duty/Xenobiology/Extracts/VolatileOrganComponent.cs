namespace Content.Shared._Duty.Xenobiology.Extracts;

/// <summary>
/// Organ that periodically zaps its owner (and nearby targets) with lightning arcs while implanted.
/// Ported from Space Onyx.
/// </summary>
[RegisterComponent]
public sealed partial class VolatileOrganComponent : Component
{
    [DataField]
    public int ArcDepth = 1;

    [DataField]
    public int MaxLightningArcs = 3;

    [DataField]
    public TimeSpan MinInterval = TimeSpan.FromSeconds(90);

    [DataField]
    public TimeSpan MaxInterval = TimeSpan.FromSeconds(300);

    [DataField]
    public float Range = 5f;

    [ViewVariables]
    public TimeSpan NextArc;
}

/// <summary>
/// Marker linking a body to the <see cref="VolatileOrganComponent"/> currently implanted in it.
/// </summary>
[RegisterComponent]
public sealed partial class VolatileOrganUserComponent : Component
{
    [ViewVariables]
    public EntityUid Organ;
}
