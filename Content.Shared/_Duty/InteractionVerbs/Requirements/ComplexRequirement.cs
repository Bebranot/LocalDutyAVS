// Портировано из Goob-Station (Content.Shared/_EinsteinEngines/InteractionVerbs), изначально Einstein Engines.
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.InteractionVerbs.Requirements;

/// <summary>
///     Требование, объединяющее несколько других требований.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class ComplexRequirement : InteractionRequirement
{
    [DataField]
    public List<InteractionRequirement> Requirements = new();

    /// <summary>
    ///     Если true, должны выполниться все требования (И). Иначе — достаточно одного (ИЛИ).
    /// </summary>
    [DataField]
    public bool RequireAll = true;

    public override bool IsMet(InteractionArgs args, InteractionVerbPrototype proto, InteractionAction.VerbDependencies deps)
    {
        return RequireAll
            ? Requirements.All(r => r.IsMet(args, proto, deps))
            : Requirements.Any(r => r.IsMet(args, proto, deps));
    }
}
