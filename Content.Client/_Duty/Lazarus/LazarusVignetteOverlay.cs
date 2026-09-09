using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Lazarus;

/// <summary>
/// «Послевкусие» эффекта Лазаруса: тёмная виньетка по краям экрана, которая проявляется
/// по мере того, как отступает чернота <see cref="LazarusOverlay"/>, держится
/// <c>LazarusComponent.VignetteDuration</c> и угасает за <c>VignetteFadeOut</c>.
///
/// Тот же шейдер <c>GradientCircleMask</c>, что у <c>DutyPainFlashOverlay</c> и ванильной
/// боли — игрок читает сужение поля зрения как знакомый язык. Но без пульсации и в почти
/// чёрном цвете: пульсирующий красный означает боль, а здесь — угасание, а не рана.
/// </summary>
public sealed class LazarusVignetteOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> CircleMaskShader = "GradientCircleMask";

    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <summary>Выше <c>DutyPainFlashOverlay</c> и агонии, чтобы порядок был определён явно.</summary>
    private const int SceneZIndex = 100;

    /// <summary>Непрозрачность виньетки на пике. Держится долго, поэтому сдержанная.</summary>
    private const float Strength = 0.85f;

    /// <summary>Радиусы маски в долях ширины вьюпорта: мягкое затемнение от краёв к центру.</summary>
    private const float InnerRadiusFraction = 0.26f;
    private const float OuterRadiusFraction = 0.98f;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    /// <summary>
    /// Общий множитель непрозрачности сцены, 0..1 — общий с <see cref="LazarusOverlay"/>.
    /// Клиентская <c>LazarusSystem</c> гасит его при обрыве кинематики.
    /// </summary>
    public float Fade = 1f;

    private readonly ShaderInstance _shader;

    private readonly TimeSpan _start;
    private readonly float _appearStart;
    private readonly float _appearDuration;
    private readonly float _hold;
    private readonly float _fadeOut;

    public LazarusVignetteOverlay(
        IGameTiming timing,
        float appearStart,
        float appearDuration,
        float hold,
        float fadeOut)
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index(CircleMaskShader).InstanceUnique();

        ZIndex = SceneZIndex;

        _start = timing.RealTime;
        _appearStart = MathF.Max(appearStart, 0f);
        _appearDuration = MathF.Max(appearDuration, 0.01f);
        _hold = MathF.Max(hold, 0f);
        _fadeOut = MathF.Max(fadeOut, 0.01f);
    }

    /// <summary>Виньетка отыграна — оверлей можно убрать.</summary>
    public bool Finished => Elapsed >= _appearStart + _appearDuration + _hold + _fadeOut;

    private float Elapsed => (float)(_timing.RealTime - _start).TotalSeconds;

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (CurrentAlpha <= 0.001f)
            return false;

        // Только в глаз локального игрока, иначе виньетка полезет в чужие вьюпорты
        // (камеры наблюдения и т.п.) — как в DutyPainFlashOverlay.
        return _entity.TryGetComponent(_player.LocalEntity, out EyeComponent? eye)
               && args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var distance = args.ViewportBounds.Width;

        // time = 0: радиусы статичны. Пульсация тут читалась бы как боль.
        _shader.SetParameter("time", 0f);
        _shader.SetParameter("color", new Vector3(0.06f, 0.02f, 0.02f));
        _shader.SetParameter("darknessAlphaOuter", CurrentAlpha);
        _shader.SetParameter("innerCircleRadius", InnerRadiusFraction * distance);
        _shader.SetParameter("innerCircleMaxRadius", InnerRadiusFraction * distance);
        _shader.SetParameter("outerCircleRadius", OuterRadiusFraction * distance);
        _shader.SetParameter("outerCircleMaxRadius", OuterRadiusFraction * distance);

        handle.UseShader(_shader);
        handle.DrawRect(args.WorldAABB, Color.White);
        handle.UseShader(null);
    }

    private float CurrentAlpha => Strength * Fade * Envelope(Elapsed);

    /// <summary>Огибающая 0 → 1 → 0: проявление под уход черноты, удержание, угасание.</summary>
    private float Envelope(float t)
    {
        if (t < _appearStart)
            return 0f;

        var appearEnd = _appearStart + _appearDuration;
        if (t < appearEnd)
            return Smooth((t - _appearStart) / _appearDuration);

        var holdEnd = appearEnd + _hold;
        if (t < holdEnd)
            return 1f;

        var fadeEnd = holdEnd + _fadeOut;
        if (t < fadeEnd)
            return 1f - Smooth((t - holdEnd) / _fadeOut);

        return 0f;
    }

    private static float Smooth(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }
}
