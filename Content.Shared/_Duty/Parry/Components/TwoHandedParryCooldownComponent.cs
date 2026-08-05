namespace Content.Shared._Duty.Parry.Components;

/// <summary>
/// Единый 15с кулдаун парирующего блока (Фаза 2). Ставится при каждой активации парирующего
/// блока вне зависимости от того, дошло ли до QTE-катсцены — это же число одновременно
/// не даёт повторить катсцену чаще раза в 15 секунд.
/// </summary>
[RegisterComponent, Access(typeof(TwoHandedBlockSystem))]
public sealed partial class TwoHandedParryCooldownComponent : Component
{
    public TimeSpan EndTime;
}
