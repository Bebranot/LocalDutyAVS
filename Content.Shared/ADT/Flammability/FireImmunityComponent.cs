using Robust.Shared.GameStates;

namespace Content.Shared.ADT.Flammability;

[RegisterComponent, NetworkedComponent]
public sealed partial class FireImmunityComponent : Component
{
    public override bool SessionSpecific => true;

    /// <summary>_Duty: момент, с которого сущность непрерывно горит (FlammableComponent.OnFire).
    /// Огнеиммунные (новакиды) не получают урон от огня и раньше вообще не тухли сами по себе —
    /// теперь после <see cref="Content.Server.Atmos.EntitySystems.FlammableSystem"/>-таймаута
    /// стакам огня всё же даём угасать, чтобы персонаж мог полностью потухнуть. Сбрасывается,
    /// как только OnFire становится false. Не сетится — чисто серверная служебка.</summary>
    [ViewVariables]
    public TimeSpan? OnFireSince;
}