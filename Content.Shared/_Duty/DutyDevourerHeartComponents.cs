using System;
using System.Collections.Generic;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty;

/// <summary>
/// Marker on the Devourer's heart item. Using (eating) it grants
/// <see cref="DutyDevourerHeartRegenComponent"/> and consumes the item.
/// </summary>
[RegisterComponent]
public sealed partial class DutyDevourerHeartComponent : Component { }

/// <summary>
/// Permanent (until the character dies) slow healing of all damage groups for the eater.
/// </summary>
[RegisterComponent]
public sealed partial class DutyDevourerHeartRegenComponent : Component
{
    /// <summary>
    /// Heal interval in seconds.
    /// </summary>
    [DataField]
    public double HealInterval = 5;

    /// <summary>
    /// Amount of damage healed per group per tick (negative = healing).
    /// </summary>
    [DataField]
    public double HealAmount = -2;

    /// <summary>
    /// Damage groups that get healed a little bit every tick.
    /// </summary>
    [DataField]
    public List<ProtoId<DamageGroupPrototype>> HealGroups = new() { "Brute", "Burn", "Toxin", "Airloss", "Genetic" };

    [DataField]
    public TimeSpan LastHeal;
}