namespace Content.Shared._Duty.Block.Components;

/// <summary>
/// Короткий (0.2с) лок атаки/стрельбы сразу после закрытия окна блока — вешается при ЛЮБОМ
/// закрытии окна (обычном или досрочном), независимо от исхода. Не сетевой — чисто игровое
/// ограничение, отдельного UI под него не предусмотрено (слишком короткое, чтобы иметь смысл).
/// </summary>
[RegisterComponent, Access(typeof(BlockSystem))]
public sealed partial class BlockAttackLockComponent : Component
{
    public TimeSpan EndTime;
}
