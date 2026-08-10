using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Block.Components;

/// <summary>
/// Штраф за блок огнестрельным оружием — 3с нельзя стрелять и нельзя выбросить/снять/поднять/
/// переэкипировать оружие в руках. Вешается при ЛЮБОЙ активации полного уровня блока оружием
/// с <c>GunComponent</c>, независимо от исхода блока. Таймер стартует в момент активации, не в
/// момент закрытия окна — независим от обычного кулдауна блока.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(BlockGunLockSystem))]
public sealed partial class BlockGunLockComponent : Component
{
    public TimeSpan EndTime;

    /// <summary>Момент последнего показанного pop-up'а "Не могу прицелиться..." — анти-спам дебаунс.</summary>
    public TimeSpan LastPopupTime;
}
