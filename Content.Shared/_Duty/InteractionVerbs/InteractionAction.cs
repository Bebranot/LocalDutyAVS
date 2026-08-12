// Портировано из Goob-Station (Content.Shared/_EinsteinEngines/InteractionVerbs), изначально Einstein Engines.
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.InteractionVerbs;

/// <summary>
///     Действие, выполняемое при успешном использовании интеракт-верба.
/// </summary>
[ImplicitDataDefinitionForInheritors, Serializable, NetSerializable]
public abstract partial class InteractionAction
{
    /// <summary>
    ///     Вызывается при построении списка вербов для цели, после всех остальных проверок верба.
    ///     Если возвращает false, верб не показывается пользователю.
    /// </summary>
    public virtual bool IsAllowed(
        InteractionArgs args,
        InteractionVerbPrototype proto,
        VerbDependencies deps
    ) => true;

    /// <summary>
    ///     Проверяет, можно ли выполнить верб прямо сейчас.
    ///     Если у верба есть do-after, вызывается и до, и после него.
    /// </summary>
    public abstract bool CanPerform(
        InteractionArgs args,
        InteractionVerbPrototype proto,
        bool beforeDelay,
        VerbDependencies deps
    );

    /// <summary>
    ///     Выполняет действие и возвращает признак успеха.
    /// </summary>
    public abstract bool Perform(
        InteractionArgs args,
        InteractionVerbPrototype proto,
        VerbDependencies deps
    );

    /// <summary>
    ///     Набор зависимостей, доступных интеракт-вербам без лишних [Dependency] на каждый Action/Requirement.
    /// </summary>
    /// <remarks>
    ///     Чтобы получить рабочий экземпляр, создайте новый и вызовите IoCManager.InjectDependencies().
    ///     Исключение — <see cref="WhitelistSystem"/>: это EntitySystem, а не сервис из глобального IoC,
    ///     поэтому его нужно проставить вручную (см. SharedInteractionVerbsSystem.Initialize).
    /// </remarks>
    public sealed class VerbDependencies
    {
        [Dependency] public readonly IEntityManager EntMan = default!;
        [Dependency] public readonly IPrototypeManager ProtoMan = default!;
        [Dependency] public readonly IRobustRandom Random = default!;
        [Dependency] public readonly IGameTiming Timing = default!;
        [Dependency] public readonly ISerializationManager Serialization = default!;
        public EntityWhitelistSystem WhitelistSystem = default!;
    }
}
