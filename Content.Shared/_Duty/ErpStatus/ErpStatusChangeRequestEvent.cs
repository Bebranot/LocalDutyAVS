using Robust.Shared.Serialization;

namespace Content.Shared._Duty.ErpStatus;

/// <summary>
/// Клиент -&gt; сервер: запрос на смену ЕРП-статуса своего персонажа. Сервер сам находит
/// присоединённую сущность отправителя и сам проверяет кулдаун — клиентское значение
/// <see cref="ErpStatusComponent.NextChangeAllowedAt"/> используется только для UI.
/// </summary>
[Serializable, NetSerializable]
public sealed class ErpStatusChangeRequestEvent : EntityEventArgs
{
    public readonly DutyErpStatus NewStatus;

    public ErpStatusChangeRequestEvent(DutyErpStatus newStatus)
    {
        NewStatus = newStatus;
    }
}
