// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.ADT.Traits;
using Content.Shared.Stunnable;

namespace Content.Shared._Duty.Traits;

/// <summary>
/// Фрираннинг (ADT) раньше ускорял залезание на столы — ровно то же, что наш «Паркур».
/// Чтобы трейты не дублировались, фрираннер теперь умеет группироваться: быстрее встаёт
/// после сбивания с ног и тратит меньше стамины на рывок-подъём.
/// </summary>
public sealed class FreerunningSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FreerunningComponent, GetStandUpTimeEvent>(OnGetStandUpTime);
        SubscribeLocalEvent<FreerunningComponent, TryForceStandEvent>(OnTryForceStand);
    }

    private void OnGetStandUpTime(Entity<FreerunningComponent> ent, ref GetStandUpTimeEvent args)
    {
        args.DoAfterTime *= ent.Comp.StandUpTimeModifier;
    }

    private void OnTryForceStand(Entity<FreerunningComponent> ent, ref TryForceStandEvent args)
    {
        args.Stamina *= ent.Comp.ForceStandStaminaModifier;
    }
}
