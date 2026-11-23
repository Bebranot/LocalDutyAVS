using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.GameStates;

namespace Content.Shared.ADT.Traits;

[RegisterComponent]
[NetworkedComponent]
public sealed partial class FreerunningComponent : Component
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Modifier = 0.6f;

    // _Duty-start: фрираннинг = умение группироваться при падении (отличие от «Паркура»)
    /// <summary>Множитель времени подъёма после сбивания с ног.</summary>
    [DataField]
    public float StandUpTimeModifier = 0.5f;

    /// <summary>Множитель стамины, которую тратит рывок-подъём.</summary>
    [DataField]
    public float ForceStandStaminaModifier = 0.6f;
    // _Duty-end
}
