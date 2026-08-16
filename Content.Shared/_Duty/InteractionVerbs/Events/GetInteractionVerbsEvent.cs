// Портировано из Goob-Station (Content.Shared/_EinsteinEngines/InteractionVerbs), изначально Einstein Engines.
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.InteractionVerbs.Events;

/// <summary>
///     Кидается напрямую на сущность-пользователя, чтобы дать ей заявить дополнительные интеракт-вербы.
///     В отличие от InteractionVerbsComponent (какие вербы можно применить К сущности), это событие
///     определяет, какие вербы сущность может применять САМА.<br/><br/>
///
///     Кидается до проверок IsAllowed у любого из вербов.
/// </summary>
[ByRefEvent]
public sealed class GetInteractionVerbsEvent(List<ProtoId<InteractionVerbPrototype>> verbs)
{
    public List<ProtoId<InteractionVerbPrototype>> Verbs = verbs;

    public bool Add(ProtoId<InteractionVerbPrototype> verb)
    {
        if (Verbs.Contains(verb))
            return false;

        Verbs.Add(verb);
        return true;
    }
}
