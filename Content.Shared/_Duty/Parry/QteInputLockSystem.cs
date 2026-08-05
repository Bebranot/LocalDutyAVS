using Content.Shared._Duty.Parry.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Movement.Events;
using Content.Shared.Throwing;

namespace Content.Shared._Duty.Parry;

/// <summary>
/// Блокирует все обычные действия участника QTE-катсцены. Набор Attempt-событий повторяет тот,
/// что отменяет StunnedComponent — чтобы «замороженный» участник не мог ни ходить, ни
/// разоружиться, ни использовать предметы, пока идёт дуэль.
/// </summary>
public sealed class QteInputLockSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<QteInputLockComponent, ComponentStartup>(OnStartupShutdown);
        SubscribeLocalEvent<QteInputLockComponent, ComponentShutdown>(OnStartupShutdown);

        SubscribeLocalEvent<QteInputLockComponent, UpdateCanMoveEvent>(OnMoveAttempt);
        SubscribeLocalEvent<QteInputLockComponent, InteractionAttemptEvent>(OnInteractAttempt);
        SubscribeLocalEvent<QteInputLockComponent, ChangeDirectionAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<QteInputLockComponent, UseAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<QteInputLockComponent, ThrowAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<QteInputLockComponent, DropAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<QteInputLockComponent, AttackAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<QteInputLockComponent, PickupAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<QteInputLockComponent, IsEquippingAttemptEvent>(OnEquipAttempt);
        SubscribeLocalEvent<QteInputLockComponent, IsUnequippingAttemptEvent>(OnUnequipAttempt);
    }

    private void OnStartupShutdown(EntityUid uid, QteInputLockComponent component, EntityEventArgs args)
    {
        _blocker.UpdateCanMove(uid);
    }

    private void OnMoveAttempt(EntityUid uid, QteInputLockComponent component, UpdateCanMoveEvent args)
    {
        if (component.LifeStage > ComponentLifeStage.Running)
            return;

        args.Cancel();
    }

    private void OnInteractAttempt(Entity<QteInputLockComponent> ent, ref InteractionAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnAttempt(EntityUid uid, QteInputLockComponent component, CancellableEntityEventArgs args)
    {
        args.Cancel();
    }

    private void OnEquipAttempt(EntityUid uid, QteInputLockComponent component, IsEquippingAttemptEvent args)
    {
        if (args.Equipee == uid)
            args.Cancel();
    }

    private void OnUnequipAttempt(EntityUid uid, QteInputLockComponent component, IsUnequippingAttemptEvent args)
    {
        if (args.Unequipee == uid)
            args.Cancel();
    }
}
