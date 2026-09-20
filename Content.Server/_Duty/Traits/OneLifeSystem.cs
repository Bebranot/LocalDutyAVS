// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Traits;
using Content.Shared.Gibbing;
using Content.Shared.Mobs;

namespace Content.Server._Duty.Traits;

public sealed partial class OneLifeSystem : EntitySystem
{
    [Dependency] private readonly GibbingSystem _gibbing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OneLifeComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(Entity<OneLifeComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            _gibbing.Gib(ent, dropGiblets: false);
    }
}
