using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Heartbeat;

/// <summary>
/// _Duty: «уровень пульса» сущности. Чисто презентационная механика (как контузия) —
/// НЕ влияет на геймплей. Уровень считается сервером на события урона / смены mob-состояния
/// (НЕ каждый тик) и сетится в <see cref="Level"/> (Dirty ТОЛЬКО при реальной смене уровня).
///
/// Сам звук сердцебиения проигрывает КЛИЕНТ и ТОЛЬКО у владельца тела (Filter.Local,
/// не позиционно) — окружающие чужой пульс не слышат. Писка монитора на теле НЕТ (он
/// звучит только в анализаторе здоровья).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HeartbeatComponent : Component
{
    /// <summary>Текущий уровень пульса. Считается в <c>SharedHeartbeatSystem.GetLevel</c>.</summary>
    [DataField, AutoNetworkedField]
    public HeartbeatLevel Level = HeartbeatLevel.None;

    // ── Пороги (агрессивные: пульс реагирует почти на любой урон) ──────────────
    /// <summary>Ниже этой доли HP (1 = полное HP) начинается лёгкий пульс.</summary>
    [DataField]
    public float LightHpThreshold = 0.90f;

    /// <summary>Ниже этой доли HP пульс становится тяжёлым.</summary>
    [DataField]
    public float HeavyHpThreshold = 0.50f;

    /// <summary>С какой «глубины» крита (0 = только вошёл, 1 = у порога смерти) пульс = Critical.</summary>
    [DataField]
    public float CriticalDeepFraction = 0.50f;

    // ── Интервалы ударов (секунды между сэмплами) ──────────────────────────────
    // ВАЖНО: интервал не короче длины сэмпла (heavy ≈ 0.77с, light ≈ 0.3с), иначе
    // одиночные сэмплы наложатся друг на друга.
    [DataField]
    public float LightInterval = 2.0f;

    [DataField]
    public float HeavyInterval = 1.3f;

    [DataField]
    public float CriticalInterval = 0.9f;

    // ── Сэмплы (SoundCollection — легко добавить вариативность без правки кода) ─
    [DataField]
    public SoundSpecifier LightSound = new SoundCollectionSpecifier("DutyHeartbeatLight")
    {
        Params = AudioParams.Default.WithVolume(-3f),
    };

    [DataField]
    public SoundSpecifier HeavySound = new SoundCollectionSpecifier("DutyHeartbeatHeavy")
    {
        Params = AudioParams.Default.WithVolume(-2f),
    };

    // ── Рантайм-заглушка (не сетится) ──────────────────────────────────────────
    /// <summary>
    /// До этого времени уровень пульса принудительно = None (эффект Лазаруса / «вторая
    /// жизнь»): все наши звуки молчат, чтобы не клэшиться с кинематикой Last Standing.
    /// </summary>
    [ViewVariables]
    public TimeSpan SuppressUntil;
}

/// <summary>
/// Уровень пульса. Порядок = нарастание тяжести. Используется и компонентом тела,
/// и звуком в анализаторе здоровья (<c>HealthAnalyzerAudioEvent</c>).
/// </summary>
public enum HeartbeatLevel : byte
{
    None = 0,
    Light = 1,
    Heavy = 2,
    Critical = 3,
}
