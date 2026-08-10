namespace Content.Shared._Duty.Block.Components;

/// <summary>
/// Служебный маркер на оружии, которым сейчас держат блок — вешается/снимается вместе с
/// <see cref="BlockComponent"/> на пользователе. Нужен отдельным компонентом (а не подпиской прямо
/// на <c>WieldableComponent</c>), потому что на пару (компонент, событие) в RobustToolbox допускается
/// только один подписчик через SubscribeLocalEvent, а <c>ItemUnwieldedEvent</c> для
/// <c>WieldableComponent</c> уже занят <c>SharedWieldableSystem</c>.
/// </summary>
[RegisterComponent, Access(typeof(BlockSystem))]
public sealed partial class BlockWeaponComponent : Component
{
    /// <summary>Кто держит блок этим оружием/телом.</summary>
    public EntityUid Blocker;
}
