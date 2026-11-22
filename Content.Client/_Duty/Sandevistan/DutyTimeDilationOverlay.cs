// SPDX-FileCopyrightText: 2026 LocalDuty
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared._Duty.Sandevistan;
using Content.Shared.ADT.Sandevistan;
using Content.Shared.Ghost.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.Sandevistan;

/// <summary>
/// _Duty: лёгкий холодный/обесцвеченный оверлей для игроков, попавших в замедление времени Sandevistan.
/// Исключает наблюдателей (GhostComponent) и активных носителей Sandevistan.
/// </summary>
public sealed class DutyTimeDilationOverlay : Overlay
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public override bool RequestScreenTexture => true;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly ShaderInstance _shader;
    private float _strength;
    public float Strength => _strength;

    private const float FadeInSpeed = 5.0f;
    private const float FadeOutSpeed = 3.5f;

    public DutyTimeDilationOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = (int) Content.Shared.DrawDepth.DrawDepth.Effects + 19;
        _shader = _prototypeManager.Index<ShaderPrototype>("DutySandevistanDilation").Instance().Duplicate();
    }

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
        if (_playerManager.LocalEntity is not { Valid: true } player)
            return false;

        if (_entityManager.HasComponent<GhostComponent>(player)
            || _entityManager.HasComponent<ActiveSandevistanUserComponent>(player))
        {
            return false;
        }

        if (!_entityManager.HasComponent<SandevistanTimeDilatedComponent>(player) && _strength <= 0.001f)
            return false;

        return base.BeforeDraw(in args);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture is null || _strength <= 0.001f)
            return;

        var handle = args.WorldHandle;
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("strength", _strength);

        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
