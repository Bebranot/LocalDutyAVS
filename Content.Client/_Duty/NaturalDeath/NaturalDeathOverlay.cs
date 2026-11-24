using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._Duty.NaturalDeath;

/// <summary>
/// _Duty: полноэкранное плавное затемнение восприятия при естественной смерти (без белой вспышки).
/// Управляется клиентской <see cref="NaturalDeathSystem"/>.
/// </summary>
public sealed class NaturalDeathOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    /// <summary>
    /// Выше оверлеев урона/крита/контузии, но ниже LazarusOverlay (1000).
    /// </summary>
    private const int SceneZIndex = 900;

    private readonly IClyde _clyde;

    /// <summary>Текущая непрозрачность черноты (0..1).</summary>
    public float Alpha;

    public NaturalDeathOverlay(IClyde clyde)
    {
        _clyde = clyde;
        ZIndex = SceneZIndex;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return Alpha > 0.001f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var size = _clyde.ScreenSize;
        var bounds = new UIBox2(0, 0, size.X, size.Y);
        args.ScreenHandle.DrawRect(bounds, new Color(0f, 0f, 0f, Math.Clamp(Alpha, 0f, 1f)));
    }
}
