// _Duty: подсветка цели верба "Указать" (PointAt) — см.
// Content.Shared/_Duty/InteractionVerbs/Actions/HighlightTargetAction.cs (навешивает/продлевает),
// Content.Server/_Duty/InteractionVerbs/PointHighlightSystem.cs (снятие по таймеру),
// Content.Client/_Duty/InteractionVerbs/PointHighlightSystem.cs (шейдер обводки).
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.InteractionVerbs.Components;

/// <summary>
///     Висит на сущности, пока её обводка-хайлайт от верба "Указать" активна. Клиенту важно только
///     присутствие компонента (навешивает/снимает шейдер по ComponentStartup/Shutdown) —
///     <see cref="EndTime"/> это чисто серверная бухгалтерия, по сети не идёт.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PointHighlightComponent : Component
{
    /// <summary>Момент времени, когда подсветка снимается.</summary>
    [ViewVariables]
    public TimeSpan EndTime;
}
