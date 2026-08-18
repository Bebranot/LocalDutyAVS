// _Duty: серверная бухгалтерия для вербов взаимного согласия (Handshake/HighFive) — см.
// Content.Shared/_Duty/InteractionVerbs/Actions/MutualConsentAction.cs и
// Content.Server/_Duty/InteractionVerbs/MutualConsentSystem.cs (чистка просроченных запросов).
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.InteractionVerbs.Components;

/// <summary>
///     Висит на сущности, которая первой кликнула верб взаимного согласия и теперь ждёт, что цель
///     кликнет тот же верб в ответ. Не сетевой — клиенту не нужно знать, кто кого ждёт, это чисто
///     серверное состояние одного диалога.
/// </summary>
[RegisterComponent]
public sealed partial class PendingConsentRequestComponent : Component
{
    /// <summary>
    ///     Активные исходящие запросы, по одному на верб — так независимый Handshake и HighFive
    ///     не затирают друг друга.
    /// </summary>
    [ViewVariables]
    public Dictionary<ProtoId<InteractionVerbPrototype>, PendingConsentRequest> Requests = new();
}

/// <summary>
///     Один исходящий запрос согласия: кого ждём в ответ (<see cref="AwaitingFrom"/> — цель
///     исходного клика) и до какого момента запрос ещё действителен.
/// </summary>
public readonly record struct PendingConsentRequest(EntityUid AwaitingFrom, TimeSpan ExpiresAt);
