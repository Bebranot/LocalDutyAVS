using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.FuryStimulator;

/// <summary>
/// _Duty: вешается на моба, в организме которого есть стимулятор Fury-16.
/// Хранит скрытый уровень вещества и текущую стадию. Сервер — авторитет, пишет <see cref="Metabolism"/>
/// и <see cref="Stage"/>; клиент читает их для визуала (оверлей, тряска экрана).
/// Логика убывания/стадий/баффов — в <c>FuryStimulatorSystem</c> (сервер), общее — в
/// <c>SharedFuryStimulatorSystem</c>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedFuryStimulatorSystem))]
public sealed partial class FuryStimulatorComponent : Component
{
    /// <summary>Текущее количество вещества в организме. Убывает со временем, авторитетно на сервере.</summary>
    [ViewVariables, AutoNetworkedField]
    public float Metabolism;

    /// <summary>Текущая стадия, вычисленная из <see cref="Metabolism"/>. Читается клиентом для визуала.</summary>
    [ViewVariables, AutoNetworkedField]
    public FuryStage Stage = FuryStage.None;

    // ── Серверные поля (не сетевые) ───────────────────────────

    /// <summary>
    /// Пока сейчас &gt; текущего времени — вещество не убывает (фиксированная фаза ввода 5–10 с).
    /// </summary>
    [ViewVariables]
    public TimeSpan HoldUntil;

    /// <summary>Время следующего тревожного pop-up (стадии Intro/Washout).</summary>
    [ViewVariables]
    public TimeSpan NextPopup;

    /// <summary>
    /// Оружие/сам моб, на которые сейчас навешаны маркеры Fury (ган-дебафф, мили-бафф).
    /// Отслеживаем явно, чтобы гарантированно снять их при смерти/передозе, даже если руки
    /// уже опустели (защита от утечек). Серверное поле.
    /// </summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> AffectedWeapons = new();

    // ── Музыка (серверная, персональная для игрока; сетевые — сами аудио-сущности) ──

    /// <summary>Текущий (проявляющийся) музыкальный стрим.</summary>
    [ViewVariables]
    public EntityUid? MusicStream;

    /// <summary>Затухающий музыкальный стрим (crossfade).</summary>
    [ViewVariables]
    public EntityUid? MusicStreamFading;

    /// <summary>Заглушка (индекс трека), под который сейчас играет музыка, чтобы не перезапускать зря.</summary>
    [ViewVariables]
    public int MusicTrack = -1;

    /// <summary>Текущая громкость проявляющегося стрима (0..1).</summary>
    [ViewVariables]
    public float MusicGain;

    /// <summary>Текущая громкость затухающего стрима (0..1).</summary>
    [ViewVariables]
    public float MusicGainFading;

    // ── Тюнинг ────────────────────────────────────────────────

    /// <summary>Скорость убывания вещества, ед/сек.</summary>
    [DataField]
    public float DecayPerSecond = 1f;

    /// <summary>Целевая громкость музыки (линейная, 0..1).</summary>
    [DataField]
    public float MusicVolume = 1f;

    /// <summary>Скорость fade музыки, ед. громкости в секунду.</summary>
    [DataField]
    public float MusicFadeSpeed = 0.6f;

    /// <summary>Минимальный/максимальный интервал между тревожными pop-up (сек).</summary>
    [DataField]
    public float PopupIntervalMin = 3f;

    [DataField]
    public float PopupIntervalMax = 6f;

    // ── Аудио-заглушки ────────────────────────────────────────
    // TODO _Duty: временно указывают на существующие треки проекта. Замени пути на реальные
    // PATH_SOUND_1/2/3, когда будут файлы (одна строка на каждый; или переопредели в прототипе).

    /// <summary>PATH_SOUND_1 — «разогрев» (Intro/Washout).</summary>
    [DataField]
    public SoundSpecifier MusicIntro =
        new SoundPathSpecifier("/Audio/_Duty/Ambient/AmbientPeace/mogott.ogg");

    /// <summary>PATH_SOUND_2 — пик (Peak).</summary>
    [DataField]
    public SoundSpecifier MusicPeak =
        new SoundPathSpecifier("/Audio/_Duty/Ambient/AmbientPeace/DSM/Countsman/countsman_1.ogg");

    /// <summary>PATH_SOUND_3 — спад (Decline).</summary>
    [DataField]
    public SoundSpecifier MusicDecline =
        new SoundPathSpecifier("/Audio/_Duty/Ambient/AmbientPeace/PortBalreska/balreska_1.ogg");

    /// <summary>Звук передозировки (взрыв/гиб).</summary>
    [DataField]
    public SoundSpecifier OverdoseSound =
        new SoundPathSpecifier("/Audio/Effects/gib1.ogg");

    // ── Передозировка ─────────────────────────────────────────

    /// <summary>
    /// Интенсивность взрыва при передозе. 0 = без AoE-урона по окружающим (только гиб самого игрока).
    /// По просьбе: окружающие не должны получать урон, поэтому по умолчанию 0.
    /// </summary>
    [DataField]
    public float OverdoseExplosionIntensity;

    /// <summary>Тип взрыва (если <see cref="OverdoseExplosionIntensity"/> &gt; 0).</summary>
    [DataField]
    public string OverdoseExplosionType = "Default";
}
