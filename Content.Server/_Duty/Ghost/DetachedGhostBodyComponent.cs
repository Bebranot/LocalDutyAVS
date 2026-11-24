namespace Content.Server._Duty.Ghost;

/// <summary>
/// Маркер тела, от которого разум был отсоединён при /ghost в живом состоянии,
/// для включения возврата в тело после его смерти или крита.
/// </summary>
[RegisterComponent]
public sealed partial class DetachedGhostBodyComponent : Component
{
    public EntityUid? Ghost;
}
