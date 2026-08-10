namespace Content.Shared._Duty.HealthPhrases;

/// <summary>
/// Визуальные константы реплик боли (popup + whisper).
/// </summary>
public static class DutyHealthPhrasesVisuals
{
    public const string FontPrototypeId = "Underdog";
    public const string FontPath = "/Fonts/Duty/Underdog/Underdog-Regular.ttf";

    /// <summary>Popup над головой — чуть крупнее стандартного Small (10).</summary>
    public const int PopupFontSize = 16;

    /// <summary>Whisper в чате.</summary>
    public const int WhisperFontSize = 14;

    public const string PainColorHex = "#DC143C";

    // ── Крик боли (DutyHealthScream) ─────────────────────────────────────────
    // Popup крупнее и краснее обычной боли — визуально явно отличим от DutyHealthPain.

    /// <summary>Popup крика — крупнее PopupFontSize.</summary>
    public const int ScreamFontSize = 20;

    public const string ScreamColorHex = "#FF0000";

    /// <summary>Сколько секунд после появления текст крика трясётся (дальше — неподвижен).</summary>
    public const float ScreamShakeDuration = 0.85f;

    /// <summary>Частота дрожания (в колебаниях в секунду).</summary>
    public const float ScreamShakeFrequency = 18f;

    /// <summary>Амплитуда дрожания по X, в пикселях.</summary>
    public const float ScreamShakeAmplitude = 6f;

    // ── Лёгкая тряска обычных popup боли (DutyHealthPainShake, HP < ~40%) ──────
    // Заметно слабее крика — это ещё не вопль, а нарастающая дрожь от боли.

    public const float PainShakeDuration = 0.5f;
    public const float PainShakeFrequency = 14f;
    public const float PainShakeAmplitude = 2.5f;
}
