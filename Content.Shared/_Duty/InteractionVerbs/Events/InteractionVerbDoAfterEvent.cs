// Портировано из Goob-Station (Content.Shared/_EinsteinEngines/InteractionVerbs), изначально Einstein Engines.
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.InteractionVerbs.Events;

[Serializable, NetSerializable]
public sealed partial class InteractionVerbDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public ProtoId<InteractionVerbPrototype>? VerbPrototype;

    [NonSerialized]
    public InteractionArgs? VerbArgs; // Используется только на сервере — если это когда-то перестанет быть правдой, переносить всё в Server и забыть.

    public InteractionVerbDoAfterEvent(ProtoId<InteractionVerbPrototype>? verbPrototype, InteractionArgs? verbArgs)
    {
        VerbPrototype = verbPrototype;
        VerbArgs = verbArgs;
    }
}
