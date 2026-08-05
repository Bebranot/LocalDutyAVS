using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Parry.Components;

/// <summary>
/// Вешается на сущность, которая держит активное окно блока/парирования двуручным оружием.
/// Само присутствие компонента сетевое (добавление/удаление реплицируется всем в PVS) — по нему
/// клиенты рисуют иконку над головой. Поля НЕ синхронизируются (нет AutoNetworkedField) —
/// они нужны только серверу для резолва удара, клиенту достаточно факта наличия компонента.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(TwoHandedBlockSystem))]
public sealed partial class TwoHandedBlockComponent : Component
{
    /// <summary>Двуручное оружие, которым поставлен блок.</summary>
    public EntityUid Weapon;

    /// <summary>Момент времени, когда окно блока закрывается.</summary>
    public TimeSpan EndTime;

    /// <summary>
    /// true — это короткий парирующий блок (0.2с, доступен только под <see cref="JustBlockedAttackerComponent"/>),
    /// false — обычный блок (0.3с). В Фазе 1 всегда false — парирование добавляется в Фазе 2.
    /// </summary>
    public bool IsParry;

    /// <summary>
    /// Атакующий из последнего полученного AttackedEvent — кэш для сопоставления со
    /// следующим за ним синхронно DamageModifyEvent (см. TwoHandedBlockSystem).
    /// </summary>
    public EntityUid? PendingAttacker;
}
