// SPDX-FileCopyrightText: 2026 LocalDuty
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.CritVision;

/// <summary>
/// _Duty: полноэкранный оверлей крит-зрения. Обесцвечивает и затемняет картинку в Critical
/// (с плавным лёгким эффектом в SoftCritical) и обрезает засветку поверх оверлеев ПНВ/термовизора
/// по мировому радиусу в тайлах.
/// </summary>
public sealed partial class CritVisionOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> CritVisionShader = "DutyCritVision";

    /// <summary>
    /// Рисуем выше ADT NightVision/ThermalVision (10000..10004), чтобы в крите зрение оставалось
    /// чёрно-белым и темнее даже с активным ПНВ/термовизором, но в WorldSpace — ScreenSpace-сцены
    /// (LazarusOverlay, NaturalDeathOverlay) всегда остаются поверх.
    /// </summary>
    public const int SceneZIndex = 10010;

    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private readonly SharedTransformSystem _transform;
    private readonly ShaderInstance _shader;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    public float Desaturate;
    public float Darkness;
    public float Contrast = 1f;
    public float MaskStrength;
    public float VisionRadiusTiles = 12f;

    public CritVisionOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = SceneZIndex;
        _transform = _entity.System<SharedTransformSystem>();
        _shader = _proto.Index(CritVisionShader).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (Desaturate <= 0.001f && Darkness <= 0.001f && MaskStrength <= 0.001f)
            return false;

        if (_player.LocalEntity is not { Valid: true } local)
            return false;

        if (!_entity.TryGetComponent(local, out EyeComponent? eyeComp) || eyeComp.Eye == null)
            return false;

        return args.Viewport.Eye == eyeComp.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null || _player.LocalEntity is not { Valid: true } local)
            return;

        if (!_entity.TryGetComponent(local, out TransformComponent? xform))
            return;

        var invMatrix = args.Viewport.GetWorldToLocalMatrix();
        var playerWorldPos = _transform.GetWorldPosition(xform);
        var p0 = Vector2.Transform(playerWorldPos, invMatrix);
        var p1 = Vector2.Transform(playerWorldPos + Vector2.UnitX, invMatrix);
        var pixelsPerTile = MathF.Max(1f, (p1 - p0).Length());

        var vertical = args.Viewport.Size.Y;
        var eyeCenterPx = new Vector2(p0.X, vertical - p0.Y);
        var outerRadiusPx = MathF.Max(1f, VisionRadiusTiles * pixelsPerTile);
        var innerRadiusPx = outerRadiusPx * 0.55f;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("desaturate", Desaturate);
        _shader.SetParameter("darkness", Darkness);
        _shader.SetParameter("contrast", Contrast);
        _shader.SetParameter("maskStrength", MaskStrength);
        _shader.SetParameter("eyeCenterPx", eyeCenterPx);
        _shader.SetParameter("innerRadiusPx", innerRadiusPx);
        _shader.SetParameter("outerRadiusPx", outerRadiusPx);

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
