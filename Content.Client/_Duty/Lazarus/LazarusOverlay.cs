using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Lazarus;

/// <summary>
/// Полноэкранная кинематика эффекта Лазаруса: затемнение в чёрный → дрожащая
/// рукописная фраза (шрифт Caveat) → плавный возврат. Анимируется самостоятельно
/// по <see cref="IGameTiming.RealTime"/>, клиентская <c>LazarusSystem</c> лишь
/// создаёт/удаляет оверлей и ведёт <see cref="Fade"/>.
///
/// Виньетку рисует отдельный <see cref="LazarusVignetteOverlay"/>: она живёт в
/// мировом пространстве и через шейдер, а чернота с текстом — экранная.
/// </summary>
public sealed class LazarusOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    /// <summary>
    /// Экранные оверлеи с одинаковым ZIndex движок рисует в произвольном порядке
    /// (<c>OverlayManager.Sort</c> — нестабильная сортировка по словарю типов). Без явного
    /// значения чернота то перекрывала попапы, то оказывалась под ними — от запуска к запуску.
    /// Сцена Лазаруса перекрывает всё.
    /// </summary>
    private const int SceneZIndex = 1000;

    private const string FontPath = "/Fonts/Duty/Caveat/Caveat-Regular.ttf";
    private const int BaseFontSize = 58;
    private static readonly Color TextColor = Color.FromHex("#E6DDCB");

    private readonly IClyde _clyde;
    private readonly IGameTiming _timing;
    private readonly Font _font;

    private readonly TimeSpan _start;
    private readonly string _phrase;

    private readonly float _fadeIn;
    private readonly float _hold;
    private readonly float _fadeOut;

    /// <summary>
    /// Общий множитель непрозрачности сцены, 0..1. Держит клиентская <c>LazarusSystem</c>:
    /// при обрыве кинематики (персонаж умер или его вытащили) она гасит его до нуля.
    /// </summary>
    public float Fade = 1f;

    public LazarusOverlay(
        IClyde clyde,
        IGameTiming timing,
        IResourceCache cache,
        string phrase,
        float fadeIn,
        float hold,
        float fadeOut)
    {
        _clyde = clyde;
        _timing = timing;
        _font = new VectorFont(cache.GetResource<FontResource>(FontPath), BaseFontSize);

        ZIndex = SceneZIndex;

        _start = timing.RealTime;
        _phrase = phrase;
        _fadeIn = MathF.Max(fadeIn, 0.01f);
        _hold = MathF.Max(hold, 0.01f);
        _fadeOut = MathF.Max(fadeOut, 0.01f);
    }

    /// <summary>Кинематика полностью отыграна — систему можно убрать.</summary>
    public bool Finished => Elapsed >= TotalDuration;

    private float Elapsed => (float)(_timing.RealTime - _start).TotalSeconds;

    private float TotalDuration => _fadeIn + _hold + _fadeOut;

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return Fade > 0.001f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        var size = _clyde.ScreenSize;
        var bounds = new UIBox2(0, 0, size.X, size.Y);

        var t = Elapsed;
        var tFadeIn = _fadeIn;
        var tHoldEnd = tFadeIn + _hold;
        var tFadeOutEnd = tHoldEnd + _fadeOut;

        var blackAlpha = GetBlackAlpha(t, tFadeIn, tHoldEnd, tFadeOutEnd) * Fade;
        if (blackAlpha > 0.001f)
            handle.DrawRect(bounds, new Color(0f, 0f, 0f, blackAlpha));

        var textAlpha = GetTextAlpha(t, tFadeIn, tHoldEnd, tFadeOutEnd) * Fade;
        if (textAlpha > 0.001f)
            DrawPhrase(handle, size, textAlpha, t);
    }

    private float GetBlackAlpha(float t, float tFadeIn, float tHoldEnd, float tFadeOutEnd)
    {
        if (t < tFadeIn)
            return Smooth(t / tFadeIn);
        if (t < tHoldEnd)
            return 1f;
        if (t < tFadeOutEnd)
            return 1f - Smooth((t - tHoldEnd) / _fadeOut);
        return 0f;
    }

    private float GetTextAlpha(float t, float tFadeIn, float tHoldEnd, float tFadeOutEnd)
    {
        // Появляется по мере затемнения, держится, тает вместе с чернотой.
        var appearStart = tFadeIn * 0.45f;
        if (t < appearStart)
            return 0f;
        if (t < tFadeIn)
            return Smooth((t - appearStart) / (tFadeIn - appearStart));
        if (t < tHoldEnd)
            return 1f;
        if (t < tFadeOutEnd)
            return 1f - Smooth((t - tHoldEnd) / _fadeOut);
        return 0f;
    }

    private void DrawPhrase(DrawingHandleScreen handle, Vector2i size, float alpha, float t)
    {
        var scale = Math.Clamp(size.Y / 1080f, 0.7f, 2.2f);
        var dims = handle.GetDimensions(_font, _phrase, scale);

        // Дрожание — "корявая", трясущаяся надпись. Слои разных частот дают
        // живой нервный тремор, а не равномерное покачивание.
        var jitterX = (MathF.Sin(t * 43f) * 0.6f + MathF.Sin(t * 27f) * 0.4f) * 3.6f * scale;
        var jitterY = (MathF.Cos(t * 39f) * 0.6f + MathF.Cos(t * 23f) * 0.4f) * 3.2f * scale;

        var center = new Vector2(size.X / 2f, size.Y / 2f);
        var pos = center - dims / 2f + new Vector2(jitterX, jitterY);

        // Слабая "тень"-двойник со смещением для рукописной шероховатости.
        handle.DrawString(_font, pos + new Vector2(2f * scale, 2f * scale), _phrase, scale,
            new Color(0f, 0f, 0f, alpha * 0.5f));
        handle.DrawString(_font, pos, _phrase, scale, TextColor.WithAlpha(alpha));
    }

    /// <summary>Кубическое сглаживание smoothstep для красивых переходов.</summary>
    private static float Smooth(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }
}
