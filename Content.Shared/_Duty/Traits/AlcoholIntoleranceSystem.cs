// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Drunk;

namespace Content.Shared._Duty.Traits;

public sealed partial class AlcoholIntoleranceSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damage = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AlcoholIntoleranceComponent, SharedDrunkSystem.DrunkEvent>(OnDrunk);
    }

    private void OnDrunk(Entity<AlcoholIntoleranceComponent> ent, ref SharedDrunkSystem.DrunkEvent args)
    {
        var amount = (float) args.Duration.TotalSeconds * ent.Comp.PoisonPerSecondOfDrunkenness;
        _damage.TryChangeDamage(ent.Owner, new DamageSpecifier { DamageDict = { ["Poison"] = amount } },
            interruptsDoAfters: false);
    }
}
