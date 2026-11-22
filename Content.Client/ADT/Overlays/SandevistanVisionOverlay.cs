using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Content.Shared.ADT.Abilities;
using Robust.Client.GameObjects;
using System.Numerics;

namespace Content.Client.ADT.Overlays;

public sealed partial class SandevistanVisionOverlay : Overlay
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] IEntityManager _entityManager = default!;

    public override bool RequestScreenTexture => true;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    private readonly ShaderInstance _sandevistanVisionShader;
    private readonly TransformSystem _transformSystem = default!;

    // _Duty: smooth fade-in / fade-out strength for Edgerunners vision shader.
    private float _strength;
    public float Strength => _strength;

    private const float FadeInSpeed = 5.0f;
    private const float FadeOutSpeed = 3.0f;

    public SandevistanVisionOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = (int)Content.Shared.DrawDepth.DrawDepth.Effects + 20;
        _sandevistanVisionShader = _prototypeManager.Index<ShaderPrototype>("SandevistanVision").Instance().Duplicate();
        _transformSystem = _entityManager.System<TransformSystem>();
    }

    // _Duty: update fade strength from SandevistanVisionSystem.
    public void UpdateFade(float frameTime, bool active)
    {
        var target = active ? 1f : 0f;
        var speed = active ? FadeInSpeed : FadeOutSpeed;
        if (_strength < target)
            _strength = MathF.Min(target, _strength + speed * frameTime);
        else if (_strength > target)
            _strength = MathF.Max(target, _strength - speed * frameTime);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        // _Duty: allow overlay to draw while fading out even after component removal.
        if (_playerManager.LocalEntity is not { Valid: true } player)
            return false;

        if (!_entityManager.HasComponent<SandevistanVisionComponent>(player) && _strength <= 0.001f)
            return false;

        return base.BeforeDraw(in args);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture is null || _strength <= 0.001f)
            return;

        var worldHandle = args.WorldHandle;
        var viewport = args.WorldBounds;
        var eye = args.Viewport.Eye;

        if (eye == null)
            return;

        var player = _playerManager.LocalEntity;
        if (player == null)
            return;

        _sandevistanVisionShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        // _Duty: pass fade strength to Edgerunners shader.
        _sandevistanVisionShader.SetParameter("strength", _strength);
        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(_sandevistanVisionShader);
        worldHandle.DrawRect(viewport, Color.White);
        worldHandle.UseShader(null);

        if (_entityManager.TryGetComponent<SpriteComponent>(player.Value, out var playerSprite))
        {
            var playerXform = _entityManager.GetComponent<TransformComponent>(player.Value);
            var playerPos = _transformSystem.GetWorldPosition(playerXform);
            var playerRot = _transformSystem.GetWorldRotation(playerXform);

            worldHandle.UseShader(null);
            playerSprite.Render(worldHandle, eye.Rotation, playerRot, null, playerPos);
        }
    }
}
