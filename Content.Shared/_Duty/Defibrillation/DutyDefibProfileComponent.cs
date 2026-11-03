// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Defibrillation;

/// <summary>
/// Профиль автоматического дефибриллятора LifePak. Висит рядом с ванильным <c>DefibrillatorComponent</c>:
/// старт разряда (анализ, голос, зарядка) ведёт профиль, а сам удар и оживление — ванильный Zap.
/// Старые дефибы без этого компонента работают как раньше.
/// </summary>
/// <remarks>
/// Doafter разряда = <see cref="ZapLead"/> (голосовое вступление, после которого сервер решает, заряжать ли)
/// + время зарядки (<c>DefibrillatorComponent.DoAfterDuration</c>, совпадает с длиной звука зарядки).
/// Если задан <see cref="ZapDuration"/>, вступление удлиняется до него.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class DutyDefibProfileComponent : Component
{
    /// <summary>
    /// Двухшаговый режим (40s): разряд разрешён только после отдельного анализа по той же цели.
    /// </summary>
    [DataField]
    public bool RequireAnalysis;

    /// <summary>Длительность анализа в двухшаговом режиме. Заряд не тратится.</summary>
    [DataField]
    public TimeSpan AnalysisDuration = TimeSpan.FromSeconds(4);

    /// <summary>Сколько живёт результат анализа «разряд рекомендован».</summary>
    [DataField]
    public TimeSpan AnalysisWindow = TimeSpan.FromSeconds(20);

    /// <summary>Есть ли отдельный verb «Анализ ритма» с точным числом (45-A).</summary>
    [DataField]
    public bool RhythmCheckVerb;

    [DataField]
    public TimeSpan RhythmCheckDuration = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Вступление doafter разряда до начала зарядки: «Analyzing» у 45-A, «Stand clear» у 40s.
    /// В конце вступления сервер решает — заряжать или отменить doafter с голосом.
    /// </summary>
    [DataField]
    public TimeSpan ZapLead = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Полная длительность doafter разряда, не зависящая от уровня энергии. Ноль — вступление + зарядка как есть.
    /// Растягивается вступление, а не зарядка: зарядка обязана совпадать с длиной своего звука.
    /// </summary>
    [DataField]
    public TimeSpan ZapDuration = TimeSpan.Zero;

    /// <summary>Когда во время анализа 40s отзвучит «Analyzing»: проверка пульса и «Stand clear».</summary>
    [DataField]
    public TimeSpan AnalysisLead = TimeSpan.FromSeconds(1.5);

    /// <summary>Звук зарядки аппарата без уровней энергии (40s). У 45-A — свой у каждого уровня.</summary>
    [DataField]
    public SoundSpecifier? ChargeSound;

    /// <summary>
    /// За сколько секунд до конца зарядки 45-A звучит «Stand clear».
    /// </summary>
    [DataField]
    public TimeSpan StandClearBeforeShock = TimeSpan.FromSeconds(1.2);

    /// <summary>Минимальный интервал между щелчками энкодера — от спама verb-ом.</summary>
    [DataField]
    public TimeSpan EnergySwitchInterval = TimeSpan.FromSeconds(0.3);

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan NextEnergySwitch;

    /// <summary>Пауза после отказа аппарата, в которую новые применения игнорируются.</summary>
    [DataField]
    public TimeSpan RetryCooldown = TimeSpan.FromSeconds(2);

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan RetryAfter;

    /// <summary>
    /// Уровни энергии. Пусто — аппарат работает на значениях своего <c>DefibrillatorComponent</c> (40s).
    /// При выборе уровня его значения переписываются в <c>DefibrillatorComponent</c> и <c>PowerCellDraw</c>,
    /// чтобы ванильный код сам взял нужные время, звук, лечение и заряд.
    /// </summary>
    [DataField]
    public List<DutyDefibEnergyLevel> Energies = new();

    [DataField, AutoNetworkedField]
    public int CurrentEnergy;

    // --- Кеш анализа (40s). Живёт на аппарате, а не на цели: анализ — свойство конкретного прибора.

    [DataField, AutoNetworkedField]
    public EntityUid? LastAnalyzedTarget;

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan AnalysisExpires;

    [DataField, AutoNetworkedField]
    public bool ShockAdvised;

    // --- Пациент не в игре (SSD / нет разума): первое применение — предупреждение, повторное — разряд.

    [DataField, AutoNetworkedField]
    public EntityUid? SoftWarnedTarget;

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan SoftWarnedUntil;

    /// <summary>Сколько после предупреждения действует разрешение на принудительный разряд.</summary>
    [DataField]
    public TimeSpan SoftWarnWindow = TimeSpan.FromSeconds(20);

    /// <summary>Время последнего удара — сервер по нему отличает завершённый doafter от прерванного.</summary>
    [ViewVariables]
    public TimeSpan LastZap;

    // --- Одежда на груди.

    /// <summary>Скафандр на груди полностью блокирует разряд (40s) или только ослабляет его (45-A).</summary>
    [DataField]
    public bool SuitBlocks = true;

    [DataField]
    public float SuitHealMultiplier = 0.5f;

    [DataField]
    public float SuitShockMultiplier = 1.5f;

    /// <summary>Множитель лечения, если на груди броня (1 — броня не мешает).</summary>
    [DataField]
    public float ArmorHealMultiplier = 1f;

    // --- Гель и повторные удары.

    [DataField]
    public float GelHealMultiplier = 1.1f;

    /// <summary>Ожог электродами без геля; итог выбирается в диапазоне [Min, Max].</summary>
    [DataField]
    public int BurnMin = 3;

    [DataField]
    public int BurnMax = 5;

    /// <summary>Множители лечения для 1-го, 2-го, 3-го+ удара подряд.</summary>
    [DataField]
    public List<float> SpamHealMultipliers = new() { 1f };

    /// <summary>Множители шока пациенту для 1-го, 2-го, 3-го+ удара подряд.</summary>
    [DataField]
    public List<float> SpamShockMultipliers = new() { 1f, 1.5f, 2f };

    /// <summary>Пауза, после которой серия ударов считается заново.</summary>
    [DataField]
    public TimeSpan SpamResetTime = TimeSpan.FromSeconds(10);

    // --- Последствия.

    [DataField]
    public TimeSpan ArrhythmiaDuration = TimeSpan.FromMinutes(5);

    [DataField]
    public TimeSpan RevivalWeaknessDuration = TimeSpan.FromSeconds(60);

    /// <summary>Сколько добавить к шкале контузии при подъёме (короткое головокружение).</summary>
    [DataField]
    public float RevivalConcussion = 25f;

    // --- Голос аппарата.

    [DataField]
    public SoundSpecifier? VoiceAnalyzing = new SoundPathSpecifier("/Audio/_Duty/Defib/voice_analyzing.ogg");

    [DataField]
    public SoundSpecifier? VoiceShockAdvised = new SoundPathSpecifier("/Audio/_Duty/Defib/voice_shock_advised.ogg");

    [DataField]
    public SoundSpecifier? VoiceNoShockAdvised = new SoundPathSpecifier("/Audio/_Duty/Defib/voice_no_shock_advised.ogg");

    [DataField]
    public SoundSpecifier? VoicePushToShock = new SoundPathSpecifier("/Audio/_Duty/Defib/voice_push_to_shock.ogg");

    [DataField]
    public SoundSpecifier? VoiceStandClear = new SoundPathSpecifier("/Audio/_Duty/Defib/voice_stand_clear.ogg");

    [DataField]
    public SoundSpecifier? VoiceCheckPulse = new SoundPathSpecifier("/Audio/_Duty/Defib/voice_check_pulse.ogg");

    [DataField]
    public SoundSpecifier? ReadyTone = new SoundPathSpecifier("/Audio/_Duty/Defib/ready_tone.ogg");

    [DataField]
    public SoundSpecifier? ChargeDone = new SoundPathSpecifier("/Audio/_Duty/Defib/charge_done.ogg");

    /// <summary>Щелчки энкодера при шаге на следующий уровень энергии.</summary>
    [DataField]
    public SoundSpecifier? EnergyStepSound = new SoundPathSpecifier("/Audio/_Duty/Defib/encoder_step.ogg",
        AudioParams.Default.WithVariation(0.04f));

    /// <summary>Длинная прокрутка энкодера при возврате с верхнего уровня на нижний.</summary>
    [DataField]
    public SoundSpecifier? EnergyWrapSound = new SoundPathSpecifier("/Audio/_Duty/Defib/encoder_wrap.ogg",
        AudioParams.Default.WithVariation(0.04f));
}

