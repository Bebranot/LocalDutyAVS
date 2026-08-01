// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared.Alert;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Duty.Movement;

/// <summary>
/// _Duty: третья ступень передвижения — «спринт» (клавиша C) поверх ванильного бега.
/// За основу взят held-спринт Goob (зажал клавишу → расход стамины → кончилась → не спринтуешь),
/// но с ОТДЕЛЬНЫМ пулом выносливости (без вырубания, в отличие от боевой StaminaComponent).
/// Скорость спринта = бег × <see cref="SprintBonus"/> × ХП × выносливость × оружие-в-руках × слоты.
/// На нуле выносливости спринт становится медленнее обычного бега. См. <see cref="DutySprintSystem"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class DutyStaminaComponent : Component
{
    /// <summary>Зажата ли клавиша спринта (C). Ставится инпут-хендлером, предсказывается.</summary>
    [DataField, AutoNetworkedField]
    public bool WantsSprint;

    /// <summary>Текущий запас выносливости, 0..<see cref="Max"/>.</summary>
    [DataField, AutoNetworkedField]
    public float Current = 100f;

    /// <summary>Максимальный запас выносливости.</summary>
    [DataField, AutoNetworkedField]
    public float Max = 100f;

    /// <summary>Множитель скорости спринта на полной выносливости поверх бега (1.3 = +30%).</summary>
    [DataField, AutoNetworkedField]
    public float SprintBonus = 1.3f;

    /// <summary>Расход выносливости в секунду при спринте с движением. 5/с = ~20с спринта.</summary>
    [DataField, AutoNetworkedField]
    public float DrainPerSecond = 5f;

    /// <summary>
    /// Доля от <see cref="Max"/>, выше которой спринт идёт на полной скорости.
    /// Ниже — бонус скорости плавно падает к <see cref="MinEnduranceFactor"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float WindedFraction = 0.4f;

    /// <summary>
    /// Множитель выносливости при нулевом запасе. С <see cref="SprintBonus"/>=1.3 даёт
    /// 1.3×0.6=0.78 — медленнее обычного бега (загнанный «спринт»).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MinEnduranceFactor = 0.6f;

    /// <summary>Доп. множитель расхода при ранах и полной занятости слотов (1.6 = до +60%).</summary>
    [DataField, AutoNetworkedField]
    public float MaxDrainPenalty = 1.6f;

    /// <summary>Доля запаса, ниже которой ОБЫЧНЫЙ бег (без спринта) тоже штрафуется.</summary>
    [DataField, AutoNetworkedField]
    public float LowRunThreshold = 0.2f;

    /// <summary>Множитель скорости обычного бега при выносливости ниже <see cref="LowRunThreshold"/> (0.8 = −20%).</summary>
    [DataField, AutoNetworkedField]
    public float LowRunPenalty = 0.8f;

    // ── Восстановление ────────────────────────────────────────────────────────

    /// <summary>Пауза перед восстановлением, если выносливость потрачена НЕ в ноль (сек).</summary>
    [DataField, AutoNetworkedField]
    public float PartialRegenDelay = 3f;

    /// <summary>Скорость восстановления после частичной траты (ед/сек).</summary>
    [DataField, AutoNetworkedField]
    public float PartialRegenRate = 10f;

    /// <summary>Пауза перед восстановлением после ПОЛНОЙ траты (сек).</summary>
    [DataField, AutoNetworkedField]
    public float ExhaustRegenDelay = 5f;

    /// <summary>Скорость восстановления после полной траты (ед/сек) — медленное.</summary>
    [DataField, AutoNetworkedField]
    public float ExhaustRegenRate = 5f;

    /// <summary>Истощён ли пул (был выжат в ноль). Сбрасывается при полном восстановлении.</summary>
    [DataField, AutoNetworkedField]
    public bool Exhausted;

    /// <summary>Время, после которого можно начинать восстановление.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextRegen = TimeSpan.Zero;

    // ── Отдышка (клиентский звук) ─────────────────────────────────────────────

    /// <summary>Сколько секунд непрерывного спринта до появления отдышки.</summary>
    [DataField]
    public float BreathingStartSeconds = 15f;

    /// <summary>Отдышка прекращается, когда выносливость восстановилась до этой доли.</summary>
    [DataField]
    public float BreathingStopFraction = 0.3f;

    /// <summary>Звук отдышки для мужских голосов (рандом из коллекции).</summary>
    [DataField]
    public SoundSpecifier MaleBreathSound = new SoundCollectionSpecifier("DutyRunBreathMale");

    /// <summary>Звук отдышки для женских голосов.</summary>
    [DataField]
    public SoundSpecifier FemaleBreathSound = new SoundPathSpecifier("/Audio/_Duty/Effects/RunBreath/breath2woman.ogg");

    /// <summary>Накопленное время непрерывного спринта (локально, не сетевое).</summary>
    [ViewVariables]
    public float SprintElapsed;

    /// <summary>
    /// Активна ли отдышка. СЕТЕВОЕ (сервер — источник истины), иначе при снятии
    /// ActiveDutyStamina клиентский флаг застывал бы в true и звук крутился бы вечно.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Breathing;

    /// <summary>Кэш последнего применённого множителя — чтобы не дёргать рефреш каждый тик.</summary>
    [ViewVariables]
    public float LastSprintModifier = 1f;

    [DataField]
    public ProtoId<AlertPrototype> EnduranceAlert = "DutyEndurance";

    // ── Рывок: звук и пыль (порт с Goob) ──────────────────────────────────────

    /// <summary>Звук рывка в момент начала спринта.</summary>
    [DataField]
    public SoundSpecifier SprintStartSound = new SoundPathSpecifier("/Audio/_Duty/Effects/Sprint/sprint_puff.ogg");

    /// <summary>Большое облако пыли — спавнится один раз на старте рывка.</summary>
    [DataField]
    public EntProtoId SprintCloud = "DutySprintCloud";

    /// <summary>Мелкое облачко — спавнится периодически, пока бежишь.</summary>
    [DataField]
    public EntProtoId SprintCloudSmall = "DutySprintCloudSmall";

    /// <summary>Период между мелкими облачками пыли, сек.</summary>
    [DataField]
    public float TimeBetweenSteps = 0.6f;

    /// <summary>Служебное (только сервер): предыдущее состояние «реально бежит», для фронта звука.</summary>
    [ViewVariables]
    public bool WasSprinting;

    /// <summary>Служебное (только клиент): предыдущее состояние «реально бежит», для фронта пыли.</summary>
    [ViewVariables]
    public bool WasSprintingVisual;

    /// <summary>Служебное (только клиент): когда спавнили прошлое облачко пыли.</summary>
    [ViewVariables]
    public TimeSpan LastStepTime;

    // ── Тряска спрайта от усталости (порт с Goob, перевешен на наш пул) ────────
    // Переиспользуем готовую StunSystem.GetFatigueAnimation — она уже есть в форке и крутит
    // анимацию усталости от БОЕВОЙ стамины. Здесь те же параметры, но шкала — наша выносливость.

    /// <summary>Доля запаса, ниже которой спрайт начинает дышать и подрагивать.</summary>
    [DataField]
    public float ShakeThreshold = 0.5f;

    /// <summary>Минимальное вертикальное смещение «дыхания» на пороге.</summary>
    [DataField]
    public float BreathingAmplitudeMin = 0.04f;

    /// <summary>Сколько добавляется к дыханию к нулю выносливости.</summary>
    [DataField]
    public float BreathingAmplitudeMod = 0.04f;

    /// <summary>Минимальная амплитуда дрожи на пороге.</summary>
    [DataField]
    public float JitterAmplitudeMin;

    /// <summary>Сколько добавляется к дрожи к нулю выносливости.</summary>
    [DataField]
    public float JitterAmplitudeMod = 0.04f;

    /// <summary>Нижние множители дрожи по X и Y.</summary>
    [DataField]
    public Vector2 JitterMin = new(0.5f, 0.125f);

    /// <summary>Верхние множители дрожи по X и Y.</summary>
    [DataField]
    public Vector2 JitterMax = new(1f, 0.25f);

    /// <summary>Минимальная частота анимаций в секунду на пороге.</summary>
    [DataField]
    public float FrequencyMin = 0.25f;

    /// <summary>Сколько добавляется к частоте к нулю выносливости.</summary>
    [DataField]
    public float FrequencyMod = 1.75f;

    /// <summary>Сколько подрагиваний за одну анимацию.</summary>
    [DataField]
    public int Jitters = 4;

    /// <summary>Служебное (только клиент): прошлое смещение дрожи, чтобы не дёргать в тот же угол.</summary>
    [ViewVariables]
    public Vector2 LastJitter;

    /// <summary>Служебное (только клиент): смещение спрайта до начала тряски, для возврата.</summary>
    [ViewVariables]
    public Vector2 StartOffset;
}
