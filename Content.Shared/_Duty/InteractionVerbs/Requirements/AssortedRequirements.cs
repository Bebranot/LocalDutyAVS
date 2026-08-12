// Портировано из Goob-Station (Content.Shared/_EinsteinEngines/InteractionVerbs), изначально Einstein Engines.
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Bed.Sleep;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Whitelist;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.InteractionVerbs.Requirements;

/// <summary>
///     Требует, чтобы цель проходила вайтлист и не проходила блэклист.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class EntityWhitelistRequirement : InteractionRequirement
{
    [DataField] public EntityWhitelist Whitelist = new(), Blacklist = new();

    public override bool IsMet(InteractionArgs args, InteractionVerbPrototype proto, InteractionAction.VerbDependencies deps)
    {
        return deps.WhitelistSystem.IsValid(Whitelist, args.Target) &&
               !deps.WhitelistSystem.IsValid(Blacklist, args.Target);
    }
}

/// <summary>
///     Требует, чтобы цель была мобом в определённом состоянии. С Inverted — требует обратного.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class MobStateRequirement : InvertableInteractionRequirement
{
    [DataField] public List<MobState> AllowedStates = new();

    public override bool IsMet(InteractionArgs args, InteractionVerbPrototype proto, InteractionAction.VerbDependencies deps)
    {
        if (!deps.EntMan.TryGetComponent<MobStateComponent>(args.Target, out var state))
            return false;

        return AllowedStates.Contains(state.CurrentState) ^ Inverted;
    }
}

/// <summary>
///     Требует, чтобы цель находилась в определённом положении (стоя/лёжа/сбита с ног/спит).
/// </summary>
[Serializable, NetSerializable]
public sealed partial class StandingStateRequirement : InteractionRequirement
{
    [DataField] public bool AllowStanding, AllowLaying, AllowKnockedDown, AllowSleep;

    public override bool IsMet(InteractionArgs args, InteractionVerbPrototype proto, InteractionAction.VerbDependencies deps)
    {
        if (deps.EntMan.HasComponent<SleepingComponent>(args.Target) && !AllowSleep)
            return false;
        if (deps.EntMan.HasComponent<KnockedDownComponent>(args.Target))
            return AllowKnockedDown;

        if (!deps.EntMan.TryGetComponent<StandingStateComponent>(args.Target, out var state))
            return false;

        return state.Standing && AllowStanding
               || !state.Standing && AllowLaying;
    }
}

/// <summary>
///     Требует, чтобы целью был сам пользователь.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SelfTargetRequirement : InvertableInteractionRequirement
{
    public override bool IsMet(InteractionArgs args, InteractionVerbPrototype proto, InteractionAction.VerbDependencies deps)
    {
        return (args.Target == args.User) ^ Inverted;
    }
}
