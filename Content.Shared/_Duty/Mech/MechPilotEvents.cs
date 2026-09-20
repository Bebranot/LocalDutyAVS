// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: у Onyx это же приходит из общей Vehicle-системы (OnVehicleEnteredEvent/ExitedEvent),
// которой в нашем движке нет — мехи ещё не переведены на неё. Заводим свою пару событий и
// поднимаем их напрямую из SharedMechSystem.TryInsert/TryEject.

namespace Content.Shared._Duty.Mech;

[ByRefEvent]
public readonly record struct MechPilotEnteredEvent(EntityUid Mech, EntityUid Pilot);

[ByRefEvent]
public readonly record struct MechPilotExitedEvent(EntityUid Mech, EntityUid Pilot);
