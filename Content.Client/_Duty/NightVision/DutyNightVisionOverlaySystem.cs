using Robust.Client.Graphics;

namespace Content.Client._Duty.NightVision;

/// <summary>
/// _Duty: держит зелёный оверлей ПНВ (<see cref="DutyNightVisionOverlay"/>) добавленным.
/// Сам оверлей в BeforeDraw решает, рисоваться ли (по активности ПНВ у локального игрока).
/// </summary>
public sealed class DutyNightVisionOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;

    private DutyNightVisionOverlay _overlayInstance = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlayInstance = new DutyNightVisionOverlay();
        _overlay.AddOverlay(_overlayInstance);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay(_overlayInstance);
    }
}
