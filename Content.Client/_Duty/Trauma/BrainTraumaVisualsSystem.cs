// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared._Duty.Trauma.Components;
using Content.Shared.CCVar;
using Content.Shared.Camera;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Trauma;

/// <summary>
/// _Duty: клиентская «качка» камеры при сотрясении мозга — мир плывёт тем сильнее, чем тяжелее
/// сотрясение. Намеренно ОТЛИЧАЕТСЯ от затемнения аудио-контузии (<c>_Duty/Concussion</c>):
/// там экран темнеет, здесь — уплывает, поэтому две механики не путаются визуально.
///
/// Реализовано смещением глаза (<see cref="GetEyeOffsetEvent"/>), а не шейдером — не требует
/// новых ассетов и складывается с прочими смещениями камеры (отдача и т.п.).
/// </summary>
public sealed class BrainTraumaVisualsSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private bool _reducedMotion;

    // ── Тюнинг (Phase 6). ──────────────────────────────────────────────────────

    /// <summary>Амплитуда качки (в тайлах) по тяжести.</summary>
    private const float LightAmplitude = 0.03f;
    private const float MediumAmplitude = 0.07f;
    private const float SevereAmplitude = 0.13f;

    /// <summary>Во сколько раз слабее качать при включённом «уменьшении движения».</summary>
    private const float ReducedMotionScale = 0.3f;

    /// <summary>Разные частоты по осям — траектория выходит неровной «восьмёркой», а не кругом.</summary>
    private const float FrequencyX = 0.9f;
    private const float FrequencyY = 0.63f;

    public override void Initialize()
    {
        SubscribeLocalEvent<HeadTraumaComponent, GetEyeOffsetEvent>(OnGetEyeOffset);

        Subs.CVar(_cfg, CCVars.ReducedMotion, value => _reducedMotion = value, true);
    }

    private void OnGetEyeOffset(Entity<HeadTraumaComponent> ent, ref GetEyeOffsetEvent args)
    {
        var amplitude = ent.Comp.Tier switch
        {
            HeadTraumaTier.Light => LightAmplitude,
            HeadTraumaTier.Medium => MediumAmplitude,
            HeadTraumaTier.Severe => SevereAmplitude,
            _ => 0f,
        };

        if (amplitude <= 0f)
            return;

        // Доступность: при «уменьшении движения» качка остаётся, но заметно слабее.
        if (_reducedMotion)
            amplitude *= ReducedMotionScale;

        var time = (float)_timing.CurTime.TotalSeconds;

        args.Offset += new Vector2(
            MathF.Sin(time * FrequencyX * MathF.Tau) * amplitude,
            MathF.Sin(time * FrequencyY * MathF.Tau) * amplitude);
    }
}
