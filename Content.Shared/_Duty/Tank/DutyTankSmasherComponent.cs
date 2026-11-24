// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.Tank;

/// <summary>
/// Техника с этим компонентом проламывает обычные стены, шлюзы, окна и решётки
/// (в RMC сносится только то, на чём заранее висит VehicleSmashable — у наших карт его нет).
/// Предохранитель: только внутренние, не укреплённые конструкции, и только если
/// в квадрате 3×3 вокруг цели нет космоса — чтобы танк не разгерметизировал станцию.
/// </summary>
[RegisterComponent]
public sealed partial class DutyTankSmasherComponent : Component
{
    /// <summary>Урон по цели при проломе (структурный, хватает на обычную стену).</summary>
    [DataField]
    public double DamageOnHit = 1000;

    /// <summary>Во сколько раз танк замедляется, проламывая стену.</summary>
    [DataField]
    public float SlowdownMultiplier = 0.35f;

    /// <summary>Сколько секунд держится замедление после пролома.</summary>
    [DataField]
    public float SlowdownDuration = 1.5f;

    /// <summary>
    /// Подстроки id прототипа, которые танк не ломает никогда: укреплённое, шаттлы, астероиды, хранилища.
    /// </summary>
    [DataField]
    public List<string> ForbiddenIdParts = new()
    {
        "Reinforced", "Shuttle", "Plastitanium", "Rock", "Asteroid", "Vault", "Diamond", "Blast", "HighSec", "Necro", "Cult",
    };
}
