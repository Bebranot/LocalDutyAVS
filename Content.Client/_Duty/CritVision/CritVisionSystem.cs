// SPDX-FileCopyrightText: 2026 LocalDuty
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.GameTicking;
using Content.Shared.Ghost.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._Duty.CritVision;

/// <summary>
/// _Duty: управляет оверлеями крит-зрения (<see cref="CritVisionOverlay"/> и
/// <see cref="CritVisionSilhouetteOverlay"/>), плавными переходами между Alive / SoftCritical / Critical / Dead
/// и гарантированной очисткой при смерти, выходе из крита или отсоединении игрока.
/// </summary>
public sealed partial class CritVisionSystem : EntitySystem
{
    public const float CriticalVisionRangeTiles = 3.0f;
    public const float SoftCriticalVisionRangeTiles = 5.5f;
    private const float InactiveVisionRangeTiles = 12.0f;

    private const float FadeSpeed = 4.5f;
    private const float SilhouetteFadeOutSpeed = 7.0f;

    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private CritVisionOverlay _screenOverlay = default!;
    private CritVisionSilhouetteOverlay _silhouetteOverlay = default!;

    private EntityUid? _trackedEntity;
    private float _desaturate;
    private float _darkness;
    private float _contrast = 1f;
    private float _maskStrength;
    private float _visionRadiusTiles = InactiveVisionRangeTiles;
    private float _silhouetteOpacity;

    public override void Initialize()
    {
        base.Initialize();

        _screenOverlay = new CritVisionOverlay();
        _silhouetteOverlay = new CritVisionSilhouetteOverlay();

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        ClearOverlaysImmediate();
    }

    private void OnPlayerAttached(LocalPlayerAttachedEvent args)
    {
        if (args.Entity != _trackedEntity)
            ClearOverlaysImmediate();

        _trackedEntity = args.Entity;
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        ClearOverlaysImmediate();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        ClearOverlaysImmediate();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var local = _player.LocalEntity;
        if (local is not { Valid: true } uid ||
            TerminatingOrDeleted(uid) ||
            HasComp<GhostComponent>(uid) ||
            !TryComp<MobStateComponent>(uid, out var mobState))
        {
            if (_trackedEntity != null || IsAnyOverlayActive())
                ClearOverlaysImmediate();
            return;
        }

        if (_trackedEntity != uid)
        {
            ClearOverlaysImmediate();
            _trackedEntity = uid;
        }

        var showOverlays = !TryComp<MobThresholdsComponent>(uid, out var thresholds) || thresholds.ShowOverlays;
        var state = showOverlays ? mobState.CurrentState : MobState.Alive;

        GetTargets(
            state,
            out var targetDesaturate,
            out var targetDarkness,
            out var targetContrast,
            out var targetMask,
            out var targetRadius,
            out var targetSilhouette);

        _desaturate = MoveTowards(_desaturate, targetDesaturate, FadeSpeed * frameTime);
        _darkness = MoveTowards(_darkness, targetDarkness, FadeSpeed * frameTime);
        _contrast = MoveTowards(_contrast, targetContrast, FadeSpeed * frameTime);
        _maskStrength = MoveTowards(_maskStrength, targetMask, FadeSpeed * frameTime);
        _visionRadiusTiles = MoveTowards(_visionRadiusTiles, targetRadius, FadeSpeed * 3.0f * frameTime);

        var silSpeed = targetSilhouette > _silhouetteOpacity ? FadeSpeed : SilhouetteFadeOutSpeed;
        _silhouetteOpacity = MoveTowards(_silhouetteOpacity, targetSilhouette, silSpeed * frameTime);

        var screenActive = _desaturate > 0.001f || _darkness > 0.001f || _maskStrength > 0.001f;
        if (screenActive)
        {
            _screenOverlay.Desaturate = _desaturate;
            _screenOverlay.Darkness = _darkness;
            _screenOverlay.Contrast = _contrast;
            _screenOverlay.MaskStrength = _maskStrength;
            _screenOverlay.VisionRadiusTiles = _visionRadiusTiles;

            if (!_overlayManager.HasOverlay<CritVisionOverlay>())
                _overlayManager.AddOverlay(_screenOverlay);
        }
        else if (_overlayManager.HasOverlay<CritVisionOverlay>())
        {
            _overlayManager.RemoveOverlay(_screenOverlay);
        }

        var silhouetteActive = _silhouetteOpacity > 0.001f;
        if (silhouetteActive)
        {
            _silhouetteOverlay.Opacity = _silhouetteOpacity;
            _silhouetteOverlay.VisionRadiusTiles = CriticalVisionRangeTiles;

            if (!_overlayManager.HasOverlay<CritVisionSilhouetteOverlay>())
                _overlayManager.AddOverlay(_silhouetteOverlay);
        }
        else if (_overlayManager.HasOverlay<CritVisionSilhouetteOverlay>())
        {
            _overlayManager.RemoveOverlay(_silhouetteOverlay);
        }
    }

    private static void GetTargets(
        MobState state,
        out float desaturate,
        out float darkness,
        out float contrast,
        out float maskStrength,
        out float visionRadiusTiles,
        out float silhouetteOpacity)
    {
        switch (state)
        {
            case MobState.Critical:
                desaturate = 1.0f;
                darkness = 0.56f;
                contrast = 1.18f;
                maskStrength = 1.0f;
                visionRadiusTiles = CriticalVisionRangeTiles;
                silhouetteOpacity = 0.92f;
                break;

            case MobState.SoftCritical:
                desaturate = 0.28f;
                darkness = 0.14f;
                contrast = 1.04f;
                maskStrength = 1.0f;
                visionRadiusTiles = SoftCriticalVisionRangeTiles;
                silhouetteOpacity = 0f;
                break;

            case MobState.Dead:
                // При естественной смерти NaturalDeathOverlay уводит экран в темноту; силуэты сразу гаснут.
                desaturate = 0f;
                darkness = 0f;
                contrast = 1f;
                maskStrength = 0f;
                visionRadiusTiles = CriticalVisionRangeTiles;
                silhouetteOpacity = 0f;
                break;

            default:
                desaturate = 0f;
                darkness = 0f;
                contrast = 1f;
                maskStrength = 0f;
                visionRadiusTiles = InactiveVisionRangeTiles;
                silhouetteOpacity = 0f;
                break;
        }
    }

    private bool IsAnyOverlayActive()
        => _overlayManager.HasOverlay<CritVisionOverlay>() || _overlayManager.HasOverlay<CritVisionSilhouetteOverlay>();

    private void ClearOverlaysImmediate()
    {
        _trackedEntity = null;
        _desaturate = 0f;
        _darkness = 0f;
        _contrast = 1f;
        _maskStrength = 0f;
        _visionRadiusTiles = InactiveVisionRangeTiles;
        _silhouetteOpacity = 0f;

        if (_overlayManager.HasOverlay<CritVisionOverlay>())
            _overlayManager.RemoveOverlay(_screenOverlay);

        if (_overlayManager.HasOverlay<CritVisionSilhouetteOverlay>())
            _overlayManager.RemoveOverlay(_silhouetteOverlay);
    }

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta)
            return target;

        return current + MathF.Sign(target - current) * maxDelta;
    }
}
