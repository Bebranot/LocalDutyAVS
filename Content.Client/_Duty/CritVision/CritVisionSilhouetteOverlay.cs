// SPDX-FileCopyrightText: 2026 LocalDuty
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Client.Stealth;
using Content.Shared.Examine;
using Content.Shared.Ghost.Components;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Stealth.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.CritVision;

/// <summary>
/// _Duty: рисует видимых гуманоидов в пределах радиуса обзора крита (3 тайла) как целиком
/// залитые чёрно-белым «телевизионным» шумом силуэты. Не просвечивает сквозь стены и
/// контейнеры, учитывает лежачую позу спрайта, масштаб/зум и не раскрывает деталей внешности.
/// </summary>
public sealed partial class CritVisionSilhouetteOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> SilhouetteShader = "DutyCritSilhouette";
    private const string PostShaderId = "duty-crit-silhouette";
    private const float SpriteRadiusMargin = 0.75f;

    public const int SceneZIndex = CritVisionOverlay.SceneZIndex + 1;

    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private readonly ContainerSystem _container;
    private readonly ExamineSystemShared _examine;
    private readonly SpriteSystem _sprite;
    private readonly StealthSystem _stealth;
    private readonly SharedTransformSystem _transform;

    private readonly EntityQuery<GhostComponent> _ghostQuery;
    private readonly EntityQuery<MetaDataComponent> _metaQuery;
    private readonly EntityQuery<StealthComponent> _stealthQuery;
    private readonly EntityQuery<TransformComponent> _xformQuery;

    private readonly ShaderInstance _shader;
    private readonly SpriteComponent.PostShaderEntry[] _post;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public float Opacity;
    public float VisionRadiusTiles = 3f;

    public CritVisionSilhouetteOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = SceneZIndex;

        _container = _entity.System<ContainerSystem>();
        _examine = _entity.System<ExamineSystemShared>();
        _sprite = _entity.System<SpriteSystem>();
        _stealth = _entity.System<StealthSystem>();
        _transform = _entity.System<SharedTransformSystem>();

        _ghostQuery = _entity.GetEntityQuery<GhostComponent>();
        _metaQuery = _entity.GetEntityQuery<MetaDataComponent>();
        _stealthQuery = _entity.GetEntityQuery<StealthComponent>();
        _xformQuery = _entity.GetEntityQuery<TransformComponent>();

        _shader = _proto.Index(SilhouetteShader).InstanceUnique();
        _post = new[] { new SpriteComponent.PostShaderEntry(PostShaderId, _shader) };
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (Opacity <= 0.001f)
            return false;

        if (_player.LocalEntity is not { Valid: true } local)
            return false;

        if (!_entity.TryGetComponent(local, out EyeComponent? eyeComp) || eyeComp.Eye == null)
            return false;

        return args.Viewport.Eye == eyeComp.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not { } eye || _player.LocalEntity is not { Valid: true } local)
            return;

        if (!_xformQuery.TryGetComponent(local, out var localXform) || localXform.MapID != args.MapId)
            return;

        var originMapCoords = _transform.GetMapCoordinates(local, localXform);
        var originWorldPos = originMapCoords.Position;
        var invMatrix = args.Viewport.GetWorldToLocalMatrix();
        var p0 = Vector2.Transform(originWorldPos, invMatrix);
        var p1 = Vector2.Transform(originWorldPos + Vector2.UnitX, invMatrix);
        var pixelsPerTile = MathF.Max(1f, (p1 - p0).Length());

        var vertical = args.Viewport.Size.Y;
        var eyeCenterPx = new Vector2(p0.X, vertical - p0.Y);
        var outerRadiusPx = MathF.Max(1f, VisionRadiusTiles * pixelsPerTile);
        var innerRadiusPx = outerRadiusPx * 0.65f;
        var renderScale = MathF.Max(0.5f, (args.Viewport.RenderScale.X + args.Viewport.RenderScale.Y) * 0.5f);

        _shader.SetParameter("opacity", Opacity);
        _shader.SetParameter("grainSize", 2.0f * renderScale);
        _shader.SetParameter("eyeCenterPx", eyeCenterPx);
        _shader.SetParameter("innerRadiusPx", innerRadiusPx);
        _shader.SetParameter("outerRadiusPx", outerRadiusPx);

        var maxCenterDist = VisionRadiusTiles + SpriteRadiusMargin;
        var maxCenterDistSq = maxCenterDist * maxCenterDist;

        var handle = args.WorldHandle;
        var eyeRotation = eye.Rotation;

        var query = _entity.EntityQueryEnumerator<HumanoidProfileComponent, MobStateComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var sprite, out var xform))
        {
            if (uid == local || _ghostQuery.HasComp(uid))
                continue;

            if (!sprite.Visible || sprite.ContainerOccluded || xform.MapID != args.MapId)
                continue;

            var meta = _metaQuery.GetComponent(uid);
            if (_container.IsEntityInContainer(uid, meta))
                continue;

            if (_stealthQuery.TryGetComponent(uid, out var stealth) && _stealth.GetVisibility(uid, stealth) <= 0.5f)
                continue;

            var (targetWorldPos, targetWorldRot) = _transform.GetWorldPositionRotation(xform);
            if ((targetWorldPos - originWorldPos).LengthSquared() > maxCenterDistSq)
                continue;

            var targetMapCoords = new MapCoordinates(targetWorldPos, xform.MapID);
            if (!_examine.InRangeUnOccluded(
                    originMapCoords,
                    targetMapCoords,
                    maxCenterDist,
                    (local, uid),
                    static (hit, state) => hit == state.local || hit == state.uid))
                continue;

            _sprite.RenderSprite((uid, sprite), handle, eyeRotation, targetWorldRot, targetWorldPos, _post);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }
}
