using Content.Shared._Duty.Weapon.Module.Effects;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Weapon.Module;

/// <summary>
/// Вешается на ствол: агрегирует эффекты вставленных модулей и применяет их к Gun.
/// Порт из STALKER-14 (без зум-скоупинга).
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(STSharedWeaponModuleSystem))]
public sealed partial class STWeaponModuleContainerComponent : Component
{
    [ViewVariables]
    public STWeaponModuleEffect CachedEffect;

    [ViewVariables]
    public float BaseSoundGunshotVolume;
}
