// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.RMCVehicle.Gun;

namespace Content.Shared._RMC14.Vehicle;

// _Duty: в RMC SharedGunSystem.TryGetGun сам спрашивает у техники выбранное оружие.
// У нас это сделано событием, чтобы в апстрим-файле был один короткий блок.
public sealed partial class VehicleWeaponsSystem
{
    private void InitializeDuty()
    {
        SubscribeLocalEvent<VehicleWeaponsOperatorComponent, DutyGetGunEvent>(OnOperatorGetGun);
    }

    private void OnOperatorGetGun(Entity<VehicleWeaponsOperatorComponent> ent, ref DutyGetGunEvent args)
    {
        if (args.Gun != null || ent.Comp.Vehicle is not { } vehicle)
            return;

        if (TryGetSelectedWeaponForOperator(vehicle, ent.Owner, out var weapon))
            args.Gun = weapon;
    }
}
