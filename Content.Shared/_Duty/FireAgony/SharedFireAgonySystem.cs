using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.FireAgony;

/// <summary>
/// _Duty: предсказанный фидбек сцены агонии. Само блокирование ввода делает paralyze
/// (<c>StunnedComponent</c>), но молча — здесь на попытки действий показываем попап
/// «Вы в агонии!» (с анти-спам кулдауном). Серверная машина состояний — в
/// <c>Content.Server._Duty.FireAgony.FireAgonySystem</c>, клиентская кинематографика —
/// в <c>Content.Client._Duty.FireAgony.FireAgonySystem</c>.
/// </summary>
public sealed class SharedFireAgonySystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly TimeSpan BlockedPopupCooldown = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FireAgonyComponent, InteractionAttemptEvent>(OnInteractAttempt);
        SubscribeLocalEvent<FireAgonyComponent, UseAttemptEvent>(OnUseAttempt);
    }

    private void OnInteractAttempt(EntityUid uid, FireAgonyComponent comp, ref InteractionAttemptEvent args)
    {
        if (!comp.Active)
            return;

        args.Cancelled = true;
        ShowBlockedPopup(uid, comp);
    }

    private void OnUseAttempt(EntityUid uid, FireAgonyComponent comp, UseAttemptEvent args)
    {
        if (!comp.Active)
            return;

        args.Cancel();
        ShowBlockedPopup(uid, comp);
    }

    private void ShowBlockedPopup(EntityUid uid, FireAgonyComponent comp)
    {
        if (_timing.CurTime < comp.NextBlockedPopup)
            return;

        comp.NextBlockedPopup = _timing.CurTime + BlockedPopupCooldown;
        _popup.PopupClient(Loc.GetString("fire-agony-popup-blocked"), uid, uid, PopupType.LargeCaution);
    }
}
