using System.Linq;
using Content.Server.Database;
using Content.Shared.ADT.CCVar;
using Content.Shared.Administration;
using Robust.Server;

namespace Content.Server.Connection;

public sealed partial class ConnectionManager
{
    [Dependency] private readonly IBaseServer _baseServer = default!;

    private bool IsHostReservedSlotsDenied(Admin? adminData)
    {
        var reserved = _cfg.GetCVar(ADTCCVars.HostReservedSlots);
        if (reserved <= 0 || _plyMgr.PlayerCount < _baseServer.MaxPlayers - reserved)
            return false;

        return !HasHostFlag(adminData);
    }

    // _Duty: права из БД разбирает менеджер админов: в БД лежат и узлы дерева, прежний разбор падал на их id
    private bool HasHostFlag(Admin? adminData)
    {
        return _adminManager.HasHostFlag(adminData);
    }
}
