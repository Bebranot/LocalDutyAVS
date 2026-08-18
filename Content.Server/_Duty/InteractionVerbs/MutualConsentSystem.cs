// _Duty: чистит просроченные запросы согласия (Handshake/HighFive) — см.
// Content.Shared/_Duty/InteractionVerbs/Actions/MutualConsentAction.cs.
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.InteractionVerbs;
using Content.Shared._Duty.InteractionVerbs.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Duty.InteractionVerbs;

public sealed class MutualConsentSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly List<ProtoId<InteractionVerbPrototype>> _expiredBuffer = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<PendingConsentRequestComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            _expiredBuffer.Clear();
            foreach (var (verbId, request) in comp.Requests)
            {
                if (now >= request.ExpiresAt)
                    _expiredBuffer.Add(verbId);
            }

            if (_expiredBuffer.Count == 0)
                continue;

            foreach (var verbId in _expiredBuffer)
                comp.Requests.Remove(verbId);

            if (comp.Requests.Count == 0)
                RemCompDeferred<PendingConsentRequestComponent>(uid);
        }
    }
}
