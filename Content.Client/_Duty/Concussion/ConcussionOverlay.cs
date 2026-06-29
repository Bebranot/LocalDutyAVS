using Content.Shared.CCVar;
using Content.Shared._Duty.Concussion;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Concussion;

/// <summary>
/// _Duty: затемнение экрана от контузии. Совмещает три слоя:
/// постоянное «приглушение» по уровню шкалы, короткое моргание от выстрела
/// и резкий blackout с долгим fade-out от взрыва. Итоговая альфа — максимум из них.
/// </summary>
public sealed class ConcussionOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private readonly SharedConcussionSystem _concussion;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    /// <summary>Сервер/мобстейт могут полностью гасить эффект (смерть, выключено в CVar).</summary>
    public bool Suppressed;

    private bool _reducedMotion;

    // ── Импульсы ────────────────────────────────────────────────────────────
    private const float ShotDuration = 0.2f;
    private const float ShotRise = 0.05f;

    private const float BlastRise = 0.12f;
    private const float BlastHold = 2.0f;
    private const float BlastFade = 2.5f;

    private TimeSpan _shotStart;
    private float _shotPeak;

    private TimeSpan _blastStart;
    private float _blastPeak;

    public ConcussionOverlay()
    {
        IoCManager.InjectDependencies(this);
        _concussion = _entMan.System<SharedConcussionSystem>();
        _cfg.OnValueChanged(CCVars.ReducedMotion, b => _reducedMotion = b, invokeImmediately: true);
    }

    public void SetShot(float intensity)
    {
        var peak = 0.10f + 0.50f * Math.Clamp(intensity, 0f, 1f);
        if (_reducedMotion)
            peak *= 0.4f;

        _shotStart = _timing.CurTime;
        _shotPeak = peak;
    }

    public void SetBlast(float intensity)
    {
        // Базовый blackout сильный независимо от шкалы; шкала чуть добавляет.
        _blastPeak = Math.Clamp(0.80f + 0.15f * Math.Clamp(intensity, 0f, 1f), 0f, 0.95f);
        _blastStart = _timing.CurTime;
    }

    private float ShotAlpha()
    {
        if (_shotPeak <= 0f)
            return 0f;

        var t = (float)(_timing.CurTime - _shotStart).TotalSeconds;
        if (t < 0f || t > ShotDuration)
            return 0f;

        return t < ShotRise
            ? _shotPeak * (t / ShotRise)
            : _shotPeak * (1f - (t - ShotRise) / (ShotDuration - ShotRise));
    }

    private float BlastAlpha()
    {
        if (_blastPeak <= 0f)
            return 0f;

        var t = (float)(_timing.CurTime - _blastStart).TotalSeconds;
        if (t < 0f || t > BlastHold + BlastFade)
            return 0f;

        if (t < BlastRise)
            return _blastPeak * (t / BlastRise);
        if (t < BlastHold)
            return _blastPeak;

        return _blastPeak * (1f - (t - BlastHold) / BlastFade);
    }

    private float BaselineAlpha()
    {
        var player = _player.LocalEntity;
        if (player == null || !_entMan.TryGetComponent(player, out ConcussionComponent? comp))
            return 0f;

        var norm = comp.MaxLevel <= 0f ? 0f : _concussion.GetCurrentLevel(comp) / comp.MaxLevel;
        // Приглушение начинается с ~20% шкалы и доходит до 0.30 на максимуме.
        return Math.Clamp((norm - 0.2f) / 0.8f, 0f, 1f) * 0.30f;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (Suppressed)
            return false;

        if (!_entMan.TryGetComponent(_player.LocalEntity, out EyeComponent? eyeComp))
            return false;
        if (args.Viewport.Eye != eyeComp.Eye)
            return false;

        return GetAlpha() > 0.001f;
    }

    private float GetAlpha()
    {
        return Math.Max(BaselineAlpha(), Math.Max(ShotAlpha(), BlastAlpha()));
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var alpha = GetAlpha();
        if (alpha <= 0.001f)
            return;

        args.WorldHandle.DrawRect(args.WorldBounds, new Color(0f, 0f, 0f, alpha));
    }
}
