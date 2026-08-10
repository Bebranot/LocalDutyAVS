using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Block.Components;

/// <summary>
/// Наказание за удар в полный уровень блока — на 1с полностью лочит движение, атаку, стрельбу,
/// взаимодействие, использование, бросок, поднятие и экип/разэкип предметов. Сознательно СВОЙ
/// компонент, а не ванильный <c>StunnedComponent</c> — чтобы не ловить конфликты с чужими
/// системами, завязанными именно на Stunned (например, выбивание оружия). Сетевой — атакующему
/// нужно знать о своём состоянии сразу же для локального предикшна ввода.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(BlockPunishStunSystem))]
public sealed partial class BlockPunishStunComponent : Component
{
    public TimeSpan EndTime;
}
