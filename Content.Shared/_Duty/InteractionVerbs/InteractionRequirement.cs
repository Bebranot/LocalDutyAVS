// Портировано из Goob-Station (Content.Shared/_EinsteinEngines/InteractionVerbs), изначально Einstein Engines.
// SPDX-License-Identifier: AGPL-3.0-or-later

using JetBrains.Annotations;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.InteractionVerbs;

/// <summary>
///     Условие показа/доступности интеракт-верба. Если условие не выполнено, верб скрывается
///     (см. <see cref="InteractionVerbPrototype.HideByRequirement"/>) или показывается отключённым.
/// </summary>
[ImplicitDataDefinitionForInheritors, Serializable, NetSerializable]
[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public abstract partial class InteractionRequirement
{
    public abstract bool IsMet(InteractionArgs args, InteractionVerbPrototype proto, InteractionAction.VerbDependencies deps);
}

/// <inheritdoc cref="InteractionRequirement"/>
[Serializable, NetSerializable]
public abstract partial class InvertableInteractionRequirement : InteractionRequirement
{
    [DataField] public bool Inverted = false;
}
