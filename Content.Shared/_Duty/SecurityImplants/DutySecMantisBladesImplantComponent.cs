using Content.Shared.ADT.MantisDaggers;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.SecurityImplants;

/// <summary>
/// Ослабленный СБ-клон синдикатских Клинков Богомола (см. <see cref="MantisDaggersImplantComponent"/>).
/// Отдельный имплант-прототип: через Content.Server._Duty.SecurityImplants.DutySecMantisBladesImplantSystem
/// выдаёт штатный MantisDaggersComponent, но настроенный на собственное (более слабое) оружие
/// DutySecMantisBlades вместо синдикатского ADTMantisDaggers.
/// </summary>
[RegisterComponent]
[NetworkedComponent]
public sealed partial class DutySecMantisBladesImplantComponent : Component
{
}
