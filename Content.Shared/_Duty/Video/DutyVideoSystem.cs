// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Timing;

namespace Content.Shared._Duty.Video;

public sealed class DutyVideoSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DutyVideoComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<DutyVideoComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.StartTime = _timing.CurTime;
        Dirty(ent);
    }
}
