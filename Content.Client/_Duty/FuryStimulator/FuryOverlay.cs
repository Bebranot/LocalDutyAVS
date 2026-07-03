using Content.Shared._Duty.FuryStimulator;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.FuryStimulator;

/// <summary>
/// _Duty: экранный оверлей стимулятора Fury-16 — синяя виньетка по бокам и лёгкое искажение,
/// сила которых зависит от текущей стадии локального игрока (тряска экрана делается отдельно
/// через <c>GetEyeOffsetEvent</c> в <see cref="FuryStimulatorSystem"/>).
/// </summary>
public sealed class FuryOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> FuryShader = "FuryVision";

    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;

    public override bool RequestScreenTexture => true;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly ShaderInstance _shader;
    private float _strength;

    public FuryOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index(FuryShader).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_entMan.TryGetComponent(_player.LocalEntity, out EyeComponent? eye))
            return false;

        if (args.Viewport.Eye != eye.Eye)
            return false;

        if (!_entMan.TryGetComponent(_player.LocalEntity, out FuryStimulatorComponent? fury))
            return false;

        _strength = SharedFuryStimulatorSystem.VisualIntensity(fury.Stage);
        return _strength > 0f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("strength", _strength);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
