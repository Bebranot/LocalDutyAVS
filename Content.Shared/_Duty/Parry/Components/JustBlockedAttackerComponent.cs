namespace Content.Shared._Duty.Parry.Components;

/// <summary>
/// Служебный маркер: сущность только что ударила по чужому активному блоку и сейчас отбывает
/// 0.8с наказание-стан. Пока этот компонент жив, сущность имеет право поставить короткий
/// парирующий блок (Фаза 2). Выдаётся ТОЛЬКО из обычного разрешения блока — никогда из исходов
/// QTE-катсцены, иначе возможны бесконечные цепочки повторных парирований.
/// Не сетевой — чисто серверное состояние, клиенту знать об этом не нужно.
/// </summary>
[RegisterComponent, Access(typeof(TwoHandedBlockSystem))]
public sealed partial class JustBlockedAttackerComponent : Component
{
    /// <summary>Кто именно заблокировал удар (единственный, чья атака в окне парирующего блока запускает QTE).</summary>
    public EntityUid Blocker;

    /// <summary>Момент, когда маркер (и право на парирующий блок) истекает.</summary>
    public TimeSpan ExpireAt;
}
