// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Client.Resources;
using Content.Shared._Duty.Neurointerface;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.Neurointerface;

/// <summary>
/// _Duty: экранный оверлей критического отключения (flatline) при гибели носителя нейроинтерфейса.
/// Появляется после фазы глитча HUD (~0.85с), выводит «СИГНАЛ ПОТЕРЯН» и время раунда ЧЧ:ММ:СС,
/// затем плавно затухает (~4.6с) или плавно закрывается по клику, не блокируя интерфейс призрака.
/// </summary>
public sealed partial class DutyNeuroFlatlineOverlay : Overlay
{
    private const int FlatlineZIndex = 950;
    private const float FadeInDuration = 0.28f;
    private const float FadeOutDuration = 0.65f;

    private static readonly ProtoId<ShaderPrototype> CrtShaderId = "DutyNeuroCrt";

    private static readonly Color BackdropColor = new(0.025f, 0.004f, 0.008f, 0.88f);
    private static readonly Color TitleRed = Color.FromHex("#FF2A3C");
    private static readonly Color GlowRed = Color.FromHex("#8F0D1B");
    private static readonly Color SubRed = Color.FromHex("#D9707B");
    private static readonly Color HintRed = Color.FromHex("#8C4850");
    private static readonly Color FlatlineTrace = Color.FromHex("#FF3B4E");

    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private readonly ShaderInstance _crtShader;
    private readonly Font _titleFont;
    private readonly Font _monoFont;
    private readonly Font _hintFont;

    // Локализованные строки — один раз, а не каждый кадр.
    private readonly string _titleText;
    private readonly string _prefixText;
    private readonly string _prefixTextGorlex;
    private DutyNeuroNetwork _subTextNetwork;
    private readonly string _hintText;

    private DutyNeuroClientSystem? _clientSystem;

    /// <summary>Секунда, для которой собрана <see cref="_subText"/>; -1 — строку нужно пересобрать.</summary>
    private int _subTextSeconds = -1;
    private string _subText = string.Empty;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public DutyNeuroFlatlineOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = FlatlineZIndex;

        _titleText = Loc.GetString("duty-neuro-flatline-title");
        _prefixText = Loc.GetString("duty-neuro-flatline-prefix");
        _prefixTextGorlex = Loc.GetString("duty-neuro-flatline-prefix-gorlex");
        _hintText = Loc.GetString("duty-neuro-hud-flatline-hint");

