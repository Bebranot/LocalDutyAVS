using Content.Shared.ADT.Abilities;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client.ADT.Overlays;

public sealed partial class SandevistanVisionSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayMan = default!;
    [Dependency] private ISharedPlayerManager _playerMan = default!;

    private SandevistanVisionOverlay _overlay = default!;
    // _Duty: track whether overlay is currently registered so fade-out finishes before removal.
    private bool _overlayActive;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SandevistanVisionComponent, ComponentInit>(OnSandevistanVisionInit);
        SubscribeLocalEvent<SandevistanVisionComponent, ComponentShutdown>(OnSandevistanVisionShutdown);
        SubscribeLocalEvent<SandevistanVisionComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<SandevistanVisionComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);

        _overlay = new();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!_overlayActive)
            return;

        var player = _playerMan.LocalEntity;
        var active = player is { Valid: true } && HasComp<SandevistanVisionComponent>(player.Value);
        _overlay.UpdateFade(frameTime, active);

        if (!active && _overlay.Strength <= 0.001f)
        {
            _overlayMan.RemoveOverlay(_overlay);
            _overlayActive = false;
        }
    }

    private void OnSandevistanVisionInit(EntityUid uid, SandevistanVisionComponent component, ComponentInit args)
    {
        if (uid == _playerMan.LocalEntity && !_overlayActive)
        {
            _overlayMan.AddOverlay(_overlay);
            _overlayActive = true;
        }
    }

    private void OnSandevistanVisionShutdown(EntityUid uid, SandevistanVisionComponent component, ComponentShutdown args)
    {
        // _Duty: let FrameUpdate fade out smoothly before removing overlay.
    }

    private void OnPlayerAttached(EntityUid uid, SandevistanVisionComponent component, LocalPlayerAttachedEvent args)
    {
        if (!_overlayActive)
        {
            _overlayMan.AddOverlay(_overlay);
            _overlayActive = true;
        }
    }

    private void OnPlayerDetached(EntityUid uid, SandevistanVisionComponent component, LocalPlayerDetachedEvent args)
    {
        if (_overlayActive)
        {
            _overlay.UpdateFade(10f, false);
            _overlayMan.RemoveOverlay(_overlay);
            _overlayActive = false;
        }
    }

    private void OnNoVisionFiltersChanged(bool enabled)
    {
        if (enabled)
            _overlayMan.RemoveOverlay(_overlay);
        else
            _overlayMan.AddOverlay(_overlay);
    }
}
