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
public sealed class DutyPainFlashSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;

    private DutyPainFlashOverlay _flash = default!;

    private float _duration;

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
        // Новая вспышка поверх недогасшей — берём максимум, чтобы серия неудач не выглядела
        // слабее одной.
        _flash.Level = MathF.Max(_flash.Level, Math.Clamp(ev.Intensity, 0f, 1f));
        _duration = MathF.Max(_duration, MathF.Max(ev.Duration, 0.1f));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_flash.Level <= 0f)
            return;

        _flash.Level -= frameTime / _duration;

        if (_flash.Level <= 0f)
            _flash.Level = 0f;
    }
}
