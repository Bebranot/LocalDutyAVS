using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.FireAgony;

/// <summary>
/// _Duty: предсказанный фидбек сцены агонии. Само блокирование ввода делает paralyze
/// (<c>StunnedComponent</c>), но молча — здесь на попытки действий показываем попап
/// «Вы в агонии!» (с анти-спам кулдауном), а также перехватываем клик ЛКМ окружающих —
/// вместо ванильного объятия (<c>InteractionPopupSystem</c>) это попытка «сбить пламя».
/// Серверная машина состояний — в <c>Content.Server._Duty.FireAgony.FireAgonySystem</c>,
/// клиентская кинематографика — в <c>Content.Client._Duty.FireAgony.FireAgonySystem</c>.
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

        // before: обязателен — ванильное объятие (InteractionPopupComponent, висит на той же базе
        // гуманоидов) подписано на тот же InteractHandEvent и предсказывается независимо на
        // клиенте; без явного порядка клиент может успеть показать "Вы обнимаете" раньше, чем мы
        // выставим Handled. Тот же приём — SharedBuckleSystem.Interaction.cs.
        SubscribeLocalEvent<FireAgonyComponent, InteractHandEvent>(OnInteractHand, before: [typeof(InteractionPopupSystem)]);
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

    /// <summary>
    /// Клик ЛКМ окружающего по горящему в агонии союзнику — вместо ванильного "Вы обнимаете"
    /// показываем красный дрожащий попап "Вы пытаетесь сбить пламя" и бродкастим попытку
    /// на цель; реальное уменьшение FireStacks делает только сервер (см. событие).
    /// </summary>
    private void OnInteractHand(EntityUid uid, FireAgonyComponent comp, InteractHandEvent args)
    {
        if (args.Handled || args.User == uid || !comp.Active)
            return;

        args.Handled = true;

        _popup.PopupPredicted(
            Loc.GetString("fire-agony-help-extinguish-attempt", ("target", Identity.Entity(uid, EntityManager))),
            Loc.GetString("fire-agony-help-extinguish-attempt-others",
                ("user", Identity.Entity(args.User, EntityManager)),
                ("target", Identity.Entity(uid, EntityManager))),
            uid, args.User, PopupType.DutyHealthScream);

        var ev = new FireAgonyHelpExtinguishAttemptEvent(args.User);
        RaiseLocalEvent(uid, ref ev);
    }
}
