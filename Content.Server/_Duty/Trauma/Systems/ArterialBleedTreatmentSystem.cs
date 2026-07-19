// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.UI;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Verbs;

namespace Content.Server._Duty.Trauma.Systems;

/// <summary>
/// _Duty: пошаговое лечение артериального кровотечения через кастомное BUI-окно.
/// Окно открывается на пациенте, лечащим может быть сам пациент или другой игрок.
/// Этапы строго последовательны: прижать ладонью → пальцем → наложить жгут → затянуть (несколько
/// раз). На первом этапе лечащий замедляется и его камера приближается — до конца лечения или до
/// закрытия окна (закрытие = отмена). Наложение жгута требует 2 ткани ИЛИ жгут в активной руке.
///
/// Эффекты лечащего (слоудаун + зум) и незавершённый DoAfter снимаются в ComponentShutdown
/// <see cref="ActiveArterialTreatmentComponent"/>, поэтому любой путь удаления сессии
/// (завершение, закрытие окна, смерть/дисконнект лечащего) откатывает состояние корректно.
/// </summary>
public sealed class ArterialBleedTreatmentSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly SharedContentEyeSystem _contentEye = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    /// <summary>Категория ПКМ-меню «Самолечение» (когда лечишь сам себя).</summary>
    private static readonly VerbCategory SelfTreatmentCategory = new("trauma-verb-category-self-treatment", null);

    // ── Тюнинг (Phase 6). ──────────────────────────────────────────────────────
    private static readonly TimeSpan PalmPressTime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FingerPressTime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ApplyTourniquetTime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TightenTime = TimeSpan.FromSeconds(5);

    /// <summary>Сколько раз нужно «затянуть жгут» (по 5с каждое).</summary>
    private const int TightenRequired = 5;

    /// <summary>Множитель скорости лечащего во время лечения (0.3 = −70%).</summary>
    private const float SlowdownModifier = 0.3f;

    /// <summary>Сколько ткани нужно для наложения жгута.</summary>
    private const int RequiredClothCount = 2;

    /// <summary>Приближение камеры лечащего (меньше 1 = зум in).</summary>
    private static readonly System.Numerics.Vector2 TreatmentZoom = new(0.75f, 0.75f);

    public override void Initialize()
    {
        SubscribeLocalEvent<ArterialBleedComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<ArterialBleedComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<ArterialBleedComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<ArterialBleedComponent, ArterialTreatmentStepMessage>(OnStep);

        SubscribeLocalEvent<ActiveArterialTreatmentComponent, ArterialTreatmentDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<ActiveArterialTreatmentComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<ActiveArterialTreatmentComponent, ComponentShutdown>(OnSessionShutdown);
    }

    private void OnGetVerbs(Entity<ArterialBleedComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var user = args.User;
        var patient = ent.Owner;
        var isSelf = user == patient;

        var verb = new Verb
        {
            Text = Loc.GetString("trauma-verb-treat-arterial"),
            // Самолечение — под своей вкладкой; лечение другого — обычным пунктом.
            Category = isSelf ? SelfTreatmentCategory : null,
            Act = () => _ui.OpenUi(patient, ArterialTreatmentUiKey.Key, user),
        };

        args.Verbs.Add(verb);
    }

    private void OnUiOpened(Entity<ArterialBleedComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is not ArterialTreatmentUiKey)
            return;

        var healer = args.Actor;

        // Одна активная сессия на лечащего.
        if (HasComp<ActiveArterialTreatmentComponent>(healer))
            return;

        var session = AddComp<ActiveArterialTreatmentComponent>(healer);
        session.Patient = ent.Owner;
        session.Step = ArterialTreatmentStep.PalmPress;

        PushState(ent.Owner, session);
    }

    private void OnUiClosed(Entity<ArterialBleedComponent> ent, ref BoundUIClosedEvent args)
    {
        if (args.UiKey is not ArterialTreatmentUiKey)
            return;

        // Закрытие окна = отмена лечения. Прогресс сбрасывается; эффекты и DoAfter снимутся в
        // ComponentShutdown сессии.
        if (TryComp<ActiveArterialTreatmentComponent>(args.Actor, out var session) && session.Patient == ent.Owner)
            RemComp<ActiveArterialTreatmentComponent>(args.Actor);
    }

    private void OnStep(Entity<ArterialBleedComponent> ent, ref ArterialTreatmentStepMessage args)
    {
        var healer = args.Actor;

        if (!TryComp<ActiveArterialTreatmentComponent>(healer, out var session) || session.Patient != ent.Owner)
            return;

        // Строго по порядку и не параллельно.
        if (session.Busy || args.Step != session.Step)
            return;

        // Наложение жгута требует материал в руке — фиксируем конкретный предмет на старте этапа.
        EntityUid? used = null;
        if (session.Step == ArterialTreatmentStep.ApplyTourniquet)
        {
            if (!TryGetTourniquetMaterial(healer, out var material))
            {
                _popup.PopupEntity(Loc.GetString("trauma-arterial-need-material"), healer, healer);
                return;
            }

            session.TourniquetItem = material;
            used = material;
        }

        // Эффекты лечащего включаются с первого этапа и держатся до конца/отмены.
        if (session.Step == ArterialTreatmentStep.PalmPress && !session.EffectsApplied)
            ApplyHealerEffects(healer, session);

        var doAfter = new DoAfterArgs(EntityManager, healer, GetStepTime(session.Step), new ArterialTreatmentDoAfterEvent(session.Step), healer, ent.Owner, used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = used != null,
        };

        if (!_doAfter.TryStartDoAfter(doAfter, out var id))
            return;

        session.CurrentDoAfter = id;
        session.Busy = true;
        PushState(ent.Owner, session);
    }

    private void OnDoAfter(Entity<ActiveArterialTreatmentComponent> ent, ref ArterialTreatmentDoAfterEvent args)
    {
        var session = ent.Comp;
        session.Busy = false;
        session.CurrentDoAfter = null;

        if (args.Cancelled || args.Handled)
        {
            PushState(session.Patient, session);
            return;
        }

        args.Handled = true;

        switch (args.Step)
        {
            case ArterialTreatmentStep.PalmPress:
                session.Step = ArterialTreatmentStep.FingerPress;
                break;

            case ArterialTreatmentStep.FingerPress:
                session.Step = ArterialTreatmentStep.ApplyTourniquet;
                break;

            case ArterialTreatmentStep.ApplyTourniquet:
                ConsumeTourniquetMaterial(session.TourniquetItem);
                session.TourniquetItem = null;
                session.Step = ArterialTreatmentStep.TightenTourniquet;
                break;

            case ArterialTreatmentStep.TightenTourniquet:
                session.TightenProgress++;
                if (session.TightenProgress >= TightenRequired)
                {
                    CompleteTreatment(ent.Owner, session);
                    return;
                }
                break;
        }

        PushState(session.Patient, session);
    }

    private void OnRefreshSpeed(EntityUid uid, ActiveArterialTreatmentComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        if (comp.EffectsApplied)
            args.ModifySpeed(SlowdownModifier);
    }

    private void OnSessionShutdown(Entity<ActiveArterialTreatmentComponent> ent, ref ComponentShutdown args)
    {
        // Отменяем незавершённый DoAfter.
        if (ent.Comp.CurrentDoAfter is { } doAfter)
            _doAfter.Cancel(doAfter);

        if (!ent.Comp.EffectsApplied)
            return;

        // Снимаем слоудаун и зум, каким бы путём сессия ни удалялась.
        ent.Comp.EffectsApplied = false;
        _movementSpeed.RefreshMovementSpeedModifiers(ent.Owner);
        _contentEye.SetZoom(ent.Owner, SharedContentEyeSystem.DefaultZoom);
    }

    private void CompleteTreatment(EntityUid healer, ActiveArterialTreatmentComponent session)
    {
        var patient = session.Patient;

        RemComp<ArterialBleedComponent>(patient);
        _popup.PopupEntity(Loc.GetString("trauma-arterial-treated"), patient, healer);

        _ui.CloseUi(patient, ArterialTreatmentUiKey.Key, healer);
        // Снятие сессии (ComponentShutdown откатит эффекты и отменит DoAfter).
        RemComp<ActiveArterialTreatmentComponent>(healer);
    }

    private void ApplyHealerEffects(EntityUid healer, ActiveArterialTreatmentComponent session)
    {
        session.EffectsApplied = true;
        _movementSpeed.RefreshMovementSpeedModifiers(healer);
        _contentEye.SetZoom(healer, TreatmentZoom);
    }

    private void PushState(EntityUid patient, ActiveArterialTreatmentComponent session)
    {
        _ui.SetUiState(
            patient,
            ArterialTreatmentUiKey.Key,
            new ArterialTreatmentBuiState(session.Step, session.TightenProgress, TightenRequired, session.Busy));
    }

    private static TimeSpan GetStepTime(ArterialTreatmentStep step) => step switch
    {
        ArterialTreatmentStep.PalmPress => PalmPressTime,
        ArterialTreatmentStep.FingerPress => FingerPressTime,
        ArterialTreatmentStep.ApplyTourniquet => ApplyTourniquetTime,
        ArterialTreatmentStep.TightenTourniquet => TightenTime,
        _ => TimeSpan.Zero,
    };

    /// <summary>
    /// Есть ли в активной руке жгут (любой цельный предмет) ИЛИ стак ткани нужного размера.
    /// Возвращает конкретный предмет, чтобы зафиксировать его на весь этап.
    /// </summary>
    private bool TryGetTourniquetMaterial(EntityUid healer, out EntityUid material)
    {
        material = default;

        if (!_hands.TryGetActiveItem(healer, out var item) || item is not { } used)
            return false;

        // Стак (ткань) — нужно не меньше RequiredClothCount; иначе это цельный предмет (жгут).
        if (TryComp<StackComponent>(used, out var stack) && _stack.GetCount((used, stack)) < RequiredClothCount)
            return false;

        material = used;
        return true;
    }

    private void ConsumeTourniquetMaterial(EntityUid? item)
    {
        // Расходуется только зафиксированный на старте этапа предмет, и только если это стак ткани;
        // цельный жгут — многоразовый.
        if (item is not { } used || Deleted(used) || !TryComp<StackComponent>(used, out var stack))
            return;

        _stack.SetCount(used, _stack.GetCount((used, stack)) - RequiredClothCount, stack);
    }
}
