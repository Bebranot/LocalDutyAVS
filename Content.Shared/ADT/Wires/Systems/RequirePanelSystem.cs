using Content.Shared.ADT.Wires.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Wires;

namespace Content.Shared.ADT.Wires.Systems;

public sealed partial class RequirePanelSystem : EntitySystem
{

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemSlotsRequirePanelComponent, ItemSlotInsertAttemptEvent>(ItemSlotInsertAttempt);
        SubscribeLocalEvent<ItemSlotsRequirePanelComponent, ItemSlotEjectAttemptEvent>(ItemSlotEjectAttempt);
    }

    private void ItemSlotInsertAttempt(Entity<ItemSlotsRequirePanelComponent> entity, ref ItemSlotInsertAttemptEvent args)
    {
        args.Cancelled = !CheckPanelStateForItemSlot(entity, args.Slot.ID);
    }
    private void ItemSlotEjectAttempt(Entity<ItemSlotsRequirePanelComponent> entity, ref ItemSlotEjectAttemptEvent args)
    {
        args.Cancelled = !CheckPanelStateForItemSlot(entity, args.Slot.ID);
    }

    public bool CheckPanelStateForItemSlot(Entity<ItemSlotsRequirePanelComponent> entity, string? slot)
    {
        var (uid, comp) = entity;

        if (slot == null)
            return false;

        // _Duty: было `return false`, но контракт метода — true значит "не отменять
        // взаимодействие" (см. вызывающий код: `args.Cancelled = !CheckPanelStateForItemSlot(...)`).
        // Из-за инверсии эта ветка ("слот не требует панели") на самом деле ВСЕГДА отменяла
        // вставку/извлечение для любого слота, не перечисленного в Slots — противоположно
        // комментарию "don't cancel interaction".
        // If slot not require wire panel - don't cancel interaction
        if (!comp.Slots.TryGetValue(slot, out var isRequireOpen))
            return true;

        // _Duty: аналогично — отсутствие WiresPanelComponent не должно намертво
        // блокировать слот, требующий панели, которой физически не существует.
        if (!TryComp<WiresPanelComponent>(uid, out var wiresPanel))
            return true;

        return wiresPanel.Open == isRequireOpen;
    }
}
