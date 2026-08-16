// Портировано из Goob-Station (Content.Shared/_EinsteinEngines/InteractionVerbs), изначально Einstein Engines.
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.InteractionVerbs.Events;

/// <summary>
///     Кидается на исполнителя верба и на его цель, чтобы решить, разрешён ли он.
///     Кидается только если собственная проверка CanPerform верба вернула true.
/// </summary>
[ByRefEvent]
public sealed class InteractionVerbAttemptEvent(InteractionVerbPrototype proto, InteractionArgs args) : CancellableEntityEventArgs
{
    public bool Handled { get; set; } = false;

    public InteractionVerbPrototype Proto => proto;
    public InteractionArgs Args => args;
}
