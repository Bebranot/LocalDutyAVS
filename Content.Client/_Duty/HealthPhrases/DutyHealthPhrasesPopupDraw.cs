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

    public static void EnsureInitialized(IResourceCache cache)
    {
        if (_font != null)
            return;

        _font = new VectorFont(
            cache.GetResource<FontResource>(DutyHealthPhrasesVisuals.FontPath),
            DutyHealthPhrasesVisuals.PopupFontSize);
        _color = Color.FromHex(DutyHealthPhrasesVisuals.PainColorHex);
    }

    public static (Font Font, Color Color) GetStyle()
    {
        if (_font == null)
            EnsureInitialized(IoCManager.Resolve<IResourceCache>());

        return (_font!, _color);
    }
}
