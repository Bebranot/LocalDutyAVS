// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.Events;
using Content.Shared._Duty.Trauma.UI;
using Content.Shared.Body.Components;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Medical.Healing;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Trauma.Systems;

/// <summary>
/// _Duty: быстрая остановка артериального кровотечения фабричным жгутом на СЕБЕ.
///
/// Перехватывает обычное применение жгута (<see cref="DutyHealInterceptEvent"/> из ванильного
/// <c>HealingSystem.TryHeal</c> — он покрывает сразу оба входа: жгут в руке и клик по себе жгутом):
/// <list type="bullet">
/// <item>кровит только артерия — сразу DoAfter, жгут расходуется, кровотечение снимается;</item>
/// <item>кровит и артерия, и рана сверх неё — радиальное меню, что именно перетягивать;</item>
/// <item>артерии нет — не вмешиваемся, ваниль работает как раньше.</item>
/// </list>
///
/// Самодельный жгут (<see cref="MakeshiftTourniquetComponent"/>) сюда не попадает — им артерию
/// по-прежнему останавливают только пошаговым окном (<c>ArterialBleedTreatmentSystem</c>).
///
/// Система шаренная не ради предсказания эффекта, а ради предсказания САМОГО перехвата: решение
/// «ваниль тут не работает» должно совпасть на клиенте и на сервере, иначе клиент нарисует
/// ванильный DoAfter и звук, которых сервер не запускал. Всё, что меняет состояние, идёт только на
/// сервере.
/// </summary>
public sealed class TourniquetTreatmentSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly HealingSystem _healing = default!;

    /// <summary>Длительность быстрой остановки (Phase 6). Рвётся движением и уроном.</summary>
    private static readonly TimeSpan QuickTreatmentTime = TimeSpan.FromSeconds(3);

    /// <summary>Тег жгута — штатного и самодельного, самоделка отсеивается отдельным маркером.</summary>
    private static readonly ProtoId<TagPrototype> TourniquetTag = "Tourniquet";

    public override void Initialize()
    {
        SubscribeLocalEvent<ArterialBleedComponent, DutyHealInterceptEvent>(OnHealIntercept);
        SubscribeLocalEvent<ArterialBleedComponent, TourniquetChoiceMessage>(OnChoice);
        SubscribeLocalEvent<ArterialBleedComponent, ArterialTourniquetDoAfterEvent>(OnQuickDoAfter);
    }

    private void OnHealIntercept(Entity<ArterialBleedComponent> ent, ref DutyHealInterceptEvent args)
    {
        if (args.Handled)
            return;

        // Только на себе и только фабричным жгутом.
        if (args.User != ent.Owner || !IsQuickTourniquet(args.Item))
            return;

        // Ветку забираем на обеих сторонах — см. комментарий к классу.
        args.Handled = true;

        if (!_net.IsServer)
            return;

        // Кровит сверх артериального фона — пусть игрок сам решит, что перетягивать. Если у вида
        // нет этого окна, молча ничего не делать нельзя — лечим артерию сразу.
        if (HasPlainBleeding(ent) && _ui.TryOpenUi(ent.Owner, TourniquetChoiceUiKey.Key, ent.Owner))
            return;

        StartQuickTreatment(ent.Owner, args.Item);
    }

    private void OnChoice(Entity<ArterialBleedComponent> ent, ref TourniquetChoiceMessage args)
    {
        var user = args.Actor;

        // Окно открывается только пациентом на себе — чужой актор сюда попасть не должен.
        if (user != ent.Owner)
            return;

        _ui.CloseUi(ent.Owner, TourniquetChoiceUiKey.Key, user);

        // Предмет не запоминаем на время меню, а берём заново: пока окно висело, жгут могли
        // выронить, убрать или сменить руку.
        if (!_hands.TryGetActiveItem(user, out var item)
            || item is not { } tourniquet
            || !IsQuickTourniquet(tourniquet))
        {
            _popup.PopupEntity(Loc.GetString("trauma-tourniquet-gone"), user, user);
            return;
        }

        switch (args.Choice)
        {
            case TourniquetChoice.Artery:
                StartQuickTreatment(ent.Owner, tourniquet);
                break;

            case TourniquetChoice.PlainBleeding:
                // Ванильное применение жгута — но уже без перехвата, иначе меню откроется снова.
                if (TryComp<HealingComponent>(tourniquet, out var healing))
                    _healing.TryHeal((tourniquet, healing), user, user, allowIntercept: false);
                break;
        }
    }

    private void OnQuickDoAfter(Entity<ArterialBleedComponent> ent, ref ArterialTourniquetDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;

        // ComponentShutdown артерии сам гасит кровотечение и снимает алерт.
        RemComp<ArterialBleedComponent>(ent);
        _popup.PopupEntity(Loc.GetString("trauma-arterial-treated"), ent.Owner, ent.Owner);

        if (args.Used is not { } tourniquet || Deleted(tourniquet))
            return;

        if (TryComp<HealingComponent>(tourniquet, out var healing))
            _audio.PlayPvs(healing.HealingEndSound, ent.Owner);

        // Наложенный жгут расходуется — ровно как при обычном ванильном применении.
        PredictedQueueDel(tourniquet);
    }

    private void StartQuickTreatment(EntityUid patient, EntityUid tourniquet)
    {
        var doAfter = new DoAfterArgs(EntityManager, patient, QuickTreatmentTime, new ArterialTourniquetDoAfterEvent(), patient, patient, tourniquet)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        if (TryComp<HealingComponent>(tourniquet, out var healing))
            _audio.PlayPvs(healing.HealingBeginSound, tourniquet);
    }

    /// <summary>
    /// Кровит ли сверх артериального фона. Артерия сама держит <c>BleedAmount</c> на
    /// <see cref="ArterialBleedComponent.BleedTarget"/> и мгновенно перекрывает всё, что ниже, —
    /// поэтому «обычным» имеет смысл считать только кровотечение выше этого порога: перетягивать
    /// то, что артерия и так восстановит следующим тиком, игроку нечего.
    /// </summary>
    private bool HasPlainBleeding(Entity<ArterialBleedComponent> ent) =>
        TryComp<BloodstreamComponent>(ent, out var blood) && blood.BleedAmount > ent.Comp.BleedTarget;

    /// <summary>Фабричный жгут — тег жгута есть, маркера самоделки нет.</summary>
    private bool IsQuickTourniquet(EntityUid item) =>
        _tag.HasTag(item, TourniquetTag) && !HasComp<MakeshiftTourniquetComponent>(item);
}
