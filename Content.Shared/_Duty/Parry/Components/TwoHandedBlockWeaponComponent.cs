namespace Content.Shared._Duty.Parry.Components;

/// <summary>
/// Служебный маркер на оружии, которым сейчас держат блок — вешается/снимается вместе с
/// TwoHandedBlockComponent на пользователе. Нужен отдельным компонентом (а не подпиской прямо
/// на WieldableComponent), потому что на пару (компонент, событие) в RobustToolbox допускается
/// только один подписчик через SubscribeLocalEvent, а ItemUnwieldedEvent для WieldableComponent
/// уже занят SharedWieldableSystem.
/// </summary>
[RegisterComponent, Access(typeof(TwoHandedBlockSystem))]
public sealed partial class TwoHandedBlockWeaponComponent : Component
{
    /// <summary>Кто держит блок этим оружием.</summary>
    public EntityUid Blocker;
}
