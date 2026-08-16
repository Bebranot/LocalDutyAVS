// Портировано из Goob-Station (Content.Shared/_EinsteinEngines/InteractionVerbs), изначально Einstein Engines.
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics.CodeAnalysis;
using Content.Shared.Verbs;

namespace Content.Shared._Duty.InteractionVerbs;

/// <summary>
///     Аргументы одного вызова интеракт-верба: кто выполняет, на кого, чем.
/// </summary>
public sealed partial class InteractionArgs
{
    public EntityUid User, Target;
    public EntityUid? Used;
    public bool CanAccess, CanInteract, HasHands;

    /// <summary>
    ///     Словарь для обмена данными между стадиями одного и того же верба (например, между
    ///     CanPerform и Perform одного Action). Ключи произвольные, задаются самим Action/Requirement.
    /// </summary>
    /// <remarks>
    ///     Классам, не являющимся Action, писать сюда не рекомендуется — легко словить рассинхрон.
    /// </remarks>
    public Dictionary<string, object> Blackboard => _blackboardField ??= new(3);
    private Dictionary<string, object>? _blackboardField; // null по умолчанию, аллоцируется лениво

    public InteractionArgs(EntityUid user, EntityUid target, EntityUid? used, bool canAccess, bool canInteract, bool hasHands)
    {
        User = user;
        Target = target;
        Used = used;
        CanAccess = canAccess;
        CanInteract = canInteract;
        HasHands = hasHands;
    }

    public InteractionArgs(InteractionArgs other) : this(other.User, other.Target, other.Used, other.CanAccess, other.CanInteract, other.HasHands) {}

    public static InteractionArgs From<T>(GetVerbsEvent<T> ev) where T : Verb => new(ev.User, ev.Target, ev.Using, ev.CanAccess, ev.CanInteract, ev.Hands is not null);

    /// <summary>
    ///     Пытается достать значение из Blackboard как экземпляр конкретного типа.
    /// </summary>
    public bool TryGetBlackboard<T>(string key, [NotNullWhen(true)] out T? value)
    {
        value = default;
        if (_blackboardField == null || !_blackboardField.TryGetValue(key, out var maybeValue))
            return false;

        // Явную проверку типа не делаем — если кто-то напутал ключ/тип, это будет на его совести.
        value = (T?) maybeValue;
        return value != null;
    }
}
