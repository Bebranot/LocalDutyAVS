using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Block.Components;

/// <summary>
/// Штраф за блок огнестрельным оружием — 3с нельзя стрелять и нельзя выбросить/снять/поднять/
/// переэкипировать оружие в руках. Вешается при ЛЮБОЙ активации полного уровня блока оружием
/// с <c>GunComponent</c>, независимо от исхода блока. Таймер стартует в момент активации, не в
/// момент закрытия окна — независим от обычного кулдауна блока.
///
/// EndTime сетевой: стрельба предиктится, и клиент должен отказывать в ней сам, а не узнавать
/// об отказе от сервера.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(BlockGunLockSystem))]
public sealed partial class BlockGunLockComponent : Component
{
    [AutoNetworkedField]
    public TimeSpan EndTime;

    /// <summary>
    /// Момент последней строки "не могу прицелиться" в чат — анти-спам дебаунс. Строку шлёт
    /// только сервер, поэтому поле серверное.
    /// </summary>
    public TimeSpan LastNoticeTime;
}
