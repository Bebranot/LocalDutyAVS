using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.RoundStartVote;

/// <summary>
/// _Duty: конфигурация голосования "да/нет", которое можно запустить при старте раунда,
/// с игровым эффектом при победе "Да". Чтобы добавить новое такое голосование, достаточно
/// нового прототипа этого типа — код фреймворка трогать не нужно. Эффект применяет
/// отдельная система, которая слушает <see cref="Content.Server._Duty.RoundStartVote.RoundStartVoteEffectAppliedEvent"/>
/// и сверяет её с <see cref="EffectId"/>.
/// </summary>
[Prototype("roundStartVoteEvent")]
public sealed partial class RoundStartVoteEventPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Ключ локализации текста вопроса, показывается как заголовок голосования.</summary>
    [DataField(required: true)]
    public string Question = string.Empty;

    /// <summary>Ключ локализации описания эффекта — анонсируется в чат при старте голосования.</summary>
    [DataField(required: true)]
    public string Description = string.Empty;

    /// <summary>Длительность голосования.</summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Идентификатор эффекта, применяемого при победе "Да". Сопоставляется по строковому
    /// значению системой, которая реализует сам эффект.
    /// </summary>
    [DataField(required: true)]
    public string EffectId = string.Empty;

    /// <summary>Участвует ли прототип в случайном выборе голосования на старте раунда.</summary>
    [DataField]
    public bool Enabled = true;
}
