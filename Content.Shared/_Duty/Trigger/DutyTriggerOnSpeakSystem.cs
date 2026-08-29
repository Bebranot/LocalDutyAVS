using Content.Shared.Speech;
using Content.Shared.Speech.Components;
using Content.Shared.Trigger.Systems;
using Robust.Shared.Containers;

namespace Content.Shared._Duty.Trigger;

/// <summary>
/// Дёргает триггер на любую речь носителя. Нужен шоковому наморднику:
/// сам намордник лежит в слоте маски, а говорит владелец, поэтому кроме
/// собственной речи проверяем и речь того, в чьём контейнере мы находимся.
/// </summary>
public sealed class DutyTriggerOnSpeakSystem : EntitySystem
{
    [Dependency] private readonly TriggerSystem _trigger = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TriggerOnSpeakComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<TriggerOnSpeakComponent, ListenEvent>(OnListen);
    }

    private void OnMapInit(Entity<TriggerOnSpeakComponent> ent, ref MapInitEvent args)
    {
        EnsureComp<ActiveListenerComponent>(ent).Range = ent.Comp.ListenRange;
    }

    private void OnListen(Entity<TriggerOnSpeakComponent> ent, ref ListenEvent args)
    {
        var speaker = args.Source;

        if (speaker == ent.Owner)
        {
            _trigger.Trigger(ent, speaker, ent.Comp.KeyOut);
            return;
        }

        if (_container.TryGetContainingContainer((ent.Owner, null, null), out var container)
            && container.Owner == speaker)
        {
            _trigger.Trigger(ent, speaker, ent.Comp.KeyOut);
        }
    }
}
