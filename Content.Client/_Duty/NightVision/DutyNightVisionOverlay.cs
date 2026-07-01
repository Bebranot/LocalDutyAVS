using System.Numerics;
using Content.Shared.ADT.NightVision;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.NightVision;

/// <summary>
/// _Duty: зелёный тинт ночного зрения поверх (полностью-светлой) сцены, когда у локального игрока
/// активна ADT-система ПНВ. Состояние определяем по наличию <see cref="NightVisionComponent"/>
/// (предметный ПНВ добавляет/снимает компонент на вкл/выкл), чтобы не лезть в его [Access].
/// </summary>
public sealed class DutyNightVisionOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private static readonly ProtoId<ShaderPrototype> ShaderId = "DutyNightVision";
    private readonly ShaderInstance _shader;

    // ── Подкрутка вида ──────────────────────────────────────────────────────
    private static readonly Vector3 TintColor = new(0.10f, 1.0f, 0.20f); // зелёный ПНВ
    private const float LuminanceThreshold = 0.5f;
    private const float NoiseAmount = 0.4f;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    public DutyNightVisionOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index(ShaderId).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var player = _player.LocalEntity;
        if (player == null || !_entMan.HasComponent<NightVisionComponent>(player))
            return false;

        if (!_entMan.TryGetComponent(player, out EyeComponent? eye) || args.Viewport.Eye != eye.Eye)
            return false;

        return ScreenTexture != null;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("tint", TintColor);
        _shader.SetParameter("luminance_threshold", LuminanceThreshold);
        _shader.SetParameter("noise_amount", NoiseAmount);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
