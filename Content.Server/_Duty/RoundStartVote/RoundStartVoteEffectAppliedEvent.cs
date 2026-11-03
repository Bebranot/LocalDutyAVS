namespace Content.Server._Duty.RoundStartVote;

/// <summary>
/// _Duty: рассылается broadcast, когда голосование раунд-старта завершилось победой "Да".
/// Системы, реализующие конкретный эффект (например, дебаг-режим СМЭС), подписываются на
/// это событие и сверяют <see cref="EffectId"/> со своим — так голосования остаются полностью
/// в YAML и не завязаны в коде на конкретный эффект.
/// </summary>
public sealed class RoundStartVoteEffectAppliedEvent : EntityEventArgs
{
    public readonly string EffectId;

    public RoundStartVoteEffectAppliedEvent(string effectId)
    {
        EffectId = effectId;
    }
}
