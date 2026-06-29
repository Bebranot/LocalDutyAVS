using Content.Shared._Duty.Weapon.Module.Effects;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Weapon.Module;

/// <summary>
/// Помечает сущность как модуль/обвес оружия (порт из STALKER-14).
/// Layer/StatePostfix — для визуала на стволе; Effect — статы; ScopeEffect — зум (Фаза 2, не активен).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class STWeaponModuleComponent : Component
{
    [DataField, ViewVariables, AutoNetworkedField]
    public string Layer = string.Empty;

    [DataField, ViewVariables, AutoNetworkedField]
    public string StatePostfix = string.Empty;

    [DataField, ViewVariables, AutoNetworkedField]
    public STWeaponModuleEffect Effect;

    [DataField, ViewVariables, AutoNetworkedField]
    public STWeaponModuleScopeEffect? ScopeEffect;
}
