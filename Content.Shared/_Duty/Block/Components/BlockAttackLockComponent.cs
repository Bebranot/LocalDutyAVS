using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Block.Components;

/// <summary>
/// Короткий (0.2с) лок атаки/стрельбы сразу после закрытия окна блока — вешается при ЛЮБОМ
/// закрытии окна (обычном или досрочном), независимо от исхода.
///
/// Сетевой: и атака, и стрельба предиктятся клиентом, так что без этого компонента клиент
/// проигрывал бы замах, который сервер тут же отменит.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(BlockSystem))]
public sealed partial class BlockAttackLockComponent : Component
{
    [AutoNetworkedField]
    public TimeSpan EndTime;
}
