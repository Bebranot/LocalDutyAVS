using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Duty.ErpStatus;

/// <summary>
/// _Duty: текущий ЕРП-статус персонажа за этот раунд. При заспавне персонажа заполняется
/// значением по умолчанию из его профиля (<c>HumanoidCharacterProfile.ErpStatus</c>), а дальше
/// живёт отдельно от профиля — смена статуса в игре (через Escape/F10) не переписывает
/// сохранённую настройку профиля, только состояние текущего раунда.
///
/// Видим другим игрокам через осмотр (<c>ExaminedEvent</c>, подписка в <see cref="SharedErpStatusSystem"/>).
/// Смена статуса — серверно-авторитетна, с кулдауном; см.
/// <see cref="Content.Server._Duty.ErpStatus.ErpStatusSystem"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class ErpStatusComponent : Component
{
    /// <summary>
    /// Текущий ЕРП-статус персонажа.
    /// </summary>
    [DataField, AutoNetworkedField]
    public DutyErpStatus Status = DutyErpStatus.None;

    /// <summary>
    /// Момент, начиная с которого разрешена следующая смена статуса. Сравнивается сервером
    /// при обработке запроса на смену — клиент присылаемому значению не доверяется, оно лишь
    /// используется для отображения обратного отсчёта на клиенте.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextChangeAllowedAt = TimeSpan.Zero;
}
