using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Bitrunning.Components;

/// <summary>
/// Makes a vending machine use bitrunning points to buy items.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BitrunningPointsVendorComponent : Component;
