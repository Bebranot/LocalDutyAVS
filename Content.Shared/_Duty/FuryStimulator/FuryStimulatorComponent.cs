using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.FuryStimulator;

/// <summary>
/// _Duty: вешается на моба под действием стимулятора Fury-16.
/// Модель таймерная: фазы (<see cref="FuryStage"/>) сменяются по фиксированным длительностям.
/// Сервер — авторитет, пишет <see cref="Stage"/>; клиент читает её для визуала (оверлей, тряска).
/// Логика фаз/баффов — в <c>FuryStimulatorSystem</c> (сервер), общее — в <c>SharedFuryStimulatorSystem</c>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedFuryStimulatorSystem))]
public sealed partial class FuryStimulatorComponent : Component
{
    /// <summary>Текущая фаза. Читается клиентом для визуала.</summary>
    [ViewVariables, AutoNetworkedField]
    public FuryStage Stage = FuryStage.None;

    // ── Серверные поля (не сетевые) ───────────────────────────

    /// <summary>Момент времени, когда текущая фаза закончится и наступит следующая.</summary>
    [ViewVariables]
    public TimeSpan PhaseEnd;

    /// <summary>Время следующего тревожного pop-up (фазы ввода/спада).</summary>
    [ViewVariables]
    public TimeSpan NextPopup;

    /// <summary>
    /// Оружие/сам моб, на которые сейчас навешаны маркеры Fury. Отслеживаем явно, чтобы
    /// гарантированно снять их при смерти/передозе даже с пустыми руками (защита от утечек).
    /// </summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> AffectedWeapons = new();

    // ── Музыка (серверная, персональная; сетевые — сами аудио-сущности) ──

    [ViewVariables]
    public EntityUid? MusicStream;

    [ViewVariables]
    public EntityUid? MusicStreamFading;

    /// <summary>Индекс трека, который сейчас играет, чтобы не перезапускать зря.</summary>
    [ViewVariables]
    public int MusicTrack = -1;

    [ViewVariables]
    public float MusicGain;

    [ViewVariables]
    public float MusicGainFading;

    // ── Длительности фаз (сек) ────────────────────────────────

    [DataField]
    public float IntroDuration = 15f;

    [DataField]
    public float RampDuration = 35f;

    [DataField]
    public float PeakDuration = 35f;

    [DataField]
    public float DeclineDuration = 24f;

    /// <summary>
    /// Сила разового резкого толчка камеры в начале фазы ввода (укол). 0 = выкл.
    /// Движок клампит магнитуду толчка до 1.
    /// </summary>
    [DataField]
    public float IntroKickStrength = 1f;

    // ── Лечение в крите ───────────────────────────────────────

    /// <summary>
    /// Маленький хил, снимаемый раз в <see cref="CritHealInterval"/>, пока носитель под
    /// препаратом И в критическом состоянии. Вне крита не действует. Пропорционально
    /// уменьшает суммарный урон (сохраняя соотношение типов), постепенно вытягивая из крита.
    /// </summary>
    [DataField]
    public float CritHealAmount = 2f;

    /// <summary>Интервал лечения в крите (сек).</summary>
    [DataField]
    public float CritHealInterval = 1f;

    /// <summary>Момент следующего тика лечения в крите (сброс в 0 вне крита — при входе лечит сразу).</summary>
    [ViewVariables]
    public TimeSpan NextCritHeal;

    // ── Тюнинг ────────────────────────────────────────────────

    /// <summary>Целевая громкость музыки (линейная, 0..1).</summary>
    [DataField]
    public float MusicVolume = 1f;

    /// <summary>Скорость fade музыки, ед. громкости/сек. 2.0 = полный fade за 0.5 c.</summary>
    [DataField]
    public float MusicFadeSpeed = 2f;

    [DataField]
    public float PopupIntervalMin = 3f;

    [DataField]
    public float PopupIntervalMax = 6f;

    // ── Музыка фаз (4 разных трека) ───────────────────────────

    /// <summary>Фаза 1 «Ввод».</summary>
    [DataField]
    public SoundSpecifier MusicIntro =
        new SoundPathSpecifier("/Audio/_Duty/Effects/Fury-16/phase1.ogg");

    /// <summary>Фаза 2 «Разгон».</summary>
    [DataField]
    public SoundSpecifier MusicRamp =
        new SoundPathSpecifier("/Audio/_Duty/Effects/Fury-16/phase2.ogg");

    /// <summary>Фаза 3 «Пик».</summary>
    [DataField]
    public SoundSpecifier MusicPeak =
        new SoundPathSpecifier("/Audio/_Duty/Effects/Fury-16/phase3.ogg");

    /// <summary>Фаза 4 «Спад».</summary>
    [DataField]
    public SoundSpecifier MusicDecline =
        new SoundPathSpecifier("/Audio/_Duty/Effects/Fury-16/phase4.ogg");

    /// <summary>Звук передозировки (гиб).</summary>
    [DataField]
    public SoundSpecifier OverdoseSound =
        new SoundPathSpecifier("/Audio/Effects/gib1.ogg");

    // ── Передозировка ─────────────────────────────────────────

    /// <summary>
    /// Интенсивность взрыва при передозе. 0 = без AoE-урона по окружающим (только гиб носителя).
    /// </summary>
    [DataField]
    public float OverdoseExplosionIntensity;

    [DataField]
    public string OverdoseExplosionType = "Default";
}
