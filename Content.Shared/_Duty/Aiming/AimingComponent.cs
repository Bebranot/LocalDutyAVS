using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Aiming;

/// <summary>
/// Вешается на персонажа во время активного прицеливания. Удаляется при сбросе/завершении прицеливания.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AimingComponent : Component
{
    /// <summary>Оружие, из которого прицеливается персонаж.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid Gun;

    /// <summary>Прицеливание было начато в лежачем положении (выше FOV, нельзя встать, лок на движение по выходу).</summary>
    [DataField, AutoNetworkedField]
    public bool IsProne;
}
