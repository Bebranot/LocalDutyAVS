// SPDX-FileCopyrightText: 2026 LocalDuty
// SPDX-License-Identifier: MIT

using Robust.Client.Placement;
using Robust.Shared.Map;

namespace Content.Client._Duty.SpawnMenu;

/// <summary>
/// Перехватывает клик размещения, чтобы вместо движкового MsgPlacement (он ходит через
/// песочницу и требует прав админа) ушёл наш собственный запрос на сервер.
/// </summary>
public sealed class DutySpawnPlacementHijack(DutySpawnMenuSystem system, string proto) : PlacementHijack
{
    public override bool CanRotate => false;

    public override bool HijackPlacementRequest(EntityCoordinates coordinates)
    {
        system.RequestSpawn(proto, coordinates);
        return true;
    }
}
