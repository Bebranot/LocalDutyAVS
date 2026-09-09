using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Lazarus;

/// <summary>
/// Механика "Last Standing" / эффект Лазаруса (референс — Casualties: Unknown).
/// Когда персонаж в крите и урон подбирается вплотную к смерти, с небольшим
/// шансом включается "вторая жизнь": персонаж рывком выкарабкивается из крита,
/// получает дозу стимуляторов, временно замедляется и видит атмосферную
/// кинематику (затемнение, музыка, рукописная фраза, виньетка).
///
/// Компонент навешивается серверной <c>LazarusSystem</c> на гуманоидов при вселении
/// разума в тело, поэтому все поля имеют разумные значения по умолчанию. Глобальные
/// ручки шанса (порог зоны смерти, границы шанса, кулдаун) живут в duty.lazarus_* CVar.
/// </summary>
[RegisterComponent]
public sealed partial class LazarusComponent : Component
{
    // Порог "зоны смерти", границы шанса и кулдаун живут в duty.lazarus_* CVar
    // (Content.Shared/_Duty/DutyCCVars.cs), а не здесь: компонент вешается кодом и ни в
    // одном YAML-прототипе не встречается, поэтому DataField'ы для них были лишним вторым
    // источником правды, который вдобавок нельзя было подкрутить без пересборки.

    /// <summary>
    /// До какой доли порога крита лечится персонаж. Порог крита = "0 HP",
    /// поэтому 0.20 при пороге 100 ед. урона даёт итоговые 20 ед. урона ≈ 80 HP.
    /// </summary>
    [DataField]
    public float HealToCritFraction = 0.20f;

    /// <summary>
    /// Задержка между запуском кинематики/музыки и реальным "вставанием"
    /// (лечением из крита). Подобрана под музыку — персонаж поднимается на спаде.
    /// </summary>
    [DataField]
    public TimeSpan ReviveDelay = TimeSpan.FromSeconds(5.5);

    /// <summary>Реагенты, вводимые в кровь при срабатывании (омнизин + эфедрин).</summary>
    [DataField]
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> InjectedReagents = new()
    {
        ["Omnizine"] = 10,
        ["Ephedrine"] = 15,
    };

    /// <summary>
    /// Цена воскрешения: до какой доли снижается максимальное здоровье после срабатывания
    /// (0.9 = остаётся 90% макс. HP). Реализуется пропорциональным понижением порогов крита и
    /// смерти. Применяется один раз за жизнь (повторные срабатывания не складываются).
    /// </summary>
    [DataField]
    public float MaxHpPenaltyFraction = 0.9f;

    /// <summary>Прототип статус-эффекта замедления.</summary>
    [DataField]
    public EntProtoId SlowdownEffect = "DutyLazarusSlowdownStatusEffect";

    /// <summary>Длительность замедления.</summary>
    [DataField]
    public TimeSpan SlowdownDuration = TimeSpan.FromSeconds(15);

    /// <summary>Множитель скорости ходьбы/бега на время замедления.</summary>
    [DataField]
    public float SlowdownModifier = 0.55f;

    // ── Параметры клиентской кинематики (передаются в событии) ──────────────────

    /// <summary>Затемнение экрана в чёрный, сек.</summary>
    [DataField]
    public float BlackoutFadeIn = 0.9f;

    /// <summary>Удержание чёрного экрана с надписью, сек.</summary>
    [DataField]
    public float BlackoutHold = 3.6f;

    /// <summary>Возврат из чёрного в виньетку, сек.</summary>
    [DataField]
    public float BlackoutFadeOut = 1.8f;

    /// <summary>Сколько держится виньетка после возврата, сек (~1 минута).</summary>
    [DataField]
    public float VignetteDuration = 60f;

    /// <summary>Угасание виньетки в конце, сек.</summary>
    [DataField]
    public float VignetteFadeOut = 3f;

    /// <summary>
    /// Сердцебиение — звук-подводка, начинается первым (на затемнении экрана).
    /// </summary>
    [DataField]
    public SoundSpecifier Heartbeat = new SoundCollectionSpecifier("DutyLazarusHeartbeat");

    /// <summary>Громкость сердцебиения, дБ (0 — без изменений, положительное — громче).</summary>
    [DataField]
    public float HeartbeatVolume = 4f;

    /// <summary>
    /// Основной звук "Last Standing" — вступает с задержкой и накладывается на
    /// сердцебиение, сочетаясь с ним.
    /// </summary>
    [DataField]
    public SoundSpecifier LastStand = new SoundCollectionSpecifier("DutyLazarusMusic");

    /// <summary>Громкость основного звука, дБ (0 — без изменений, положительное — громче).</summary>
    [DataField]
    public float LastStandVolume = 4f;

    /// <summary>
    /// Задержка вступления основного звука относительно сердцебиения, сек.
    /// Подобрана так, чтобы звуки "наезжали" друг на друга.
    /// </summary>
    [DataField]
    public float LastStandDelay = 2f;

    // ── Рантайм-состояние ───────────────────────────────────────────────────────
    // Намеренно без [DataField]: это живое состояние конкретной жизни, а не настройка.
    // NextAvailableTime — абсолютное CurTime, в сохранённой карте это был бы мусор.

    /// <summary>Время, начиная с которого эффект снова доступен.</summary>
    [ViewVariables]
    public TimeSpan NextAvailableTime;

    /// <summary>
    /// Крутился ли уже бросок в текущем эпизоде крита. Один бросок на эпизод:
    /// флаг сбрасывается только при выходе из состояния Critical, а не при выходе из
    /// "зоны смерти". Иначе медик, лечащий крита медленнее, чем тот задыхается, гонял бы
    /// его через границу зоны туда-сюда и прокручивал рулетку каждые несколько секунд.
    /// </summary>
    [ViewVariables]
    public bool RolledThisCrit;
}
