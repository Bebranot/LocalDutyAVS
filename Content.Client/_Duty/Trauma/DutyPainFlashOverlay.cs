// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Trauma;

/// <summary>
/// _Duty: красная пульсирующая виньетка «больно». Намеренно повторяет ванильную виньетку боли из
/// <c>DamageOverlay</c> — тот же шейдер GradientCircleMask, тот же красный цвет и та же
/// пульсация, — чтобы игрок читал её как знакомую боль, а не как новый непонятный эффект.
///
/// Отдельным оверлеем, а не через <c>DamageOverlay.PainLevel</c>: тот уровень пересчитывается
/// контроллером по урону на каждом изменении порогов и затёр бы разовую вспышку.
/// </summary>
public sealed class DutyPainFlashOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> CircleMaskShader = "GradientCircleMask";

    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly ShaderInstance _shader;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    /// <summary>Текущая сила вспышки, 0..1. Гасится системой-владельцем.</summary>
    public float Level;

    public DutyPainFlashOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index(CircleMaskShader).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (Level <= 0f)
            return false;

        // Рисуем только в глаз локального игрока, иначе виньетка полезет и в чужие вьюпорты
        // (камеры наблюдения и т.п.).
        return _entity.TryGetComponent(_player.LocalEntity, out EyeComponent? eye)
               && args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var distance = args.ViewportBounds.Width;
        var time = (float) _timing.RealTime.TotalSeconds;

        // Параметры один в один как у ванильной боли в DamageOverlay.
        const float pulseRate = 3f;
        var outerMax = 2.0f * distance;
        var outerMin = 0.8f * distance;
        var innerMax = 0.6f * distance;
        var innerMin = 0.2f * distance;

        var outerRadius = outerMax - Level * (outerMax - outerMin);
        var innerRadius = innerMax - Level * (innerMax - innerMin);
        var pulse = MathF.Max(0f, MathF.Sin(time * pulseRate));

        _shader.SetParameter("time", pulse);
        _shader.SetParameter("color", new Vector3(1f, 0f, 0f));
        _shader.SetParameter("darknessAlphaOuter", 0.8f);
        _shader.SetParameter("outerCircleRadius", outerRadius);
        _shader.SetParameter("outerCircleMaxRadius", outerRadius + 0.2f * distance);
        _shader.SetParameter("innerCircleRadius", innerRadius);
        _shader.SetParameter("innerCircleMaxRadius", innerRadius + 0.02f * distance);

        handle.UseShader(_shader);
        handle.DrawRect(args.WorldAABB, Color.White);
        handle.UseShader(null);
    }
}
