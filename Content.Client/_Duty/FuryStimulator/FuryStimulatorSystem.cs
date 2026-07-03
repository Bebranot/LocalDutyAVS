using System.Numerics;
using Content.Shared._Duty.FuryStimulator;
using Content.Shared.Camera;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._Duty.FuryStimulator;

/// <summary>
/// _Duty: клиентская часть Fury-16 — экранный оверлей (<see cref="FuryOverlay"/>) и плавная
/// тряска экрана «восьмёркой» через <c>GetEyeOffsetEvent</c>. Общие предсказываемые эффекты
/// (скорость, оружие) — в <see cref="SharedFuryStimulatorSystem"/>.
/// </summary>
public sealed class FuryStimulatorSystem : SharedFuryStimulatorSystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <summary>Частота колебаний «восьмёрки», рад/с.</summary>
    private const float ShakeSpeed = 4f;

    /// <summary>Амплитуда смещения глаза на пике (в тайлах).</summary>
    private const float ShakeAmplitude = 0.12f;

    private FuryOverlay _fury = default!;

    public override void Initialize()
    {
        base.Initialize();

        _fury = new FuryOverlay();
        _overlay.AddOverlay(_fury);

        SubscribeLocalEvent<FuryStimulatorComponent, GetEyeOffsetEvent>(OnGetEyeOffset);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay(_fury);
    }

    private void OnGetEyeOffset(Entity<FuryStimulatorComponent> ent, ref GetEyeOffsetEvent args)
    {
        var intensity = VisualIntensity(ent.Comp.Stage);
        if (intensity <= 0f)
            return;

        // Фигура Лиссажу 1:2 → плавная «восьмёрка» влево-вправо.
        var t = (float) _timing.RealTime.TotalSeconds * ShakeSpeed;
        var amp = ShakeAmplitude * intensity;
        args.Offset += new Vector2(MathF.Sin(t), MathF.Sin(2f * t)) * amp;
    }
}
