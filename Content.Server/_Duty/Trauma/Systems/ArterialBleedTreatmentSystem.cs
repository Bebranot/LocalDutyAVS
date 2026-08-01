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
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

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
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    /// <summary>Контейнер, в котором на время лечения прячется многоразовый жгут-предмет.</summary>
    private const string TourniquetStashId = "DutyTourniquetStash";

    /// <summary>Категория ПКМ-меню «Самолечение» (когда лечишь сам себя).</summary>
    private static readonly VerbCategory SelfTreatmentCategory = new("trauma-verb-category-self-treatment", null);

    // ── Тюнинг (Phase 6). ──────────────────────────────────────────────────────
    private static readonly TimeSpan PalmPressTime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FingerPressTime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ApplyTourniquetTime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TightenTime = TimeSpan.FromSeconds(5);

    /// <summary>Сколько раз нужно «затянуть жгут» (по 5с каждое) без готового жгута в руке.</summary>
    private const int TightenRequired = 5;

    /// <summary>
    /// Сколько раз нужно «затянуть жгут», если в руке лечащего готовый жгут (тег
    /// <see cref="TourniquetTag"/>) — с ним не нужно импровизировать, достаточно одного рывка.
    /// </summary>
    private const int TightenRequiredWithTourniquet = 1;

    /// <summary>
    /// Множитель длительности этапов прижатия/наложения, если в руке лечащего готовый жгут —
    /// с профессиональным жгутом процедура идёт быстрее, чем с импровизацией из ткани.
    /// </summary>
    private const float TourniquetSpeedMultiplier = 0.5f;

    /// <summary>Множитель скорости лечащего во время лечения (0.3 = −70%).</summary>
    private const float SlowdownModifier = 0.3f;

    /// <summary>Сколько ткани нужно для наложения жгута.</summary>
    private const int RequiredClothCount = 2;

    /// <summary>Тег готового жгута (штатный Tourniquet и самодельный _Duty).</summary>
    private static readonly ProtoId<TagPrototype> TourniquetTag = "Tourniquet";

    /// <summary>Тип стака ткани.</summary>
    private static readonly ProtoId<StackPrototype> ClothStack = "Cloth";

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

    /// <summary>
    /// Сессия висит на ЛЕЧАЩЕМ, а условия её жизни — на ПАЦИЕНТЕ, то есть на ЧУЖОЙ сущности.
    /// Directed-подписок на <see cref="ArterialBleedComponent"/> для этого мало: как только компонент
    /// снят иначе, чем через <see cref="CompleteTreatment"/> (реджув, смерть/удаление пациента,
    /// админ-хил), <see cref="OnUiClosed"/> уже не вызовется — движок не доставляет directed-событие
    /// без компонента — и сессия зависает НАВСЕГДА вместе со слоудауном −70% и зумом камеры.
    /// Поэтому валидируем сессии здесь: одна проверка покрывает сразу все пути обрыва.
    /// </summary>
    public override void Update(float frameTime)
    {
        List<EntityUid>? stale = null;

        var query = EntityQueryEnumerator<ActiveArterialTreatmentComponent>();
        while (query.MoveNext(out var healer, out var session))
        {
            if (IsSessionAlive(healer, session))
                continue;

            // Удаление откладываем — нельзя менять структуру во время перебора запроса.
            (stale ??= new()).Add(healer);
        }

        if (stale is null)
            return;

        // ComponentShutdown вернёт спрятанный жгут и откатит эффекты/DoAfter.
        foreach (var healer in stale)
            RemComp<ActiveArterialTreatmentComponent>(healer);
    }

    /// <summary>Пациент ещё жив как сущность, всё ещё кровит, и окно лечения всё ещё открыто лечащим.</summary>
    private bool IsSessionAlive(EntityUid healer, ActiveArterialTreatmentComponent session)
    {
        var patient = session.Patient;

        return !TerminatingOrDeleted(patient)
               && HasComp<ArterialBleedComponent>(patient)
               && _ui.IsUiOpen(patient, ArterialTreatmentUiKey.Key, healer);
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

        // Одна активная сессия на лечащего. Второе окно надо ЗАКРЫТЬ, а не просто выйти из
        // обработчика: иначе у лечащего висит окно без состояния с мёртвыми кнопками. Закрываем
        // только чужое окно (другой пациент) — там OnUiClosed не тронет текущую сессию, т.к.
        // сверяет Patient; на своём же пациенте закрытие снесло бы живую сессию.
        if (TryComp<ActiveArterialTreatmentComponent>(healer, out var active))
        {
            if (active.Patient != ent.Owner)
            {
                _ui.CloseUi(ent.Owner, ArterialTreatmentUiKey.Key, healer);
                _popup.PopupEntity(Loc.GetString("trauma-arterial-already-treating"), healer, healer);
            }

            return;
        }

        var session = AddComp<ActiveArterialTreatmentComponent>(healer);
        session.Patient = ent.Owner;
        session.Step = ArterialTreatmentStep.PalmPress;

        PushState(ent.Owner, healer, session);
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

        var stepTime = GetStepTime(session.Step, HasRealTourniquet(healer, session));
        var doAfter = new DoAfterArgs(EntityManager, healer, stepTime, new ArterialTreatmentDoAfterEvent(session.Step), healer, ent.Owner, used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = used != null,
        };

        if (!_doAfter.TryStartDoAfter(doAfter, out var id))
            return;

        session.CurrentDoAfter = id;
        session.Busy = true;
        PushState(ent.Owner, healer, session);
    }

    private void OnDoAfter(Entity<ActiveArterialTreatmentComponent> ent, ref ArterialTreatmentDoAfterEvent args)
    {
        var session = ent.Comp;
        session.Busy = false;
        session.CurrentDoAfter = null;

        if (args.Cancelled || args.Handled)
        {
            PushState(session.Patient, ent.Owner, session);
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
                StashOrConsumeTourniquetMaterial(ent.Owner, session);
                session.TourniquetItem = null;
                session.Step = ArterialTreatmentStep.TightenTourniquet;
                break;

            case ArterialTreatmentStep.TightenTourniquet:
                session.TightenProgress++;
                if (session.TightenProgress >= GetTightenRequired(ent.Owner, session))
                {
                    CompleteTreatment(ent.Owner, session);
                    return;
                }
                break;
        }

        PushState(session.Patient, ent.Owner, session);
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

        // Возвращаем спрятанный жгут в руку, каким бы путём сессия ни прервалась (отмена/смерть/
        // дисконнект) — иначе он терялся бы в контейнере навсегда.
        ReturnStashedTourniquet(ent.Owner, ent.Comp);

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
        // Снятие сессии (ComponentShutdown вернёт спрятанный жгут и откатит эффекты/DoAfter).
        RemComp<ActiveArterialTreatmentComponent>(healer);
    }

    /// <summary>
    /// Наложение жгута состоялось: многоразовый предмет-жгут прячется из рук лечащего (вернётся
    /// по завершении/отмене лечения), а расходная ткань списывается безвозвратно, как и раньше.
    /// </summary>
    private void StashOrConsumeTourniquetMaterial(EntityUid healer, ActiveArterialTreatmentComponent session)
    {
        var item = session.TourniquetItem;
        if (item is not { } used || Deleted(used))
            return;

        if (_tag.HasTag(used, TourniquetTag))
        {
            var container = _container.EnsureContainer<ContainerSlot>(healer, TourniquetStashId);
            if (_container.Insert(used, container))
                session.StashedTourniquet = used;

            return;
        }

        ConsumeTourniquetMaterial(used);
    }

    /// <summary>Достаёт спрятанный жгут из контейнера и возвращает его в руку лечащего.</summary>
    private void ReturnStashedTourniquet(EntityUid healer, ActiveArterialTreatmentComponent session)
    {
        if (session.StashedTourniquet is not { } item)
            return;

        session.StashedTourniquet = null;

        if (Deleted(item))
            return;

        // Лечащего удаляют (гиб/дисконнект/конец раунда) — контейнер и руки уже разбираются движком,
        // лезть туда нельзя: жгут всё равно уедет вместе с сущностью, а операции над терминируемым
        // контейнером дают ошибки в лог.
        if (TerminatingOrDeleted(healer))
            return;

        if (_container.TryGetContainer(healer, TourniquetStashId, out var container))
            _container.Remove(item, container);

        _hands.TryPickupAnyHand(healer, item);
    }

    private void ApplyHealerEffects(EntityUid healer, ActiveArterialTreatmentComponent session)
    {
        session.EffectsApplied = true;
        _movementSpeed.RefreshMovementSpeedModifiers(healer);
        _contentEye.SetZoom(healer, TreatmentZoom);
    }

    private void PushState(EntityUid patient, EntityUid healer, ActiveArterialTreatmentComponent session)
    {
        _ui.SetUiState(
            patient,
            ArterialTreatmentUiKey.Key,
            new ArterialTreatmentBuiState(session.Step, session.TightenProgress, GetTightenRequired(healer, session), session.Busy));
    }

    /// <summary>
    /// Готовый жгут (тег <see cref="TourniquetTag"/>) в активной руке лечащего прямо сейчас —
    /// проверяется заново на каждом шаге, а не фиксируется один раз на всё лечение.
    /// </summary>
    private bool HasTourniquetInHand(EntityUid healer) =>
        _hands.TryGetActiveItem(healer, out var item) && item is { } used && _tag.HasTag(used, TourniquetTag);

    /// <summary>
    /// Жгут задействован в лечении — либо сейчас в руке, либо уже наложен и спрятан
    /// (<see cref="ActiveArterialTreatmentComponent.StashedTourniquet"/>, наложение прячет его из
    /// рук — см. <see cref="StashOrConsumeTourniquetMaterial"/>). Без ИЛИ проверка после наложения
    /// всегда давала бы «нет жгута», хотя он уже используется.
    /// </summary>
    private bool HasRealTourniquet(EntityUid healer, ActiveArterialTreatmentComponent session) =>
        session.StashedTourniquet is not null || HasTourniquetInHand(healer);

    /// <summary>Сколько раз нужно затянуть жгут — меньше, если в лечении задействован готовый жгут.</summary>
    private int GetTightenRequired(EntityUid healer, ActiveArterialTreatmentComponent session) =>
        HasRealTourniquet(healer, session) ? TightenRequiredWithTourniquet : TightenRequired;

    private static TimeSpan GetStepTime(ArterialTreatmentStep step, bool hasTourniquet)
    {
        var baseTime = step switch
        {
            ArterialTreatmentStep.PalmPress => PalmPressTime,
            ArterialTreatmentStep.FingerPress => FingerPressTime,
            ArterialTreatmentStep.ApplyTourniquet => ApplyTourniquetTime,
            ArterialTreatmentStep.TightenTourniquet => TightenTime,
            _ => TimeSpan.Zero,
        };

        // Затягивание не ускоряем по длительности — с жгутом сокращается количество повторов
        // (см. GetTightenRequired), а не время одного рывка.
        if (!hasTourniquet || step == ArterialTreatmentStep.TightenTourniquet)
            return baseTime;

        return baseTime * TourniquetSpeedMultiplier;
    }

    /// <summary>
    /// Есть ли в активной руке готовый жгут (по тегу — штатный или самодельный) ИЛИ стак ткани
    /// нужного размера. Возвращает конкретный предмет, чтобы зафиксировать его на весь этап.
    /// </summary>
    private bool TryGetTourniquetMaterial(EntityUid healer, out EntityUid material)
    {
        material = default;

        if (!_hands.TryGetActiveItem(healer, out var item) || item is not { } used)
            return false;

        // Готовый жгут — подойдёт как есть.
        if (_tag.HasTag(used, TourniquetTag))
        {
            material = used;
            return true;
        }

        // Иначе годится только стак ИМЕННО ткани и не меньше RequiredClothCount.
        if (!TryComp<StackComponent>(used, out var stack)
            || stack.StackTypeId != ClothStack
            || _stack.GetCount((used, stack)) < RequiredClothCount)
        {
            return false;
        }

        material = used;
        return true;
    }

    private void ConsumeTourniquetMaterial(EntityUid? item)
    {
        // Расходуется только зафиксированный на старте этапа предмет, и только если это стак ткани;
        // готовый жгут — многоразовый.
        if (item is not { } used || Deleted(used) || !TryComp<StackComponent>(used, out var stack))
            return;

        _stack.SetCount(used, _stack.GetCount((used, stack)) - RequiredClothCount, stack);
    }
}
