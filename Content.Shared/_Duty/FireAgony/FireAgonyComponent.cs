using System.Numerics;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.FireAgony;

/// <summary>
/// _Duty: «Агония от огня». Когда игровой гуманоид горит выше порога
/// <see cref="EnterThreshold"/>, запускается принудительная сцена агонии: персонаж падает,
/// теряет управление (paralyze), роняет вещи из рук, кричит (ИС-чат + звук) и не контролирует
/// себя, пока огонь не потухнет / не истечёт <see cref="SafetyTimeout"/> / не наступит крит/смерть.
/// Огонь при этом тушится автоматически (<see cref="ExtinguishRatePerSecond"/>).
///
/// Компонент висит на базе гуманоидов; сцена стартует только у player-controlled сущностей
/// (гейт по <c>ActorComponent</c> в серверной <c>FireAgonySystem</c>). Клиент реагирует на
/// сетевой флаг <see cref="Active"/> кинематографикой (оверлей боли, зум, приглушение звука).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FireAgonyComponent : Component
{
    /// <summary>Идёт ли сейчас сцена агонии. Единственное сетевое состояние — клиент по нему
    /// включает/выключает кинематографику.</summary>
    [DataField, AutoNetworkedField]
    public bool Active;

    // ── Триггер / тайминги (конфиг из YAML, читается и на клиенте) ───────────
    /// <summary>Порог firestacks (шкала 0..MaximumFireStacks, дефолт 10) для входа в сцену.
    /// Обычный поджиг даёт ~2 стака и затухает — сцена для «настоящего» горения выше базы.</summary>
    [DataField]
    public float EnterThreshold = 3f;

    /// <summary>Слот, в котором ищется скафандр (герметичный костюм с защитой от давления).
    /// Любой надетый скафандр отменяет сцену целиком, даже если он не огнестойкий —
    /// хардсьюты, EVA, softsuits, пожарный и атмос-пожарный костюмы. <c>null</c> — проверка выключена.</summary>
    [DataField]
    public string? SuitSlot = "outerClothing";

    /// <summary>Порог резиста к высокотемпературному урону (0..1), с которого сцена не начинается:
    /// 0.65 = «65% и выше», ниже — сцена будет. Считается по обеим ступеням, через которые проходит
    /// урон от горения: <c>GetFireProtectionEvent</c> и коэффициент брони по
    /// <see cref="HeatDamageType"/>. Значение &gt; 1 отключает проверку.</summary>
    [DataField]
    public float HeatResistImmunity = 0.65f;

    /// <summary>Тип урона, по которому берётся коэффициент брони для <see cref="HeatResistImmunity"/>.</summary>
    [DataField]
    public ProtoId<DamageTypePrototype> HeatDamageType = "Heat";

    /// <summary>Минимальная длительность сцены — чтобы кратковременный всплеск firestacks не давал
    /// «мигнул и потух»: раньше этого времени выход по потуханию не срабатывает.</summary>
    [DataField]
    public TimeSpan MinSceneDuration = TimeSpan.FromSeconds(3);

    /// <summary>Жёсткий предел длительности сцены — обрыв, даже если огонь не потух.</summary>
    [DataField]
    public TimeSpan SafetyTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Скорость авто-тушения во время сцены (firestacks/сек, отрицательное). Медленнее
    /// старого Resist ×15 (−1.5) — сцена должна длиться, а не мигать; от порога 3 это ~5с.</summary>
    [DataField]
    public float ExtinguishRatePerSecond = -0.6f;

    /// <summary>Рефрактерный период после обрыва сцены (в т.ч. по таймауту): даёт игроку окно
    /// контроля, даже если огонь ещё горит выше порога — иначе сцена перезапустилась бы сразу.</summary>
    [DataField]
    public TimeSpan RearmDelay = TimeSpan.FromSeconds(8);

    // ── Крики / попапы ──────────────────────────────────────────────────────
    [DataField]
    public TimeSpan PopupIntervalMin = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan PopupIntervalMax = TimeSpan.FromSeconds(4.5);

    /// <summary>Кол-во строк стартового эмоута в локали (fire-agony-scream-1 .. -N). Эмоут
    /// (3-е лицо, без стилизации) звучит один раз при входе в сцену — дальше каждую секунду
    /// вместо него идёт стилизованный крик боли, см. <see cref="PainScreamLineCount"/>.</summary>
    [DataField]
    public int ScreamLineCount = 4;

    /// <summary>Кол-во строк крика боли в локали (fire-agony-pain-scream-1 .. -N). Один из них
    /// выкрикивается (say, ИС-чат, красный/Underdog/тряска — см. ChatSystem.SendDutyHealthScream)
    /// на каждом такте сцены, т.е. примерно раз в секунду.</summary>
    [DataField]
    public int PainScreamLineCount = 4;

    /// <summary>Кол-во строк-попапов в локали (fire-agony-popup-1 .. -N).</summary>
    [DataField]
    public int PopupLineCount = 3;

    // ── Помощь окружающих (клик ЛКМ = "сбить пламя") ─────────────────────────
    /// <summary>Насколько один успешный клик "сбить пламя" уменьшает FireStacks
    /// (отрицательное значение, как и <see cref="ExtinguishRatePerSecond"/>).</summary>
    [DataField]
    public float HelpExtinguishAmount = -1.5f;

    /// <summary>Кулдаун между успешными попытками "сбить пламя" — общий на цель, не даёт
    /// нескольким помощникам спамом резать FireStacks быстрее, чем раз в этот интервал.</summary>
    [DataField]
    public TimeSpan HelpExtinguishCooldown = TimeSpan.FromSeconds(1);

    // ── Кинематографика (клиент читает эти DataField'ы из прототипа) ─────────
    /// <summary>Целевой зум во время сцены. Меньше 1 = ближе к персонажу.</summary>
    [DataField]
    public Vector2 SceneZoom = new(0.65f, 0.65f);

    /// <summary>Буст громкости крика в дБ, чтобы он оставался слышен поверх приглушённого мастера.
    /// Понижено на 6дБ (~2 ступени) от изначальных 9 — крик был слишком пронзительным.</summary>
    [DataField]
    public float ScreamVolumeDb = 3f;

    /// <summary>Звук криков агонии (коллекция из <c>Resources/Audio/_Duty/Effects/Agony</c>).</summary>
    [DataField]
    public SoundSpecifier ScreamSound = new SoundCollectionSpecifier("DutyFireAgonyScreams");

    /// <summary>Громкость позиционного крика для ОКРУЖАЮЩИХ (дБ). Их звук не приглушён даком,
    /// поэтому без буста — в отличие от собственного крика игрока. Понижено на 6дБ (~2 ступени)
    /// от изначальных 0 — крик был слишком пронзительным.</summary>
    [DataField]
    public float BystanderScreamVolumeDb = -6f;

    // ── Серверная рантайм-служебка (не сетится) ─────────────────────────────
    /// <summary>Момент старта сцены (для <see cref="SafetyTimeout"/>).</summary>
    [ViewVariables]
    public TimeSpan StartTime;

    /// <summary>Раньше этого момента новая сцена не стартует (рефрактер после обрыва).</summary>
    [ViewVariables]
    public TimeSpan NextAllowedStart;

    /// <summary>Хэндл серверного позиционного крика для окружающих (зациклен; стоп на конце сцены).</summary>
    [ViewVariables]
    public EntityUid? ScreamStream;

    /// <summary>Когда в следующий раз показать попап.</summary>
    [ViewVariables]
    public TimeSpan NextPopupTime;

    /// <summary>Раньше этого момента новый клик "сбить пламя" не уменьшает FireStacks повторно
    /// (антиспам-кулдаун, серверно-авторитетный).</summary>
    [ViewVariables]
    public TimeSpan NextHelpAllowedTime;

    // ── Предсказанная служебка (не сетится) ─────────────────────────────────
    /// <summary>Анти-спам попапа «Вы в агонии!» на попытки действий (общее для клиента/сервера).</summary>
    [ViewVariables]
    public TimeSpan NextBlockedPopup;
}
