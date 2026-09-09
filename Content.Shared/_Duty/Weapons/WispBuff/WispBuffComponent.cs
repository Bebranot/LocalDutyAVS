using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Weapons.WispBuff;

/// <summary>
/// _Duty: бафф от призванного болотного огонька (жуткий фонарь с ADT, <c>ADTWispLanternComponent</c>).
/// Вешается сервером на владельца фонаря, пока висп выпущен наружу. Раньше висп выдавал ночное
/// зрение, которое по факту ничего не давало игроку — заменено на боевой бафф: часть входящего
/// урона срезается резистом (<see cref="DamageResist"/>), а бонусный режущий урон
/// (<see cref="MeleeBonusDamage"/>) следует за текущим оружием в руке (или безоружным ударом) —
/// см. <see cref="WispMeleeBonusComponent"/> и <c>SharedWispBuffSystem</c>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WispBuffComponent : Component
{
    /// <summary>Доля входящего урона, срезаемая резистом (0.05 = -5% урона).</summary>
    [DataField, AutoNetworkedField]
    public float DamageResist = 0.05f;

    /// <summary>Бонусный урон ближнего боя, добавляемый к текущему оружию/безоружному удару.</summary>
    [DataField, AutoNetworkedField]
    public DamageSpecifier MeleeBonusDamage = new()
    {
        DamageDict = new()
        {
            { "Slash", 3 },
        },
    };

    // ── Серверная служебка (не сетится) ───────────────────────

    /// <summary>
    /// Сущность, на которую сейчас навешен <see cref="WispMeleeBonusComponent"/>: текущее оружие
    /// в руке, либо сам владелец при безоружном ударе. Перевешивается при смене рук.
    /// </summary>
    [ViewVariables]
    public EntityUid? BonusedWeapon;
}
