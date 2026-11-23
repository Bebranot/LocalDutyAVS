// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Video;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Video;

/// <summary>
/// Подгоняет кадр ролика под серверное время старта: автоанимация спрайта идёт с момента,
/// когда клиент создал сущность, а звук — с момента спавна на сервере.
/// </summary>
public sealed class DutyVideoVisualSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DutyVideoComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnState(Entity<DutyVideoComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (ent.Comp.StartTime == TimeSpan.Zero || !TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (!_sprite.LayerMapTryGet((ent, sprite), ent.Comp.Layer, out var index, false))
            return;

        var elapsed = (float) (_timing.CurTime - ent.Comp.StartTime).TotalSeconds;
        if (elapsed > 0f)
            _sprite.LayerSetAnimationTime((ent, sprite), index, elapsed);
    }
}
