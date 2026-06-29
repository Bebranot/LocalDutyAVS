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
/// Компонент навешивается серверной <c>LazarusSystem</c> на гуманоидов при спавне,
/// поэтому все поля имеют разумные значения по умолчанию (тюнятся здесь либо в YAML,
/// если компонент будет добавлен в прототип сущности).
/// </summary>
[RegisterComponent]
public sealed partial class LazarusComponent : Component
{
    /// <summary>
    /// Доля от диапазона "крит → смерть", оставшаяся до гибели, при пересечении
    /// которой крутится бросок. 0.15 = "осталось 15% до смерти".
    /// </summary>
    [DataField]
    public float NearDeathThreshold = 0.15f;

    /// <summary>Нижняя граница шанса срабатывания (5%).</summary>
    [DataField]
    public float MinChance = 0.05f;

    /// <summary>Верхняя граница шанса срабатывания (12%).</summary>
    [DataField]
    public float MaxChance = 0.12f;

    /// <summary>
    /// До какой доли порога крита лечится персонаж. Порог крита = "0 HP",
    /// поэтому 0.20 при пороге 100 ед. урона даёт итоговые 20 ед. урона ≈ 80 HP.
    /// </summary>
    [DataField]
    public float HealToCritFraction = 0.20f;

    /// <summary>Кулдаун между срабатываниями в рамках одной жизни.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(25);

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

    // ── Рантайм-состояние (не сериализуется в маппинг) ──────────────────────────

    /// <summary>Время, начиная с которого эффект снова доступен.</summary>
    [DataField]
    public TimeSpan NextAvailableTime;

    /// <summary>
    /// Находится ли персонаж сейчас в "зоне смерти" крита. Нужно, чтобы бросок
    /// крутился ровно один раз — в момент входа в зону, а не каждый тик.
    /// </summary>
    [DataField]
    public bool InDeathZone;
}
