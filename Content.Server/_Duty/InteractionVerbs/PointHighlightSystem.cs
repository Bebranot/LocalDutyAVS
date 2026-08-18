// _Duty: снимает подсветку верба "Указать" (PointAt) по истечении таймера — см.
// Content.Shared/_Duty/InteractionVerbs/Actions/HighlightTargetAction.cs (навешивает/продлевает),
// Content.Client/_Duty/InteractionVerbs/PointHighlightSystem.cs (шейдер обводки на клиенте).
// Паттерн — тот же таймерный Update, что и Content.Server/_Duty/FuryStimulator/FuryStimulatorSystem.cs.
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.InteractionVerbs.Components;
using Robust.Shared.Timing;

namespace Content.Server._Duty.InteractionVerbs;

public sealed class PointHighlightSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<PointHighlightComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now >= comp.EndTime)
                RemCompDeferred<PointHighlightComponent>(uid);
        }
    }
}
