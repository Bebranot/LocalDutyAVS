// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Server._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.UI;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Server._Duty.Trauma.Systems;

/// <summary>
/// _Duty: пошаговое лечение артериального кровотечения через кастомное BUI-окно.
/// Окно открывается на пациенте, лечащим может быть сам пациент или другой игрок.
/// Этапы строго последовательны: прижать ладонью → пальцем → наложить жгут → затянуть (несколько
/// раз). На первом этапе лечащий замедляется и его камера приближается — до конца лечения или до
/// закрытия окна (закрытие = отмена). Наложение жгута требует 2 ткани ИЛИ жгут в активной руке.
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
    private static readonly Vector2 TreatmentZoom = new(0.75f, 0.75f);

    public override void Initialize()
    {
        SubscribeLocalEvent<ArterialBleedComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<ArterialBleedComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<ArterialBleedComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<ArterialBleedComponent, ArterialTreatmentStepMessage>(OnStep);

        SubscribeLocalEvent<ActiveArterialTreatmentComponent, ArterialTreatmentDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<ActiveArterialTreatmentComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
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

        // Закрытие окна = отмена лечения. Прогресс сбрасывается, эффекты лечащего снимаются.
        if (TryComp<ActiveArterialTreatmentComponent>(args.Actor, out var session) && session.Patient == ent.Owner)
            EndSession(args.Actor, session);
    }

    private void OnStep(Entity<ArterialBleedComponent> ent, ref ArterialTreatmentStepMessage args)
    {
        var healer = args.Actor;

        if (!TryComp<ActiveArterialTreatmentComponent>(healer, out var session) || session.Patient != ent.Owner)
            return;

        // Строго по порядку и не параллельно.
        if (session.Busy || args.Step != session.Step)
            return;

        // Наложение жгута требует материал в руке.
        if (session.Step == ArterialTreatmentStep.ApplyTourniquet && !HasTourniquetMaterial(healer))
        {
            _popup.PopupEntity(Loc.GetString("trauma-arterial-need-material"), healer, healer);
            return;
        }

        // Эффекты лечащего включаются с первого этапа и держатся до конца/отмены.
        if (session.Step == ArterialTreatmentStep.PalmPress && !session.EffectsApplied)
            ApplyHealerEffects(healer, session);

        var delay = GetStepTime(session.Step);
        var doAfter = new DoAfterArgs(EntityManager, healer, delay, new ArterialTreatmentDoAfterEvent(session.Step), healer, ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = session.Step == ArterialTreatmentStep.ApplyTourniquet,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        session.Busy = true;
        PushState(ent.Owner, session);
    }

    private void OnDoAfter(Entity<ActiveArterialTreatmentComponent> ent, ref ArterialTreatmentDoAfterEvent args)
    {
        var session = ent.Comp;
        session.Busy = false;

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
                ConsumeTourniquetMaterial(ent.Owner);
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

    private void CompleteTreatment(EntityUid healer, ActiveArterialTreatmentComponent session)
    {
        var patient = session.Patient;

        RemComp<ArterialBleedComponent>(patient);
        _popup.PopupEntity(Loc.GetString("trauma-arterial-treated"), patient, healer);

        _ui.CloseUi(patient, ArterialTreatmentUiKey.Key, healer);
        // EndSession дополнительно вызовется из OnUiClosed, но он идемпотентен.
        EndSession(healer, session);
    }

    private void EndSession(EntityUid healer, ActiveArterialTreatmentComponent session)
    {
        if (session.EffectsApplied)
            _contentEye.SetZoom(healer, SharedContentEyeSystem.DefaultZoom);

        RemComp<ActiveArterialTreatmentComponent>(healer);
        // Пересчёт скорости уже без нашего обработчика — слоудаун снят.
        _movementSpeed.RefreshMovementSpeedModifiers(healer);
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

    /// <summary>Есть ли в активной руке жгут (любой предмет) ИЛИ стак ткани нужного размера.</summary>
    private bool HasTourniquetMaterial(EntityUid healer)
    {
        if (!_hands.TryGetActiveItem(healer, out var item) || item is not { } used)
            return false;

        // Стак (ткань) — нужно не меньше RequiredClothCount; иначе это цельный предмет (жгут).
        if (TryComp<StackComponent>(used, out var stack))
            return _stack.GetCount((used, stack)) >= RequiredClothCount;

        return true;
    }

    private void ConsumeTourniquetMaterial(EntityUid healer)
    {
        if (!_hands.TryGetActiveItem(healer, out var item) || item is not { } used)
            return;

        // Ткань расходуется (2 штуки); цельный жгут — многоразовый, не тратится.
        if (TryComp<StackComponent>(used, out var stack))
            _stack.SetCount(used, _stack.GetCount((used, stack)) - RequiredClothCount, stack);
    }
}
