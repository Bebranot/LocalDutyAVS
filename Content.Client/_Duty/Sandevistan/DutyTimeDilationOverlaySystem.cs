// SPDX-FileCopyrightText: 2026 LocalDuty
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Sandevistan;
using Content.Shared.ADT.Sandevistan;
using Content.Shared.Ghost.Components;
using Robust.Client.Graphics;
using Robust.Shared.Player;

namespace Content.Client._Duty.Sandevistan;

/// <summary>
/// _Duty: управляет подключением <see cref="DutyTimeDilationOverlay"/> для локального замедленного игрока.
/// </summary>
public sealed class DutyTimeDilationOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayMan = default!;
    [Dependency] private readonly ISharedPlayerManager _playerMan = default!;

    private DutyTimeDilationOverlay _overlay = default!;
    private bool _overlayActive;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesOutsidePrediction = true;
        _overlay = new DutyTimeDilationOverlay();

        SubscribeLocalEvent<SandevistanTimeDilatedComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<SandevistanTimeDilatedComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        if (_overlayActive)
        {
            _overlayMan.RemoveOverlay(_overlay);
            _overlayActive = false;
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var player = _playerMan.LocalEntity;
        var active = player is { Valid: true }
                     && HasComp<SandevistanTimeDilatedComponent>(player.Value)
                     && !HasComp<GhostComponent>(player.Value)
                     && !HasComp<ActiveSandevistanUserComponent>(player.Value);

        if (active)
            EnsureOverlayAdded();

        if (!_overlayActive)
            return;

        _overlay.UpdateFade(frameTime, active);

        if (!active && _overlay.Strength <= 0.001f)
        {
            _overlayMan.RemoveOverlay(_overlay);
            _overlayActive = false;
        }
    }

    private void OnPlayerAttached(EntityUid uid, SandevistanTimeDilatedComponent component, LocalPlayerAttachedEvent args)
    {
        if (!HasComp<GhostComponent>(uid) && !HasComp<ActiveSandevistanUserComponent>(uid))
            EnsureOverlayAdded();
    }

    private void OnPlayerDetached(EntityUid uid, SandevistanTimeDilatedComponent component, LocalPlayerDetachedEvent args)
    {
        if (_overlayActive)
        {
            _overlay.UpdateFade(10f, false);
            _overlayMan.RemoveOverlay(_overlay);
            _overlayActive = false;
        }
    }

    private void EnsureOverlayAdded()
    {
        if (_overlayActive)
            return;

        _overlayMan.AddOverlay(_overlay);
        _overlayActive = true;
    }
}
