// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx без изменений (только зависимости overlay см. DroneVisionOverlay.cs).

using Content.Shared._Duty.Drone;
using Robust.Client.Graphics;
using Robust.Shared.Player;

namespace Content.Client._Duty.Drone;

public sealed partial class DroneVisionSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;

    private DroneVisionOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DroneVisionComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<DroneVisionComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<DroneVisionComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<DroneVisionComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);
        _overlay = new DroneVisionOverlay();
    }

    private void OnInit(Entity<DroneVisionComponent> ent, ref ComponentInit args)
    {
        if (ent.Owner == _playerManager.LocalEntity)
            _overlayManager.AddOverlay(_overlay);
    }

    private void OnShutdown(Entity<DroneVisionComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Owner == _playerManager.LocalEntity)
            _overlayManager.RemoveOverlay(_overlay);
    }

    private void OnPlayerAttached(Entity<DroneVisionComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        _overlayManager.AddOverlay(_overlay);
    }

    private void OnPlayerDetached(Entity<DroneVisionComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        _overlayManager.RemoveOverlay(_overlay);
    }
}
