using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Block.Components;

/// <summary>
/// Кулдаун блока после закрытия окна — висит на игроке (не на оружии), пока он есть, блок
/// недоступен ни с каким предметом. Длительность: 3.5с, если за окно хоть раз попали (любой
/// уровень), иначе 1с.
///
/// Сетевой: клиент должен знать про кулдаун, иначе он предиктит новое окно блока (звук, иконка,
/// замедление), а сервер его отклоняет — получается рывок.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(BlockSystem))]
public sealed partial class BlockCooldownComponent : Component
{
    [AutoNetworkedField]
    public TimeSpan EndTime;

    /// <summary>
    /// Момент последней строки "блок ещё не восстановился" — анти-спам дебаунс. Строку шлёт
    /// только сервер, поэтому поле серверное.
    /// </summary>
    public TimeSpan LastNoticeTime;
}
