using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Weapons.WispBuff;

/// <summary>
/// _Duty: маркер бонуса урона от <see cref="WispBuffComponent"/> на текущем оружии владельца
/// (или на самом владельце при безоружном ударе). Перевешивается при смене оружия в руке —
/// см. <c>WispBuffSystem</c>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WispMeleeBonusComponent : Component
{
    /// <summary>Урон, добавляемый к удару этим оружием. Копируется из <see cref="WispBuffComponent.MeleeBonusDamage"/>.</summary>
    [DataField, AutoNetworkedField]
    public DamageSpecifier BonusDamage = new();
}
