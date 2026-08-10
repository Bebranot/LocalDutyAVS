namespace Content.Shared._Duty.Block.Components;

/// <summary>
/// Кулдаун блока после закрытия окна — висит на игроке (не на оружии), пока он есть, блок
/// недоступен ни с каким предметом. Длительность: 3.5с, если за окно хоть раз попали (любой
/// уровень), иначе 1с. Не сетевой — клиенту достаточно того, что кнопка не сработает до
/// истечения EndTime, а о причине сервер сообщает серой строкой в чат.
/// </summary>
[RegisterComponent, Access(typeof(BlockSystem))]
public sealed partial class BlockCooldownComponent : Component
{
    public TimeSpan EndTime;

    /// <summary>Момент последней строки "блок ещё не восстановился" — анти-спам дебаунс.</summary>
    public TimeSpan LastNoticeTime;
}
