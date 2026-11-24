using Content.Shared._RMC14.Stun;
using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Vehicle;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleSqueezeUnderComponent : Component
{
    [DataField, AutoNetworkedField]
    public RMCSizes MinBlockingSize = RMCSizes.Big;
}
