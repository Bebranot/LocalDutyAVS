using System.Numerics;
using Content.Shared.CCVar;
using Content.Shared._Duty.Concussion;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
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
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private readonly SharedConcussionSystem _concussion;

    /// <summary>Качание/мутность экрана для головокружения — переиспользуем ванильный «Drunk».</summary>
    private static readonly ProtoId<ShaderPrototype> DizzyShader = "Drunk";
    private readonly ShaderInstance _dizzyShader;

    /// <summary>Тонирование экрана по шкале — «вымывание» цвета (порт color_tint из Goob).</summary>
    private static readonly ProtoId<ShaderPrototype> TintShader = "DutyColorTint";
    private readonly ShaderInstance _tintShader;

    // ── Тинт (обесцвечивание/тонирование) — ПОДКРУТКА ЗДЕСЬ ──────────────────
    /// <summary>Цвет, к которому тянем картинку. Малонасыщенный = в основном обесцвечивает + чуть темнит.</summary>
    private static readonly Color TintColor = new(0.50f, 0.42f, 0.40f);
    /// <summary>Сила тинта на полной шкале (0..1).</summary>
    private const float TintMaxAmount = 0.5f;
    /// <summary>С какой доли шкалы тинт начинает проявляться.</summary>
    private const float TintStartFraction = 0.2f;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    // Нужен скриншот экрана под шейдер головокружения.
    public override bool RequestScreenTexture => true;

    /// <summary>Сервер/мобстейт могут полностью гасить эффект (смерть, выключено в CVar).</summary>
    public bool Suppressed;

    private bool _reducedMotion;

    // ── Импульсы ────────────────────────────────────────────────────────────
    private const float ShotDuration = 0.2f;
    private const float ShotRise = 0.05f;

    private const float BlastRise = 0.12f;
    private const float BlastHold = 3.0f;
    private const float BlastFade = 4.0f;

    // ── Головокружение (Drunk-шейдер) ────────────────────────────────────────
    private const float DizzyDuration = 3.0f;
    /// <summary>Пиковое значение boozePower (>0.5 включает второй слой искажения).</summary>
    private const float DizzyPeak = 0.6f;

    private TimeSpan _shotStart;
    private float _shotPeak;

    private TimeSpan _blastStart;
    private float _blastPeak;

    private TimeSpan _dizzyStart;
    private float _dizzyIntensity;

    public ConcussionOverlay()
    {
        IoCManager.InjectDependencies(this);
        _concussion = _entMan.System<SharedConcussionSystem>();
        _dizzyShader = _proto.Index(DizzyShader).InstanceUnique();
        _tintShader = _proto.Index(TintShader).InstanceUnique();
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
        _blastPeak = Math.Clamp(0.85f + 0.13f * Math.Clamp(intensity, 0f, 1f), 0f, 0.98f);
        _blastStart = _timing.CurTime;
    }

    public void SetDizzy(float intensity)
    {
        intensity = Math.Clamp(intensity, 0f, 1f);
        // С reduced motion качание щадящее.
        if (_reducedMotion)
            intensity *= 0.4f;

        _dizzyStart = _timing.CurTime;
        _dizzyIntensity = intensity;
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

    /// <summary>Текущая сила головокружения (boozePower для шейдера), линейно гаснет за 3с.</summary>
    private float DizzyPower()
    {
        if (_dizzyIntensity <= 0f)
            return 0f;

        var t = (float)(_timing.CurTime - _dizzyStart).TotalSeconds;
        if (t < 0f || t > DizzyDuration)
            return 0f;

        var env = 1f - t / DizzyDuration;
        return DizzyPeak * _dizzyIntensity * env;
    }

    /// <summary>Текущая заполненность шкалы контузии (0..1) у локального игрока.</summary>
    private float CurrentNorm()
    {
        var player = _player.LocalEntity;
        if (player == null || !_entMan.TryGetComponent(player, out ConcussionComponent? comp))
            return 0f;

        return comp.MaxLevel <= 0f ? 0f : _concussion.GetCurrentLevel(comp) / comp.MaxLevel;
    }

    private float BaselineAlpha()
    {
        // Приглушение начинается с ~10% шкалы и доходит до 0.40 на максимуме.
        return Math.Clamp((CurrentNorm() - 0.1f) / 0.9f, 0f, 1f) * 0.40f;
    }

    /// <summary>Сила тонирования по шкале: «вымывание» цвета растёт с контузией.</summary>
    private float TintAmount()
    {
        return Math.Clamp((CurrentNorm() - TintStartFraction) / (1f - TintStartFraction), 0f, 1f) * TintMaxAmount;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (Suppressed)
            return false;

        if (!_entMan.TryGetComponent(_player.LocalEntity, out EyeComponent? eyeComp))
            return false;
        if (args.Viewport.Eye != eyeComp.Eye)
            return false;

        return GetAlpha() > 0.001f || DizzyPower() > 0.001f || TintAmount() > 0.001f;
    }

    private float GetAlpha()
    {
        return Math.Max(BaselineAlpha(), Math.Max(ShotAlpha(), BlastAlpha()));
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;

        // 1. Тонирование экрана по шкале (вымывание цвета). Сэмплит скриншот экрана.
        var tint = TintAmount();
        if (tint > 0.001f && ScreenTexture != null)
        {
            _tintShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
            _tintShader.SetParameter("tint_color", new Vector3(TintColor.R, TintColor.G, TintColor.B));
            _tintShader.SetParameter("tint_amount", tint);
            handle.UseShader(_tintShader);
            handle.DrawRect(args.WorldBounds, Color.White);
            handle.UseShader(null);
        }

        // 2. Качание/мутность (Drunk). Во время 3-сек головокружения перекрывает тинт — это ок.
        var dizzy = DizzyPower();
        if (dizzy > 0.001f && ScreenTexture != null)
        {
            _dizzyShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
            _dizzyShader.SetParameter("boozePower", dizzy);
            handle.UseShader(_dizzyShader);
            handle.DrawRect(args.WorldBounds, Color.White);
            handle.UseShader(null);
        }

        var alpha = GetAlpha();
        if (alpha > 0.001f)
            handle.DrawRect(args.WorldBounds, new Color(0f, 0f, 0f, alpha));
    }
}
