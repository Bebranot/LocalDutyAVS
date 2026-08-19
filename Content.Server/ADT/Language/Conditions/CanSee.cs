using Content.Shared.ActionBlocker;
using Content.Shared.ADT.Language;
using Content.Shared.Eye.Blinding.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server.ADT.Language;

/// <summary>
/// Сообщение увидят только зрячие персонажи
/// </summary>
[DataDefinition]
public sealed partial class CanSee : ILanguageCondition
{
    public ProtoId<LanguagePrototype> Language { get; set; }

    [DataField]
    public bool RaiseOnListener { get; set; } = true;

    public bool Condition(EntityUid targetEntity, EntityUid? source, IEntityManager entMan)
    {
        var ev = new CanSeeAttemptEvent();
        entMan.EventBus.RaiseLocalEvent(targetEntity, ev);

        // _Duty: было `return ev.Blind` — `Condition == true` для слушателя
        // означает "оставить получателем" (см. вызовы с `RaiseOnListener`
        // в ChatSystem.cs/ChatSystem.ADT.cs: `if (!item.Condition(...))
        // condition = false;` → слушатель исключается при false). `ev.Blind`
        // истинно именно у ОСЛЕПШИХ — то есть раньше условие "сообщение
        // увидят только зрячие" (см. doc-комментарий класса, `BaseEmoteLanguage`
        // в base.yml, от которого наследуется SignLanguage) на деле оставляло
        // получателями ТОЛЬКО слепых и исключало зрячих — ровно наоборот.
        return !ev.Blind;
    }
}
