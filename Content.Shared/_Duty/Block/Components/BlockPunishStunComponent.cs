using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Block.Components;

/// <summary>
/// Наказание за удар в полный уровень блока — на 1с полностью лочит движение, атаку, стрельбу,
/// взаимодействие, использование, бросок, поднятие и экип/разэкип предметов. Сознательно СВОЙ
/// компонент, а не ванильный <c>StunnedComponent</c> — чтобы не ловить конфликты с чужими
/// системами, завязанными именно на Stunned (например, выбивание оружия).
///
/// EndTime сетевой, чтобы клиент снимал стан по тем же часам, что и сервер, а не ждал лишний
/// круг пинга после его окончания.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(BlockPunishStunSystem))]
public sealed partial class BlockPunishStunComponent : Component
{
    [AutoNetworkedField]
    public TimeSpan EndTime;
}
