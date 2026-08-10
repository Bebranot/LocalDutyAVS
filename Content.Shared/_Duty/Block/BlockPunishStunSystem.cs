using Content.Shared._Duty.Block.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Alert;
using Content.Shared.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Movement.Events;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Block;

/// <summary>
/// Наказание атакующего, ударившего в полный уровень блока: 1с полный лок движения, атаки,
/// стрельбы, взаимодействия, использования, броска, поднятия и экип/разэкип. Сознательно СВОЙ
/// компонент вместо ванильного StunnedComponent — набор подписок скопирован по составу с
/// SharedStunSystem, но независимо, чтобы не зависеть от чужих систем, завязанных на Stunned.
/// </summary>
public sealed class BlockPunishStunSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _netMan = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    /// <summary>Не чаще раза в этот интервал — иначе pop-up спамит на зажатой клавише.</summary>
    private static readonly TimeSpan PopupDebounce = TimeSpan.FromSeconds(0.6);

    private static readonly ProtoId<AlertPrototype> PunishStunAlert = "DutyBlockPunishStun";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BlockPunishStunComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<BlockPunishStunComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<BlockPunishStunComponent, ChangeDirectionAttemptEvent>(OnCancelPopup);
        SubscribeLocalEvent<BlockPunishStunComponent, UpdateCanMoveEvent>(OnCancelPopup);
        SubscribeLocalEvent<BlockPunishStunComponent, InteractionAttemptEvent>(OnInteractionAttempt);
        SubscribeLocalEvent<BlockPunishStunComponent, UseAttemptEvent>(OnCancelPopup);
        SubscribeLocalEvent<BlockPunishStunComponent, ThrowAttemptEvent>(OnCancelPopup);
        SubscribeLocalEvent<BlockPunishStunComponent, DropAttemptEvent>(OnCancelPopup);
        SubscribeLocalEvent<BlockPunishStunComponent, AttackAttemptEvent>(OnCancelPopup);
        SubscribeLocalEvent<BlockPunishStunComponent, PickupAttemptEvent>(OnCancelPopup);
        SubscribeLocalEvent<BlockPunishStunComponent, IsEquippingAttemptEvent>(OnEquipAttempt);
        SubscribeLocalEvent<BlockPunishStunComponent, IsUnequippingAttemptEvent>(OnUnequipAttempt);
        SubscribeLocalEvent<AttemptShootEvent>(OnShootAttempt);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Снятие по таймеру — серверное решение, клиент реагирует на реплицированное удаление.
        if (_netMan.IsClient)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<BlockPunishStunComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now >= comp.EndTime)
                RemCompDeferred<BlockPunishStunComponent>(uid);
        }
    }

    /// <summary>Применяет (или продлевает) наказание. Вызывается только сервером из BlockSystem.</summary>
    public void ApplyPunishStun(EntityUid uid, TimeSpan duration)
    {
        var comp = EnsureComp<BlockPunishStunComponent>(uid);
        var endTime = _timing.CurTime + duration;

        if (endTime > comp.EndTime)
            comp.EndTime = endTime;

        _alerts.ShowAlert(uid, PunishStunAlert, cooldown: (_timing.CurTime, comp.EndTime), autoRemove: true);
    }

    private void OnStartup(Entity<BlockPunishStunComponent> ent, ref ComponentStartup args)
    {
        _actionBlocker.UpdateCanMove(ent);
    }

    private void OnShutdown(Entity<BlockPunishStunComponent> ent, ref ComponentShutdown args)
    {
        _actionBlocker.UpdateCanMove(ent);
    }

    private void OnCancelPopup(EntityUid uid, BlockPunishStunComponent component, CancellableEntityEventArgs args)
    {
        args.Cancel();
        TryShowPopup(uid, component);
    }

    private void OnInteractionAttempt(Entity<BlockPunishStunComponent> ent, ref InteractionAttemptEvent args)
    {
        args.Cancelled = true;
        TryShowPopup(ent, ent.Comp);
    }

    private void OnEquipAttempt(EntityUid uid, BlockPunishStunComponent component, IsEquippingAttemptEvent args)
    {
        if (args.Equipee != uid)
            return;

        args.Cancel();
        TryShowPopup(uid, component);
    }

    private void OnUnequipAttempt(EntityUid uid, BlockPunishStunComponent component, IsUnequippingAttemptEvent args)
    {
        if (args.Unequipee != uid)
            return;

        args.Cancel();
        TryShowPopup(uid, component);
    }

    /// <summary>
    /// AttemptShootEvent летит directed на само оружие, поэтому подписка широковещательная —
    /// фильтруем по <see cref="AttemptShootEvent.User"/> вручную.
    /// </summary>
    private void OnShootAttempt(ref AttemptShootEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryComp<BlockPunishStunComponent>(args.User, out var comp))
            return;

        args.Cancelled = true;
        TryShowPopup(args.User, comp);
    }

    private void TryShowPopup(EntityUid uid, BlockPunishStunComponent component)
    {
        var now = _timing.CurTime;
        if (now - component.LastPopupTime < PopupDebounce)
            return;

        component.LastPopupTime = now;
        _popup.PopupPredicted(Loc.GetString("duty-block-punish-stun-popup"), uid, uid);
    }
}
