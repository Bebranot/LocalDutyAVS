// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma.Events;
using Robust.Client.Graphics;

namespace Content.Client._Duty.Trauma;

/// <summary>
/// _Duty: держит виньетку боли (<see cref="DutyPainFlashOverlay"/>) — вспыхивает по сетевому
/// <see cref="DutyPainFlashEvent"/> и плавно гаснет. Оверлей висит всегда, но сам себя не рисует,
/// пока сила равна нулю, поэтому добавлять и снимать его на каждую вспышку не нужно.
/// </summary>
public sealed partial class DutyPainFlashSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;

    private DutyPainFlashOverlay _flash = default!;

    /// <summary>Пик текущей вспышки и сколько она уже гаснет — Level считается из них каждый кадр.</summary>
    private float _peak;
    private float _duration;
    private float _elapsed;

    public override void Initialize()
    {
        base.Initialize();

        _flash = new DutyPainFlashOverlay();
        _overlay.AddOverlay(_flash);

        SubscribeNetworkEvent<DutyPainFlashEvent>(OnPainFlash);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay(_flash);
    }

    private void OnPainFlash(DutyPainFlashEvent ev)
    {
        var intensity = Math.Clamp(ev.Intensity, 0f, 1f);
        var duration = MathF.Max(ev.Duration, 0.1f);

        // Новая вспышка поверх недогасшей — не слабее текущего уровня, чтобы серия неудач не
        // выглядела слабее одной, и с отсчётом затухания заново. Длительность — новой вспышки:
        // раньше она бралась максимумом и не сбрасывалась, и после одной долгой вспышки все
        // короткие до конца раунда гасли так же медленно.
        _peak = MathF.Max(_flash.Level, intensity);
        _duration = duration;
        _elapsed = 0f;
        _flash.Level = _peak;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_flash.Level <= 0f)
            return;

        _elapsed += frameTime;
        var t = Math.Clamp(_elapsed / _duration, 0f, 1f);

        // Ease-out: боль бьёт сразу и быстро спадает, а слабый хвост тает мягко, без «обрыва» в
        // конце, как у линейного затухания.
        var remaining = 1f - t;
        _flash.Level = t >= 1f ? 0f : _peak * remaining * remaining;
    }
}
