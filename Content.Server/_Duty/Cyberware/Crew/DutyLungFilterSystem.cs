// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Body.Systems;
using Content.Shared.Atmos;

namespace Content.Server._Duty.Cyberware.Crew;

/// <summary>
/// _Duty: «Лёгочный фильтр» на носителе (выдаётся имплантом через DutyCyberGrant).
/// Вырезает часть вредных газов из вдоха ещё до того, как лёгкие их получат.
/// </summary>
[RegisterComponent]
public sealed partial class DutyLungFilterHostComponent : Component
{
    /// <summary>Какая доля вредного газа в вдохе задерживается фильтром.</summary>
    [DataField]
    public float Filtered = 0.6f;

    [DataField]
    public List<Gas> Gases = new()
    {
        Gas.Plasma,
        Gas.Tritium,
        Gas.Ammonia,
        Gas.Frezon,
        Gas.BZ,
        Gas.Nitrium,
        Gas.Hydrogen,
    };
}

public sealed partial class DutyLungFilterSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        // Фильтр должен успеть до дыхательной системы, иначе лёгкие уже получат газ.
        SubscribeLocalEvent<DutyLungFilterHostComponent, InhaledGasEvent>(OnInhaled, before: new[] { typeof(RespiratorSystem) });
    }

    private void OnInhaled(Entity<DutyLungFilterHostComponent> ent, ref InhaledGasEvent args)
    {
        var keep = 1f - Math.Clamp(ent.Comp.Filtered, 0f, 1f);
        foreach (var gas in ent.Comp.Gases)
        {
            args.Gas.AdjustMoles(gas, -args.Gas.GetMoles(gas) * (1f - keep));
        }
    }
}
