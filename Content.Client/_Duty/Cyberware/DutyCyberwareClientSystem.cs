// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Cyberware;
using Robust.Client.Player;

namespace Content.Client._Duty.Cyberware;

/// <summary>
/// _Duty: клиентская половина базы кибера — отдаёт HUD состояние киберсистемы локального игрока.
/// </summary>
public sealed partial class DutyCyberwareClientSystem : SharedDutyCyberwareSystem
{
    [Dependency] private IPlayerManager _player = default!;

    public bool TryGetLocalHost(out EntityUid uid, out DutyCyberHostComponent host)
    {
        if (_player.LocalEntity is { } local && TryComp(local, out DutyCyberHostComponent? comp))
        {
            uid = local;
            host = comp;
            return true;
        }

        uid = default;
        host = default!;
        return false;
    }

    public TimeSpan CurTime => Timing.CurTime;
}
