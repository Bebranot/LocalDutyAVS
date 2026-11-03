using Content.Shared._Duty.Weapons.Module.Effects;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Weapons.Module;

/// <summary>
/// Вешается на ствол: агрегирует эффекты вставленных модулей и применяет их к Gun.
/// Порт из STALKER-14 (без зум-скоупинга).
/// </summary>
/// <remarks>
/// _Duty: кэш эффекта сетевой — клиент сам пересчитывает модификаторы оружия (прицеливание,
/// вилд) и без него считал ствол «голым»: предсказанный темп стрельбы и разброс расходились
/// с серверными.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(STSharedWeaponModuleSystem))]
public sealed partial class STWeaponModuleContainerComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public STWeaponModuleEffect CachedEffect = new();

    [ViewVariables, AutoNetworkedField]
    public float BaseSoundGunshotVolume;
}
