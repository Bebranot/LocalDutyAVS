// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._RMC14.Vehicle;

// _Duty: в RMC тяжёлые хардпоинты ставят погрузчиком (PowerLoader). У нас погрузчиков нет —
// всё ставится руками, поэтому «погрузчик» всегда отсутствует, а действующее лицо — сам игрок.
public sealed partial class HardpointSlotSystem
{
    private readonly NoPowerLoader _powerLoader = new();

    private sealed class NoPowerLoader
    {
        public bool TryGetActivePowerLoader(EntityUid user, out EntityUid? loader)
        {
            loader = null;
            return false;
        }

        public bool TryGetInteractionUser(EntityUid user, out EntityUid actor)
        {
            actor = user;
            return true;
        }

        public bool CanPickupWithActiveHand(EntityUid user) => false;

        public bool TryPickupWithActiveHand(EntityUid user, EntityUid target) => false;
    }
}
