using Content.Shared._Duty.ErpStatus;

namespace Content.Client._Duty.ErpStatus;

/// <summary>
/// _Duty: клиентская часть ЕРП-статуса. Осмотр обрабатывается в <see cref="SharedErpStatusSystem"/>;
/// эта система только отправляет запрос на смену статуса по нажатию в UI.
/// </summary>
public sealed class ErpStatusSystem : SharedErpStatusSystem
{
    public void RequestChange(DutyErpStatus newStatus)
    {
        RaiseNetworkEvent(new ErpStatusChangeRequestEvent(newStatus));
    }
}
