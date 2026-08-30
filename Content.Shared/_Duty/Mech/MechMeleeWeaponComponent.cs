using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Mech;

/// <summary>
/// Метка на mech-equipment сущности: модуль ближнего боя меха (аналог энергомеча).
/// Проверяется в Content.Shared.Mech.EntitySystems.SharedMechSystem.OnGetMeleeWeapon —
/// если такой модуль установлен в мехе, GetMeleeWeaponEvent у пилота ВСЕГДА резолвится в него,
/// независимо от того, какое оборудование выбрано в UI меха (RobustToolbox не допускает
/// два независимых подписчика на один и тот же (Component, Event) для directed-событий,
/// поэтому проверка встроена прямо в штатный обработчик, а не вынесена в отдельную систему).
/// Сама механика удара (урон, анимация, attackRate) берётся из обычного MeleeWeaponComponent
/// на той же сущности — этот компонент лишь помечает "это модуль ближнего боя".
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MechMeleeWeaponComponent : Component
{
}
