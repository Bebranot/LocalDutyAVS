// Портировано из Goob-Station (Content.Shared/_EinsteinEngines/InteractionVerbs), изначально Einstein Engines.
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Random;

namespace Content.Shared._Duty.InteractionVerbs.Actions;

/// <summary>
///     Действие, которое ничего не делает само по себе — только попап/звук/шанс успеха.
///     Имитирует старые "шанс показать попап" интеракции для чисто косметических вербов
///     (Обнять, Посмотреть на, Помахать, Постучать).
/// </summary>
[Serializable]
public sealed partial class NoOpAction : InteractionAction
{
    [DataField]
    public float SuccessChance = 1f;

    public override bool CanPerform(InteractionArgs args, InteractionVerbPrototype proto, bool isBefore, VerbDependencies deps)
    {
        if (isBefore)
            return true; // чтобы do-after (если есть) вообще мог начаться

        // >= 1f — всегда успех, <= 0f — всегда провал, иначе — бросок кубика.
        return SuccessChance > 0f && (SuccessChance >= 1f || deps.Random.Prob(SuccessChance));
    }

    public override bool Perform(InteractionArgs args, InteractionVerbPrototype proto, VerbDependencies deps)
    {
        return true;
    }
}
