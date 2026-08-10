using Content.Shared._Duty.Block.Components;
using Content.Shared.Alert;
using Content.Shared.Hands;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Block;

/// <summary>
/// Штраф за блок огнестрелом: 3с нельзя стрелять и нельзя выбросить/снять/поднять/переэкипировать
/// оружие в руках — "бейся тем же, чем заблокировал". Вешается BlockSystem'ом при активации
/// полного уровня блока оружием с GunComponent, независимо от исхода блока.
/// </summary>
public sealed class BlockGunLockSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _netMan = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    /// <summary>Не чаще раза в этот интервал — иначе pop-up спамит при частой стрельбе/попытках.</summary>
    private static readonly TimeSpan PopupDebounce = TimeSpan.FromSeconds(0.6);

    private static readonly ProtoId<AlertPrototype> GunLockAlert = "DutyBlockGunLock";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AttemptShootEvent>(OnShootAttempt);
        SubscribeLocalEvent<BlockGunLockComponent, DropAttemptEvent>(OnCancelPopup);
        SubscribeLocalEvent<BlockGunLockComponent, PickupAttemptEvent>(OnCancelPopup);
        SubscribeLocalEvent<BlockGunLockComponent, IsEquippingAttemptEvent>(OnEquipAttempt);
        SubscribeLocalEvent<BlockGunLockComponent, IsUnequippingAttemptEvent>(OnUnequipAttempt);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Снятие по таймеру — серверное решение, клиент реагирует на реплицированное удаление.
        if (_netMan.IsClient)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<BlockGunLockComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now >= comp.EndTime)
                RemCompDeferred<BlockGunLockComponent>(uid);
        }
    }

    /// <summary>Применяет (или продлевает) лок. Вызывается только сервером из BlockSystem.</summary>
    public void ApplyGunLock(EntityUid uid, TimeSpan duration)
    {
        var comp = EnsureComp<BlockGunLockComponent>(uid);
        var endTime = _timing.CurTime + duration;

        if (endTime > comp.EndTime)
            comp.EndTime = endTime;

        _alerts.ShowAlert(uid, GunLockAlert, cooldown: (_timing.CurTime, comp.EndTime), autoRemove: true);
    }

    /// <summary>
    /// AttemptShootEvent летит directed на само оружие, поэтому подписка широковещательная —
    /// фильтруем по <see cref="AttemptShootEvent.User"/> вручную.
    /// </summary>
    private void OnShootAttempt(ref AttemptShootEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryComp<BlockGunLockComponent>(args.User, out var comp))
            return;

        args.Cancelled = true;
        TryShowPopup(args.User, comp);
    }

    private void OnCancelPopup(EntityUid uid, BlockGunLockComponent component, CancellableEntityEventArgs args)
    {
        args.Cancel();
        TryShowPopup(uid, component);
    }

    private void OnEquipAttempt(EntityUid uid, BlockGunLockComponent component, IsEquippingAttemptEvent args)
    {
        if (args.Equipee != uid)
            return;

        args.Cancel();
        TryShowPopup(uid, component);
    }

    private void OnUnequipAttempt(EntityUid uid, BlockGunLockComponent component, IsUnequippingAttemptEvent args)
    {
        if (args.Unequipee != uid)
            return;

        args.Cancel();
        TryShowPopup(uid, component);
    }

    private void TryShowPopup(EntityUid uid, BlockGunLockComponent component)
    {
        var now = _timing.CurTime;
        if (now - component.LastPopupTime < PopupDebounce)
            return;

        component.LastPopupTime = now;
        _popup.PopupPredicted(Loc.GetString("duty-block-gun-lock-popup"), uid, uid);
    }
}
