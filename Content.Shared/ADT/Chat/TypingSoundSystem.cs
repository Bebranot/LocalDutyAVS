using Content.Shared.Chat;
using Content.Shared.Chat.TypingIndicator;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared.ADT.Chat;

public sealed partial class TypingSoundSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TypingSoundComponent, TypingIndicatorStateChangedEvent>(OnTypingStateChanged);
        SubscribeLocalEvent<TypingSoundComponent, EntitySpokeEvent>(OnEntitySpoke);
        SubscribeLocalEvent<TypingSoundComponent, GotEquippedEvent>(GotEquipped);
        SubscribeLocalEvent<TypingSoundComponent, GotUnequippedEvent>(GotUnequipped);
    }

    private void OnTypingStateChanged(EntityUid uid, TypingSoundComponent component, TypingIndicatorStateChangedEvent args)
    {
        if (args.NewState != TypingIndicatorState.Typing)
            return;
        if (!_timing.IsFirstTimePredicted)
            return;

        _audio.PlayPvs(component.TypingSound, uid);
    }

    private void OnEntitySpoke(EntityUid uid, TypingSoundComponent component, EntitySpokeEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        _audio.PlayPvs(component.MessageSentSound, uid);
    }

    private void GotEquipped(Entity<TypingSoundComponent> ent, ref GotEquippedEvent args)
    {
        var typingSound = EnsureComp<TypingSoundComponent>(args.Equipee);
        typingSound.TypingSound = ent.Comp.TypingSound;
        typingSound.MessageSentSound = ent.Comp.MessageSentSound;
    }

    private void GotUnequipped(Entity<TypingSoundComponent> ent, ref GotUnequippedEvent args)
    {
        // _Duty: раньше компонент со звуком печати у владельца сносился безусловно,
        // даже если на нём всё ещё надет другой предмет с собственным TypingSoundComponent
        // (например второй такой же предмет в другом слоте) — тот предмет молча терял звук
        // печати до следующего пере-надевания. Ищем среди оставшихся надетых предметов
        // ещё один источник звука и переносим его настройки вместо удаления компонента.
        if (TryComp(args.Equipee, out TypingSoundComponent? ownerSound))
        {
            var enumerator = _inventory.GetSlotEnumerator(args.Equipee);
            while (enumerator.NextItem(out var item, out _))
            {
                if (item == ent.Owner)
                    continue;

                if (!TryComp(item, out TypingSoundComponent? otherSound))
                    continue;

                ownerSound.TypingSound = otherSound.TypingSound;
                ownerSound.MessageSentSound = otherSound.MessageSentSound;
                return;
            }
        }

        RemCompDeferred<TypingSoundComponent>(args.Equipee);
    }
}
