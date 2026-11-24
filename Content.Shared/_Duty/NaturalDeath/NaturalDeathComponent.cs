using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.NaturalDeath;

/// <summary>
/// _Duty: естественная смерть (переход из Critical/SoftCritical в Dead без гиба, суицида и мгновенного ваншота).
/// Хранит параметры предсмертной агонии/затухания и рантайм-состояние на теле.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NaturalDeathComponent : Component
{
    /// <summary>Активна ли сейчас последовательность естественной смерти на теле.</summary>
    [DataField, AutoNetworkedField]
    public bool Active;

    /// <summary>Длительность плавного затухания восприятия на клиенте (сек).</summary>
    [DataField]
    public float FadeOutDuration = 2.8f;

    /// <summary>Длительность плавного возвращения зрения при реанимации дефибриллятором (сек).</summary>
    [DataField]
    public float ReviveFadeInDuration = 0.6f;

    /// <summary>Окно блокировки случайного мгновенного выхода в призрака через движение сразу после смерти.</summary>
    [DataField]
    public TimeSpan GhostLockDuration = TimeSpan.FromSeconds(2.8);

    /// <summary>Длительность короткой предсмертной судороги / потери тонуса тела (сек).</summary>
    [DataField]
    public float JitterDuration = 1.1f;

    [DataField]
    public float JitterAmplitude = 3.5f;

    [DataField]
    public float JitterFrequency = 10f;

    /// <summary>Звук затухающего звона в ушах.</summary>
    [DataField]
    public SoundSpecifier TinnitusSound = new SoundPathSpecifier("/Audio/_Duty/Concussion/tinnitus.ogg");

    /// <summary>Звук последних глухих ударов сердца.</summary>
    [DataField]
    public SoundSpecifier FinalBeatSound = new SoundPathSpecifier("/Audio/_Duty/Heartbeat/heartthump-heavy.ogg");

    /// <summary>Количество вариантов предсмертного эмоута в локализации (duty-natural-death-emote-1..N).</summary>
    [DataField]
    public int EmoteLineCount = 4;

    /// <summary>Количество вариантов тихого сообщения самому умирающему (duty-natural-death-self-1..N).</summary>
    [DataField]
    public int SelfPopupLineCount = 3;

    // ── Серверное рантайм-состояние (не сетится) ─────────────────────────────
    [ViewVariables]
    public TimeSpan GhostBlockUntil;

    [ViewVariables]
    public TimeSpan NextGhostBlockPopup;

    [ViewVariables]
    public TimeSpan JitterRemoveAt;

    [ViewVariables]
    public bool AppliedJitter;

    [ViewVariables]
    public bool SuppressedBySuicide;

    [ViewVariables]
    public bool SuppressedByGib;

    [ViewVariables]
    public TimeSpan LastGhostAttemptTime;
}
