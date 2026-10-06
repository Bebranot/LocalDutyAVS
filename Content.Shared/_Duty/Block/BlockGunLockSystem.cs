using Content.Shared._Duty.Block.Components;
using Content.Shared.Hands;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Block;

/// <summary>
/// Штраф за блок огнестрелом: 3с нельзя стрелять и нельзя выбросить/снять/поднять/переэкипировать
/// оружие в руках — "бейся тем же, чем заблокировал". Вешается BlockSystem'ом при активации
/// полного уровня блока оружием с GunComponent, независимо от исхода блока.
/// </summary>
public sealed partial class BlockGunLockSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _netMan = default!;

    /// <summary>Не чаще раза в этот интервал — иначе строка в чат спамит при частых попытках.</summary>
    private static readonly TimeSpan NoticeDebounce = TimeSpan.FromSeconds(1);

    private const string NoticeGunLock = "duty-block-notice-gun-lock";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BlockGunLockComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<BlockGunLockComponent, DropAttemptEvent>(OnCancelAttempt);
        SubscribeLocalEvent<BlockGunLockComponent, PickupAttemptEvent>(OnCancelAttempt);
        SubscribeLocalEvent<BlockGunLockComponent, IsEquippingAttemptEvent>(OnEquipAttempt);
        SubscribeLocalEvent<BlockGunLockComponent, IsUnequippingAttemptEvent>(OnUnequipAttempt);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Снятие — серверное, по той же причине, что и у окна блока: клиент впереди сервера и
        // разрешил бы стрельбу на пару тиков раньше, чем её реально разрешил сервер.
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

        Dirty(uid, comp);
    }

    /// <summary>
    /// Ловим на стрелке: ShotAttemptedEvent ганы поднимают и на нём (см. BlockSystem.OnShotAttempted).
    /// </summary>
    private void OnShotAttempted(EntityUid uid, BlockGunLockComponent component, ref ShotAttemptedEvent args)
    {
        if (args.Cancelled)
            return;

        args.Cancel();
        TryNotify(uid, component);
    }

    private void OnCancelAttempt(EntityUid uid, BlockGunLockComponent component, CancellableEntityEventArgs args)
    {
        args.Cancel();
        TryNotify(uid, component);
    }

    private void OnEquipAttempt(EntityUid uid, BlockGunLockComponent component, IsEquippingAttemptEvent args)
    {
        if (args.Equipee != uid)
            return;

        args.Cancel();
        TryNotify(uid, component);
    }

    private void OnUnequipAttempt(EntityUid uid, BlockGunLockComponent component, IsUnequippingAttemptEvent args)
    {
        if (args.Unequipee != uid)
            return;

        args.Cancel();
        TryNotify(uid, component);
    }

    /// <summary>
    /// Серая строка "не могу прицелиться" вместо алерт-иконки — только серверу и с дебаунсом,
    /// иначе зажатая кнопка стрельбы забьёт чат.
    /// </summary>
    private void TryNotify(EntityUid uid, BlockGunLockComponent component)
    {
        if (_netMan.IsClient)
            return;

        var now = _timing.CurTime;
        if (now - component.LastNoticeTime < NoticeDebounce)
            return;

        component.LastNoticeTime = now;

        var ev = new BlockNoticeEvent(uid, NoticeGunLock);
        RaiseLocalEvent(ref ev);
    }
}
