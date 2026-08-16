using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.FireAgony;

/// <summary>
/// _Duty: экранный оверлей «Агонии от огня» — красная пульсирующая виньетка боли.
/// Силу (<see cref="Strength"/>, 0..1, с фейдом ~0.5с) выставляет клиентская
/// <see cref="FireAgonySystem"/> по сетевому флагу <c>FireAgonyComponent.Active</c>.
/// </summary>
public sealed class FireAgonyOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> FireShader = "FireAgonyVision";

    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;

    public override bool RequestScreenTexture => true;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    /// <summary>Текущая сила эффекта с фейдом (0 = выкл, 1 = пик).</summary>
    public float Strength;

    private readonly ShaderInstance _shader;

    public FireAgonyOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index(FireShader).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (Strength <= 0.001f)
            return false;

        if (!_entMan.TryGetComponent(_player.LocalEntity, out EyeComponent? eye))
            return false;

        return args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("strength", Strength);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
