// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Movement.Systems;

namespace Content.Shared._Duty.Cyberware.Crew;

/// <summary>
/// _Duty: модификаторы скорости кибер-импланта экипажа — магнитные ступни (медленнее) и адреналин (быстрее).
/// Общая система, чтобы клиент предсказывал скорость так же, как сервер.
/// </summary>
public sealed partial class DutyCrewSpeedSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyMagFeetSlowComponent, RefreshMovementSpeedModifiersEvent>(OnMagFeetSpeed);
        SubscribeLocalEvent<DutyAdrenalineBoostComponent, RefreshMovementSpeedModifiersEvent>(OnBoostSpeed);
    }

    private void OnMagFeetSpeed(Entity<DutyMagFeetSlowComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.Multiplier);
    }

    private void OnBoostSpeed(Entity<DutyAdrenalineBoostComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.Multiplier);
    }
}
