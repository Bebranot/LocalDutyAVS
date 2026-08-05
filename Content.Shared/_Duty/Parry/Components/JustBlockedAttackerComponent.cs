using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Parry.Components;

/// <summary>
/// Наказание за удар по чужому активному блоку: сущность не может ни двигаться, ни атаковать,
/// ни взаимодействовать, но — в отличие от обычного стана — НЕ роняет предметы из рук.
///
/// Обычный StunnedComponent здесь не подходит: SharedStunSystem.OnStunnedSuccessfully рассылает
/// DropHandItemsEvent, из-за чего атакующий терял оружие. Механика этого не предполагает —
/// нужно ровно «не может атаковать, только стоять на месте».
///
/// Пока компонент жив, сущность имеет право поставить короткий парирующий блок (Фаза 2).
/// Выдаётся ТОЛЬКО из обычного разрешения блока — никогда из исходов QTE-катсцены, иначе
/// возможны бесконечные цепочки повторных парирований.
///
/// Сетевой: клиент обязан знать о запрете, иначе он предскажет движение и его отбросит назад.
/// Поля не синхронизируются — их читает только сервер.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(TwoHandedBlockSystem))]
public sealed partial class JustBlockedAttackerComponent : Component
{
    /// <summary>Кто именно заблокировал удар (единственный, чья атака в окне парирующего блока запускает QTE).</summary>
    public EntityUid Blocker;

    /// <summary>Момент, когда наказание (и право на парирующий блок) истекает.</summary>
    public TimeSpan ExpireAt;
}
