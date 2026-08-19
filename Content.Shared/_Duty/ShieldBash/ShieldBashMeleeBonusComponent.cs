using Robust.Shared.GameStates;

namespace Content.Shared._Duty.ShieldBash;

/// <summary>
/// _Duty: временный маркер баффа «Удар по щиту» на оружии в свободной руке. Вешается сервером
/// на текущее оружие, пока владелец под баффом (см. <see cref="ShieldBashBuffComponent"/>), и
/// перевешивается на новое оружие при смене содержимого свободной руки. Собственный компонент
/// (а не переиспользование ванильных <c>BonusMeleeDamageComponent</c>/<c>BonusMeleeAttackRateComponent</c>),
/// потому что у тех запись полей ограничена <c>SharedMeleeWeaponSystem</c> — см. RA0002.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShieldBashMeleeBonusComponent : Component
{
    /// <summary>Множитель урона ближнего боя (1.2 = +20%).</summary>
    [DataField, AutoNetworkedField]
    public float DamageMultiplier = 1.2f;

    /// <summary>Множитель скорости атаки ближнего боя (1.2 = +20%).</summary>
    [DataField, AutoNetworkedField]
    public float AttackRateMultiplier = 1.2f;
}
