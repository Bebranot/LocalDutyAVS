// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx без изменений.

using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Clothing;

public sealed partial class CleanDirtEntityEffectSystem
    : EntityEffectSystem<ClothingDirtableComponent, CleanDirt>
{
    [Dependency] private ClothingDirtSystem _dirt = default!;

    protected override void Effect(Entity<ClothingDirtableComponent> entity, ref EntityEffectEvent<CleanDirt> args)
    {
        _dirt.TryCleanDirt(entity, args.Effect.Multiplier * args.Scale, entity.Comp);
    }
}

public sealed partial class CleanDirt : EntityEffectBase<CleanDirt>
{
    [DataField]
    public float Multiplier = 1f;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("entity-effect-guidebook-clean-dirt",
            ("chance", Probability),
            ("multiplier", Multiplier));
}
