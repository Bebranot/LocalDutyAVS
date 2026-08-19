using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.ShieldBash;

/// <summary>
/// _Duty: активный бафф «Удар по щиту». Вешается на владельца при активации способности на
/// случайное время (10-20с, см. <see cref="ShieldBashComponent"/>) и снимается досрочно, если
/// из рук уходит последний щит. Даёт:
/// <list type="bullet">
/// <item>резист ко всему урону (<see cref="DamageResist"/>) — <c>SharedShieldBashSystem</c>;</item>
/// <item>ускорение передвижения (<see cref="SpeedModifier"/>) — предсказывается клиентом;</item>
/// <item>игнор замедления от накопленного урона — через <c>IgnoreSlowOnDamageComponent</c>;</item>
/// <item>иконку-статус с обратным отсчётом (<see cref="Alert"/>);</item>
/// <item>бонус урона/скорости атаки оружия ближнего боя в свободной руке — динамически следует
/// за предметом в руке, см. <c>SharedShieldBashSystem.RefreshMeleeBonus</c>.</item>
/// </list>
/// Наложение/снятие по таймеру — в серверном <c>ShieldBashSystem</c>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShieldBashBuffComponent : Component
{
    [DataField, AutoNetworkedField]
    public float DamageResist = 0.15f;

    [DataField, AutoNetworkedField]
    public float SpeedModifier = 1.05f;

    [DataField, AutoNetworkedField]
    public float MeleeDamageMultiplier = 1.2f;

    [DataField, AutoNetworkedField]
    public float MeleeAttackRateMultiplier = 1.2f;

    [DataField]
    public ProtoId<AlertPrototype> Alert = "DutyShieldBash";

    /// <summary>Момент, когда бафф должен спасть. Сравнивается с CurTime в Update на сервере.</summary>
    [ViewVariables]
    public TimeSpan EndTime;

    /// <summary>Свободная рука, на предмет которой сейчас навешен бонус урона/скорости атаки.</summary>
    [ViewVariables]
    public EntityUid? BonusedWeapon;

    /// <summary>
    /// True, если <c>IgnoreSlowOnDamageComponent</c> добавлен именно этим баффом — чтобы не снять
    /// чужой источник (трейт/бронежилет) при спадении баффа.
    /// </summary>
    [ViewVariables]
    public bool AddedIgnoreSlowOnDamage;
}
