// Портировано из Goob-Station (Content.Shared/_EinsteinEngines/InteractionVerbs), изначально Einstein Engines.
// Contest-скейлинг (Mass/Stamina/Health) сознательно не портирован — сейчас ни один из наших
// вербов им не пользуется. Если понадобится (например, верб, зависящий от силы/здоровья цели),
// смотри Content.Shared/_EinsteinEngines/Contests/ContestsSystem.cs в Goob-Station и добавляй
// поля AllowedContests/ContestAdvantage* обратно сюда и в InteractionArgs/SharedInteractionVerbsSystem.
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;
using Robust.Shared.Utility;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Content.Shared._Duty.InteractionVerbs;

/// <summary>
///     Описывает один интеракт-верб (пункт во вкладке "Взаимодействовать" контекстного меню) —
///     кому он доступен, при каких условиях, что происходит при успехе/провале.
/// </summary>
[Prototype("Interaction")]
public sealed partial class InteractionVerbPrototype : IPrototype, IInheritingPrototype
{
    /// <inheritdoc />
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<InteractionVerbPrototype>))]
    public string[]? Parents { get; private set; }

    /// <inheritdoc />
    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; private set; }

    [IdDataField]
    public string ID { get; private set; } = default!;

    // Локализация по ID
    public string Name => Loc.TryGetString($"interaction-{ID}-name", out var loc) ? loc : ID;

    public string? Description => Loc.TryGetString($"interaction-{ID}-description", out var loc) ? loc : null;

    /// <summary>
    ///     Иконка, которую пользователь видит на кнопке верба.
    /// </summary>
    [DataField]
    public SpriteSpecifier? Icon;

    /// <summary>
    ///     Какие эффекты показываются при успешном/неуспешном выполнении верба.
    ///     Показываются после завершения do-after, если он есть.
    /// </summary>
    [DataField]
    public EffectSpecifier? EffectSuccess, EffectFailure;

    /// <summary>
    ///     Какие эффекты показываются при старте do-after этого верба.
    ///     Используется только если <see cref="Delay"/> не равен нулю.
    /// </summary>
    [DataField]
    public EffectSpecifier? EffectDelayed;

    /// <summary>
    ///     Условие доступности этого верба.
    /// </summary>
    [DataField]
    public InteractionRequirement? Requirement = null;

    /// <summary>
    ///     Действие этого верба — определяет, когда верб показывается, и что происходит при его выполнении.
    /// </summary>
    /// <remarks>Server-only, потому что большинству действий нужен авторитетный доступ к серверу.</remarks>
    [DataField(serverOnly: true)]
    public InteractionAction? Action = null;

    /// <summary>
    ///     Если true, верб скрывается, если <see cref="Requirement"/> не выполнен. Иначе — показывается, но отключён.
    /// </summary>
    [DataField]
    public bool HideByRequirement = false;

    /// <summary>
    ///     Если true, верб скрывается, если <see cref="Action"/> не проходит проверку IsAllowed. Иначе — показывается, но отключён.
    /// </summary>
    [DataField]
    public bool HideWhenInvalid = true;

    /// <summary>
    ///     Задержка выполнения верба. Всё, что больше нуля, приводит к do-after.
    /// </summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.Zero;

    /// <summary>
    ///     Кулдаун между использованиями этого верба. Применяется на пару пользователь-цель
    ///     (либо глобально, см. <see cref="GlobalCooldown"/>) и действует ещё до старта do-after.
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(0.5f);

    /// <summary>
    ///     Если true, кулдаун этого верба общий для всех целей, т.е. пользователь не сможет
    ///     применить тот же верб к любой другой сущности, пока кулдаун не закончится.
    /// </summary>
    [DataField]
    public bool GlobalCooldown = false;

    // Do-after создаётся императивно в SharedInteractionVerbsSystem.StartVerb (не как DataField) —
    // движковый DoAfterArgs в этом форке engine не имеет публичного конструктора без параметров,
    // поэтому тут только тюнингуемые через YAML флаги прерывания.

    /// <summary>
    ///     Прерывать ли do-after этого верба при получении урона.
    /// </summary>
    [DataField]
    public bool BreakOnDamage = true;

    /// <summary>
    ///     Прерывать ли do-after этого верба при перемещении.
    /// </summary>
    [DataField]
    public bool BreakOnMove = true;

    /// <summary>
    ///     Прерывать ли do-after этого верба при перемещении в невесомости.
    /// </summary>
    [DataField]
    public bool BreakOnWeightlessMove = true;

    [DataField]
    public RangeSpecifier Range = new();

    /// <summary>
    ///     Подразумевает ли это взаимодействие прямой физический контакт (отпечатки, волокна и т.п.).
    /// </summary>
    [DataField("contactInteraction")]
    public bool DoContactInteraction = true;

    [DataField]
    public bool RequiresHands = false;

    /// <summary>
    ///     Нужен ли пользователю обычный доступ к цели (руками или иначе), чтобы использовать верб.
    /// </summary>
    /// <remarks>Имя поля в YAML сохранено таким для совместимости с апстримом ("requiresCanInteract").</remarks>
    [DataField("requiresCanInteract")]
    public bool RequiresCanAccess = true;

    /// <summary>
    ///     Если true, верб можно применить пользователем на самого себя.
    /// </summary>
    [DataField]
    public bool AllowSelfInteract = false;

    /// <summary>
    ///     Приоритет верба. Вербы с более высоким приоритетом показываются выше в списке.
    /// </summary>
    [DataField]
    public int Priority = 0;

    /// <summary>
    ///     Если true, верб доступен на любой сущности (для которой Action это разрешает),
    ///     даже если её компоненты явно его не перечисляют через <see cref="InteractionVerbsComponent"/>.
    /// </summary>
    [DataField]
    public bool Global = false;

    /// <summary>
    ///     Если true и у верба Delay&lt;=0, клиент предсказывает собственную обратную связь (попап
    ///     успеха себе + звук) сразу по клику, не дожидаясь ответа сервера — см.
    ///     <see cref="SharedInteractionVerbsSystem.ShowPredictedOwnFeedback"/>. Ставить только на
    ///     вербы, чей успех гарантирован (NoOpAction с SuccessChance=1) — Action на клиенте
    ///     недоступен (serverOnly), клиент не может сам проверить, действительно ли верб пройдёт.
    /// </summary>
    [DataField]
    public bool PredictOwnFeedback = false;

    [DataDefinition, Serializable]
    public partial struct RangeSpecifier()
    {
        [DataField] public float Min = 0f;
        [DataField] public float Max = float.PositiveInfinity;
        [DataField] public bool Inverse = false;

        public bool IsInRange(float value) => (Inverse ? value < Min || value > Max : value >= Min && value <= Max);
    }

    [DataDefinition, Serializable]
    public partial class EffectSpecifier
    {
        [DataField]
        public EffectTargetSpecifier EffectTarget = EffectTargetSpecifier.TargetThenUser;

        /// <summary>
        ///     Какой InteractionPopup показать. Если null — попап не показывается.
        /// </summary>
        [DataField]
        public ProtoId<InteractionPopupPrototype>? Popup = null;

        /// <summary>
        ///     Звук, проигрываемый вместе с попапом. Если null — звука не будет.
        /// </summary>
        [DataField]
        public SoundSpecifier? Sound;

        /// <summary>
        ///     Если true, звук слышен всем в PVS попапа. Иначе — только цели и пользователю.
        /// </summary>
        [DataField]
        public bool SoundPerceivedByOthers = true;

        [DataField]
        public AudioParams SoundParams = new AudioParams()
        {
            Variation = 0.1f
        };
    }

    [Serializable, Flags]
    public enum EffectTargetSpecifier
    {
        /// <summary>
        ///     Попап показывается над исполнителем верба.
        /// </summary>
        User,
        /// <summary>
        ///     Попап показывается над целью верба.
        /// </summary>
        Target,
        /// <summary>
        ///     Пользователь видит попап над собой, остальные — над целью.
        /// </summary>
        UserThenTarget,
        /// <summary>
        ///     Цель видит попап над собой, остальные — над пользователем.
        /// </summary>
        TargetThenUser
    }
}