        _crtShader = _proto.Index(CrtShaderId).InstanceUnique();
        _titleFont = _resCache.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 38);
        _monoFont = _resCache.GetFont(new[]
        {
            "/Fonts/RobotoMono/RobotoMono-Regular.ttf",
            "/Fonts/NotoSans/NotoSans-Regular.ttf"
        }, 16);
        _hintFont = _resCache.GetFont(new[]
        {
            "/Fonts/RobotoMono/RobotoMono-Regular.ttf",
            "/Fonts/NotoSans/NotoSans-Regular.ttf"
        }, 11);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _clientSystem ??= _entMan.System<DutyNeuroClientSystem>();
        return _clientSystem.ShouldShowFlatline;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        // BeforeDraw уже проверил ShouldShowFlatline в этом же кадре; здесь нужен только носитель.
        var clientSystem = _clientSystem;
        if (clientSystem == null || !clientSystem.TryGetActiveWearer(out _, out var wearer))
            return;

        var elapsed = clientSystem.FlatlineElapsed;
        var alpha = ComputeEnvelopeAlpha(elapsed) * clientSystem.DismissFade;
        if (alpha <= 0.005f)
            return;

        var handle = args.ScreenHandle;
        handle.SetTransform(Matrix3x2.Identity);

        var screenSize = _clyde.ScreenSize;
        var bounds = new UIBox2(0f, 0f, screenSize.X, screenSize.Y);
        var uiScale = (args.ViewportControl as Control)?.UIScale ?? 1f;

        var glitch = elapsed < 0.6f
            ? 0.75f * (1f - elapsed / 0.6f)
            : 0.12f;

        _crtShader.SetParameter("alpha", alpha);
        _crtShader.SetParameter("glitch_intensity", glitch);

        handle.UseShader(_crtShader);
        handle.DrawRect(bounds, BackdropColor);
        handle.UseShader(null);

        var centerY = screenSize.Y * 0.5f;
        var centerX = screenSize.X * 0.5f;

        // Горизонтальная красная линия flatline ЭКГ
        DrawFlatlineTrace(handle, screenSize.X, centerY - 42f * uiScale, alpha, uiScale);

        // Заголовок СИГНАЛ ПОТЕРЯН
        var titleDims = handle.GetDimensions(_titleFont, _titleText, uiScale);
        var titlePos = new Vector2(centerX - titleDims.X * 0.5f, centerY - titleDims.Y * 0.5f);

        handle.DrawString(_titleFont, titlePos + new Vector2(2f, 2f) * uiScale, _titleText, uiScale, GlowRed.WithAlpha(0.75f * alpha));
        handle.DrawString(_titleFont, titlePos, _titleText, uiScale, TitleRed.WithAlpha(alpha));

        // Моноширинная строка NT-SEC // FLATLINE // ЧЧ:ММ:СС (пересобирается раз в секунду)
        var deathTime = wearer.DeathRoundTime ?? clientSystem.GetCurrentRoundTime();
        if (deathTime < TimeSpan.Zero)
            deathTime = TimeSpan.Zero;

        var totalSeconds = (int) deathTime.TotalSeconds;
        if (totalSeconds != _subTextSeconds || wearer.Network != _subTextNetwork)
        {
            _subTextSeconds = totalSeconds;
            _subTextNetwork = wearer.Network;
            var time = $"{totalSeconds / 3600:00}:{totalSeconds / 60 % 60:00}:{totalSeconds % 60:00}";
            var prefix = wearer.Network == DutyNeuroNetwork.Gorlex ? _prefixTextGorlex : _prefixText;
            _subText = Loc.GetString("duty-neuro-hud-flatline-line", ("prefix", prefix), ("time", time));
        }

        var subDims = handle.GetDimensions(_monoFont, _subText, uiScale);
        var subPos = new Vector2(centerX - subDims.X * 0.5f, titlePos.Y + titleDims.Y + 12f * uiScale);
        handle.DrawString(_monoFont, subPos, _subText, uiScale, SubRed.WithAlpha(alpha));

        // Подсказка о закрытии по клику
        var hintDims = handle.GetDimensions(_hintFont, _hintText, uiScale);
        var hintPos = new Vector2(centerX - hintDims.X * 0.5f, subPos.Y + subDims.Y + 26f * uiScale);
        handle.DrawString(_hintFont, hintPos, _hintText, uiScale, HintRed.WithAlpha(0.75f * alpha));
    }

    private static void DrawFlatlineTrace(DrawingHandleScreen handle, int width, float y, float alpha, float uiScale)
    {
        var traceColor = FlatlineTrace.WithAlpha(0.72f * alpha);
        var dimTrace = FlatlineTrace.WithAlpha(0.28f * alpha);

        // Смещения масштабируем на uiScale, чтобы пик ЭКГ не «сплющивался» на HiDPI.
        var pulseX = width * 0.24f;
        var a = 18f * uiScale;
        var b = 16f * uiScale;
        var c = 10f * uiScale;
        var d = 6f * uiScale;
        var e = 2f * uiScale;
        handle.DrawLine(new Vector2(0f, y), new Vector2(pulseX - a, y), dimTrace);
        handle.DrawLine(new Vector2(pulseX - a, y), new Vector2(pulseX - c, y - b), traceColor);
        handle.DrawLine(new Vector2(pulseX - c, y - b), new Vector2(pulseX - e, y + c), traceColor);
        handle.DrawLine(new Vector2(pulseX - e, y + c), new Vector2(pulseX + d, y), traceColor);
        handle.DrawLine(new Vector2(pulseX + d, y), new Vector2(width, y), traceColor);
    }

    private static float ComputeEnvelopeAlpha(float elapsed)
    {
        var total = DutyNeuroClientSystem.FlatlineTotalDuration;
        if (elapsed <= 0f || elapsed >= total)
            return 0f;

        if (elapsed < FadeInDuration)
            return elapsed / FadeInDuration;

        var fadeOutStart = total - FadeOutDuration;
        if (elapsed > fadeOutStart)
            return MathF.Max(0f, (total - elapsed) / FadeOutDuration);

        return 1f;
    }
}