/// <summary>
/// Один уровень энергии LifePak-45-A. Все числа — в YAML.
/// </summary>
[DataDefinition]
public sealed partial class DutyDefibEnergyLevel
{
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Doafter разряда; должен совпадать с длиной <see cref="ChargeSound"/>.</summary>
    [DataField]
    public TimeSpan DoAfter = TimeSpan.FromSeconds(5);

    /// <summary>Сколько заряда батареи уходит на удар (<c>PowerCellDraw.UseCharge</c>).</summary>
    [DataField]
    public float Charge = 100f;

    [DataField]
    public DamageSpecifier Heal = new();

    /// <summary>Шок пациенту (<c>DefibrillatorComponent.ZapDamage</c>).</summary>
    [DataField]
    public int ZapDamage = 5;

    [DataField]
    public SoundSpecifier? ChargeSound;

    /// <summary>Кардиоверсия: бьёт только по живому с аритмией и снимает её; по мёртвому отказывает.</summary>
    [DataField]
    public bool Cardioversion;

    /// <summary>Шанс вызвать аритмию первым ударом серии (только сервер).</summary>
    [DataField]
    public float ArrhythmiaChance;

    /// <summary>Шанс вызвать аритмию повторным ударом серии.</summary>
    [DataField]
    public float ArrhythmiaRepeatChance;
}
