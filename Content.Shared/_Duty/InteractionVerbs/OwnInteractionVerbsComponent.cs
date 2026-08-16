// Портировано из Goob-Station (Content.Shared/_EinsteinEngines/InteractionVerbs), изначально Einstein Engines.
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.InteractionVerbs;

/// <summary>
///     Список вербов, которые эта сущность может выполнить сама, на любой подходящей цели.
///     Вешается автоматически (EnsureComp) на любого, кто открывает меню взаимодействия — руками не трогать.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OwnInteractionVerbsComponent : Component
{
    public override bool SendOnlyToOwner => true;

    [DataField, AutoNetworkedField]
    public List<ProtoId<InteractionVerbPrototype>> AllowedVerbs = new();

    // Слишком волатильно, чтобы сетевать — клиент и сервер ведут этот словарь независимо друг от друга.
    [NonSerialized, ViewVariables]
    public Dictionary<(ProtoId<InteractionVerbPrototype>, EntityUid), TimeSpan> Cooldowns = new();
}
