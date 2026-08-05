namespace Content.Shared._Duty.Parry.Components;

/// <summary>
/// Кулдаун обычного блока после закрытия окна. Длительность зависит от ХП на момент старта
/// (см. TwoHandedBlockSystem) — 0.8с на полном ХП, 3.5с на грани крита.
/// Не сетевой — клиенту достаточно того, что кнопка молча не сработает до истечения EndTime.
/// </summary>
[RegisterComponent, Access(typeof(TwoHandedBlockSystem))]
public sealed partial class TwoHandedBlockCooldownComponent : Component
{
    public TimeSpan EndTime;
}
