// Портировано из Goob-Station (Content.Shared/_EinsteinEngines/InteractionVerbs), изначально Einstein Engines.
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.InteractionVerbs;

/// <summary>
///     Список интеракт-вербов, которые можно применить к этой сущности, помимо глобальных
///     (<see cref="InteractionVerbPrototype.Global"/>). Вешать вручную не обязательно — большинству
///     вербов (например, "Обнять", "Посмотреть на") этот компонент не нужен.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class InteractionVerbsComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<ProtoId<InteractionVerbPrototype>> AllowedVerbs = new();
}
