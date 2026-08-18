// _Duty: взаимное согласие (Handshake/HighFive) — оба участника должны кликнуть один и тот же
// верб друг на друга. Framework-DoAfter тут не подходит (см. SharedInteractionVerbsSystem.StartVerb)
// — он привязан к одному актёру и не умеет ждать решения второй стороны, поэтому "ожидание ответа"
// сделано отдельным состоянием с собственным TTL (см. PendingConsentRequestComponent) вместо Delay.
//
// Первый клик (A → B) создаёт исходящий запрос на A и возвращает false — это НЕ провал в смысле
// "что-то пошло не так", а просто другая ветка того же результата ("запрос отправлен, ждите
// ответа"), см. verbs.yml (effectFailure переопределён нейтральным текстом, без каскадной "красноты").
// Второй клик (B → A), пока запрос ещё не истёк, находит встречный запрос и возвращает true —
// это и есть настоящее завершение обмена, с обычным EffectSuccess.
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.InteractionVerbs.Components;

namespace Content.Shared._Duty.InteractionVerbs.Actions;

[Serializable]
public sealed partial class MutualConsentAction : InteractionAction
{
    /// <summary>Сколько времени исходящий запрос ждёт ответа, прежде чем сгорит.</summary>
    [DataField]
    public TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    public override bool CanPerform(InteractionArgs args, InteractionVerbPrototype proto, bool beforeDelay, VerbDependencies deps) => true;

    public override bool Perform(InteractionArgs args, InteractionVerbPrototype proto, VerbDependencies deps)
    {
        var now = deps.Timing.CurTime;

        // У цели есть встречный запрос именно ко мне на этот же верб? Тогда это ответ — обмен состоялся.
        if (deps.EntMan.TryGetComponent<PendingConsentRequestComponent>(args.Target, out var targetPending)
            && targetPending.Requests.TryGetValue(proto.ID, out var incoming)
            && incoming.AwaitingFrom == args.User
            && now < incoming.ExpiresAt)
        {
            targetPending.Requests.Remove(proto.ID);
            if (targetPending.Requests.Count == 0)
                deps.EntMan.RemoveComponent<PendingConsentRequestComponent>(args.Target);

            return true;
        }

        var myPending = deps.EntMan.EnsureComponent<PendingConsentRequestComponent>(args.User);

        // Уже жду ответа от кого-то на этот же верб — не затираем свежим запросом, пусть первый доиграет.
        if (myPending.Requests.TryGetValue(proto.ID, out var existing) && now < existing.ExpiresAt)
            return false;

        myPending.Requests[proto.ID] = new PendingConsentRequest(args.Target, now + RequestTimeout);
        return false;
    }
}
