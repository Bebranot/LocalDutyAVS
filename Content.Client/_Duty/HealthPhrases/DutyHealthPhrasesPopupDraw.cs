using Content.Shared._Duty.HealthPhrases;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.IoC;

namespace Content.Client._Duty.HealthPhrases;

/// <summary>
/// Отрисовка popup реплик боли (шрифт Underdog).
/// </summary>
public static class DutyHealthPhrasesPopupDraw
{
    private static Font? _font;
    private static Color _color;

    private static Font? _screamFont;
    private static Color _screamColor;

    public static void EnsureInitialized(IResourceCache cache)
    {
        if (_font != null)
            return;

        _font = new VectorFont(
            cache.GetResource<FontResource>(DutyHealthPhrasesVisuals.FontPath),
            DutyHealthPhrasesVisuals.PopupFontSize);
        _color = Color.FromHex(DutyHealthPhrasesVisuals.PainColorHex);

        _screamFont = new VectorFont(
            cache.GetResource<FontResource>(DutyHealthPhrasesVisuals.FontPath),
            DutyHealthPhrasesVisuals.ScreamFontSize);
        _screamColor = Color.FromHex(DutyHealthPhrasesVisuals.ScreamColorHex);
    }

    public static (Font Font, Color Color) GetStyle()
    {
        if (_font == null)
            EnsureInitialized(IoCManager.Resolve<IResourceCache>());

        return (_font!, _color);
    }

    public static (Font Font, Color Color) GetScreamStyle()
    {
        if (_screamFont == null)
            EnsureInitialized(IoCManager.Resolve<IResourceCache>());

        return (_screamFont!, _screamColor);
    }
}
