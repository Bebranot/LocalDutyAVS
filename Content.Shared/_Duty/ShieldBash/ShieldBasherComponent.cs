using Robust.Shared.GameStates;

namespace Content.Shared._Duty.ShieldBash;

/// <summary>
/// _Duty: вешается на владельца, пока в руках есть хотя бы один щит с <see cref="ShieldBashComponent"/>.
/// Держит личный (не привязанный к конкретному предмету) кулдаун способности — переживает
/// смену щита, см. <see cref="ShieldBashComponent.Cooldown"/>. Снимается, когда из рук уходит
/// последний щит (см. <c>SharedShieldBashSystem</c>).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ShieldBasherComponent : Component
{
    /// <summary>Момент, когда снова можно активировать способность.</summary>
    [ViewVariables]
    public TimeSpan NextBashTime = TimeSpan.Zero;

    /// <summary>Щит, через который сейчас выдан Action (первый найденный подходящий в руках).</summary>
    [ViewVariables]
    public EntityUid? GrantingShield;
}
