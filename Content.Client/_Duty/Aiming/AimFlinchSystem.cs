// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared._Duty.Aiming.Events;
using Content.Shared.Camera;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Aiming;

/// <summary>
/// Пуля, пойманная во время прицеливания, дёргает камеру: резкий короткий рывок в сторону,
/// противоположную курсору (голову/ствол отбрасывает от цели), и быстрое, но плавное возвращение.
/// </summary>
/// <remarks>
/// Не через штатную отдачу (<see cref="SharedCameraRecoilSystem"/>): та возвращает камеру с линейной
/// скоростью, которая первые секунды почти нулевая, — рывок «залипал» бы. Здесь форма рывка задана явно.
/// </remarks>
public sealed partial class AimFlinchSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IInputManager _inputManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    /// <summary>Рывок до крайней точки — почти мгновенный, как удар.</summary>
    private const float AttackSeconds = 0.05f;

    /// <summary>Возврат — быстрый, но без щелчка на месте.</summary>
    private const float ReturnSeconds = 0.3f;

    /// <summary>Сила рывка в тайлах: база плюс добавка за урон, с потолком — «маленький сдвиг», не прыжок камеры.</summary>
    private const float BaseKick = 0.18f;
    private const float KickPerDamage = 0.006f;
    private const float MaxKick = 0.4f;

    /// <summary>Доля случайного бокового увода — чтобы попадания не выглядели одинаково.</summary>
    private const float SideJitter = 0.35f;

    /// <summary>При «уменьшении движения» рывок остаётся, но слабее.</summary>
    private const float ReducedMotionScale = 0.4f;

    private float _intensity;
    private bool _reducedMotion;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<AimFlinchEvent>(OnAimFlinch);
        SubscribeLocalEvent<AimFlinchComponent, GetEyeOffsetEvent>(OnGetEyeOffset);

        Subs.CVar(_cfg, CCVars.ScreenShakeIntensity, value => _intensity = value, true);
        Subs.CVar(_cfg, CCVars.ReducedMotion, value => _reducedMotion = value, true);
    }

    private void OnAimFlinch(AimFlinchEvent ev)
    {
        if (_intensity <= 0f || _player.LocalEntity is not { } user)
            return;

        var mouse = _eyeManager.PixelToMap(_inputManager.MouseScreenPosition);
        var userPos = _xform.GetMapCoordinates(user);

        // Без курсора в мире (свернул окно, другая карта) направление брать неоткуда — случайное.
        var toCursor = mouse.MapId == userPos.MapId && mouse.MapId != MapId.Nullspace
            ? mouse.Position - userPos.Position
            : Vector2.Zero;

        var away = toCursor.LengthSquared() > 0.0001f
            ? -Vector2.Normalize(toCursor)
            : _random.NextAngle().ToVec();

        var side = new Vector2(-away.Y, away.X) * _random.NextFloat(-SideJitter, SideJitter);

        var strength = Math.Min(BaseKick + ev.Damage * KickPerDamage, MaxKick) * _intensity;
        if (_reducedMotion)
            strength *= ReducedMotionScale;

        var flinch = EnsureComp<AimFlinchComponent>(user);
        flinch.From = flinch.Current;
        flinch.Kick = Vector2.Normalize(away + side) * strength;
        flinch.Start = _timing.RealTime;
    }

    private void OnGetEyeOffset(Entity<AimFlinchComponent> ent, ref GetEyeOffsetEvent args)
    {
        args.Offset += ent.Comp.Current;
    }

    public override void FrameUpdate(float frameTime)
    {
        var now = _timing.RealTime;
        var query = EntityQueryEnumerator<AimFlinchComponent>();

        while (query.MoveNext(out var uid, out var flinch))
        {
            var t = (float)(now - flinch.Start).TotalSeconds;

            if (t < AttackSeconds)
            {
                // Ease-out: основная часть пути — в первые миллисекунды, ощущается как толчок.
                var k = t / AttackSeconds;
                flinch.Current = Vector2.Lerp(flinch.From, flinch.Kick, 1f - (1f - k) * (1f - k));
                continue;
            }

            var r = (t - AttackSeconds) / ReturnSeconds;
            if (r >= 1f)
            {
                RemCompDeferred<AimFlinchComponent>(uid);
                flinch.Current = Vector2.Zero;
                continue;
            }

            // Smoothstep: трогается с места мягко и мягко же садится — без рывка на обратном пути.
            flinch.Current = flinch.Kick * (1f - r * r * (3f - 2f * r));
        }
    }
}
