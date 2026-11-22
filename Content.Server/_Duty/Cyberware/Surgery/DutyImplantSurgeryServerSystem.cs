// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Chat.Systems;
using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Surgery;
using Content.Shared._Duty.Trauma.Events;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Duty.Cyberware.Surgery;

/// <summary>
/// _Duty: операции с имплантами через <see cref="DutyImplantSurgeryKitComponent"/>.
/// Один этап — один doafter; следующий стартует из обработчика предыдущего.
/// Если операцию бросили после разреза — кровотечение остаётся (РП-цена).
/// </summary>
public sealed partial class DutyImplantSurgeryServerSystem : EntitySystem
{
    [Dependency] private DutyImplantSurgeryKitSystem _kitSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedSubdermalImplantSystem _implants = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private TagSystem _tag = default!;

    private static readonly ProtoId<TagPrototype> SurgeryToolTag = "SurgeryTool";

    private EntityQuery<DutyCyberwareComponent> _cyberQuery;
    private EntityQuery<SubdermalImplantComponent> _implantQuery;

    public override void Initialize()
    {
        base.Initialize();

        _cyberQuery = GetEntityQuery<DutyCyberwareComponent>();
        _implantQuery = GetEntityQuery<SubdermalImplantComponent>();

        SubscribeLocalEvent<DutyImplantSurgeryKitComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<DutyImplantSurgeryKitComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<DutyImplantSurgeryKitComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<DutyImplantSurgeryKitComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<DutyImplantSurgeryKitComponent, GetVerbsEvent<UtilityVerb>>(OnVerbs);
        SubscribeLocalEvent<DutyImplantSurgeryKitComponent, DutyImplantSurgeryDoAfterEvent>(OnDoAfter);
    }

    #region Слот и визуал

    private void OnMapInit(Entity<DutyImplantSurgeryKitComponent> ent, ref MapInitEvent args)
    {
        UpdateVisual(ent);

        if (ent.Comp.Implant is not { } proto)
            return;

        if (!_container.TryGetContainer(ent, DutyImplantSurgeryKitComponent.ContainerId, out _))
        {
            Log.Error($"{ToPrettyString(ent)}: нет контейнера {DutyImplantSurgeryKitComponent.ContainerId}, имплант {proto} не заряжен.");
            return;
        }

        if (!TrySpawnInContainer(proto, ent, DutyImplantSurgeryKitComponent.ContainerId, out _))
            Log.Error($"{ToPrettyString(ent)}: не удалось вставить {proto} в слот набора.");
    }

    private void OnInserted(Entity<DutyImplantSurgeryKitComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == DutyImplantSurgeryKitComponent.ContainerId)
            UpdateVisual(ent, true);
    }

    private void OnRemoved(Entity<DutyImplantSurgeryKitComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == DutyImplantSurgeryKitComponent.ContainerId)
            UpdateVisual(ent, false);
    }

    private void UpdateVisual(Entity<DutyImplantSurgeryKitComponent> ent, bool? loaded = null)
    {
        loaded ??= _kitSystem.TryGetLoadedImplant(ent, out _);
        _appearance.SetData(ent, DutyImplantSurgeryKitVisuals.Loaded, loaded.Value);
    }

    #endregion

    #region Запуск операции

    private void OnAfterInteract(Entity<DutyImplantSurgeryKitComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        // Не мобов (стол, шкаф) молча пропускаем — пусть работает обычное взаимодействие.
        if (!HasComp<MobStateComponent>(target))
            return;

        args.Handled = true;

        if (_kitSystem.TryGetLoadedImplant(ent, out var loaded))
        {
            TryStartInsert(ent, args.User, target, loaded.Value);
            return;
        }

        // Пустой набор: по умолчанию достаём последний кибер-имплант.
        EntityUid? last = null;
        foreach (var implant in GetRemovable(target))
        {
            last = implant;
        }

        if (last == null)
        {
            _popup.PopupEntity(Loc.GetString("duty-surgery-fail-nothing"), target, args.User);
            return;
        }

        TryStartRemove(ent, args.User, target, last.Value);
    }

    private void OnVerbs(Entity<DutyImplantSurgeryKitComponent> ent, ref GetVerbsEvent<UtilityVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        if (_kitSystem.TryGetLoadedImplant(ent, out _))
            return;

        var target = args.Target;
        if (!HasComp<MobStateComponent>(target))
            return;

        var user = args.User;
        var index = 0;
        foreach (var implant in GetRemovable(target))
        {
            index++;
            var picked = implant;
            var concealed = _cyberQuery.TryComp(picked, out var cyber) && cyber.Concealed;
            var name = concealed
                ? Loc.GetString("duty-surgery-kit-unidentified") + " #" + index
                : MetaData(picked).EntityName;

            args.Verbs.Add(new UtilityVerb
            {
                Text = Loc.GetString("duty-surgery-verb-remove", ("implant", name)),
                Priority = -index,
                Act = () => TryStartRemove(ent, user, target, picked)
            });
        }
    }

    /// <summary>Кибер-импланты цели, которые можно достать (ванильные и вечные не трогаем).</summary>
    private IEnumerable<EntityUid> GetRemovable(EntityUid target)
    {
        if (!TryComp<ImplantedComponent>(target, out var implanted))
            yield break;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (!_cyberQuery.HasComp(implant))
                continue;

            if (_implantQuery.TryComp(implant, out var sub) && sub.Permanent)
                continue;

            yield return implant;
        }
    }

    private bool CanOperate(Entity<DutyImplantSurgeryKitComponent> kit, EntityUid user, EntityUid target)
    {
        if (user == target && !kit.Comp.AllowSelf)
        {
            _popup.PopupEntity(Loc.GetString("duty-surgery-fail-self"), target, user);
            return false;
        }

        if (!IsLyingOrUnconscious(target))
        {
            _popup.PopupEntity(Loc.GetString("duty-surgery-fail-standing"), target, user);
            return false;
        }

        return true;
    }

    private bool IsLyingOrUnconscious(EntityUid target)
    {
        return _standing.IsDown(target) || IsAnesthetized(target);
    }

    /// <summary>Сон, обморок или крит — боли нет.</summary>
    private bool IsAnesthetized(EntityUid target)
    {
        return HasComp<SleepingComponent>(target) || _mobState.IsIncapacitated(target);
    }

    private bool CheckImplantAttempt(EntityUid implant, EntityUid target, EntityUid user)
    {
        var ev = new DutyCyberImplantAttemptEvent(target, user);
        RaiseLocalEvent(implant, ref ev);
        if (!ev.Cancelled)
            return true;

        _popup.PopupEntity(Loc.GetString(ev.Reason ?? "duty-surgery-fail-denied"), target, user);
        return false;
    }

    private void TryStartInsert(Entity<DutyImplantSurgeryKitComponent> kit, EntityUid user, EntityUid target, EntityUid implant)
    {
        if (!CanOperate(kit, user, target) || !CheckImplantAttempt(implant, target, user))
            return;

        StartStage(kit, user, target, remove: false, null, 0);
    }

    private void TryStartRemove(Entity<DutyImplantSurgeryKitComponent> kit, EntityUid user, EntityUid target, EntityUid implant)
    {
        if (!CanOperate(kit, user, target))
            return;

        StartStage(kit, user, target, remove: true, GetNetEntity(implant), 0);
    }

    #endregion

    #region Цепочка этапов

    private List<DutySurgeryStage> Stages(DutyImplantSurgeryKitComponent comp, bool remove)
    {
        return remove ? comp.RemoveStages : comp.InsertStages;
    }

    /// <summary>
    /// Запустить doafter этапа <paramref name="index"/>. Если этапов нет вовсе — сразу выполнить действие.
    /// </summary>
    private void StartStage(Entity<DutyImplantSurgeryKitComponent> kit, EntityUid user, EntityUid target, bool remove, NetEntity? implant, int index)
    {
        var stages = Stages(kit.Comp, remove);
        if (stages.Count == 0)
        {
            PerformAction(kit, user, target, remove, implant);
            return;
        }

        var stage = stages[index];
        var delay = stage.Delay * (HasSurgeryTool(user, kit) ? kit.Comp.ToolSpeedup : 1f);

        var ev = new DutyImplantSurgeryDoAfterEvent { Stage = index, Remove = remove, Implant = implant };
        var args = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(delay), ev, kit, target: target, used: kit)
        {
            BreakOnMove = true,
            NeedHand = true,
            // Урон этапа и крики не должны рвать собственную операцию; боль от чужих ударов — отдельная история.
            BreakOnDamage = false,
        };

        if (!_doAfter.TryStartDoAfter(args))
            return;

        var msg = Loc.GetString(stage.Popup,
            ("user", Identity.Entity(user, EntityManager)),
            ("target", Identity.Entity(target, EntityManager)));
        _popup.PopupEntity(msg, target, PopupType.Medium);

        if (stage.Sound != null)
            _audio.PlayPvs(stage.Sound, target);
    }

    private bool HasSurgeryTool(EntityUid user, EntityUid kit)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (held != kit && _tag.HasTag(held, SurgeryToolTag))
                return true;
        }

        return false;
    }

    private void OnDoAfter(Entity<DutyImplantSurgeryKitComponent> kit, ref DutyImplantSurgeryDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        args.Handled = true;
        var user = args.User;
        var stages = Stages(kit.Comp, args.Remove);

        // Состояние могло измениться за время этапа: пациент встал, имплант пропал.
        if (!IsLyingOrUnconscious(target))
        {
            _popup.PopupEntity(Loc.GetString("duty-surgery-fail-standing"), target, user);
            return;
        }

        if (args.Stage < stages.Count)
            ApplyStageEffects(stages[args.Stage], user, target);

        // Имплант ложится/вынимается по окончании предпоследнего этапа: «вживление → шов».
        if (ActionPending(kit, args) && !PerformAction(kit, user, target, args.Remove, args.Implant))
            return;

        if (args.Stage + 1 < stages.Count)
            StartStage(kit, user, target, args.Remove, args.Implant, args.Stage + 1);
    }

    /// <summary>
    /// Действие выполняется ровно один раз — на этапе <c>actionStage</c>.
    /// </summary>
    private bool ActionPending(Entity<DutyImplantSurgeryKitComponent> kit, DutyImplantSurgeryDoAfterEvent args)
    {
        var count = Stages(kit.Comp, args.Remove).Count;
        return args.Stage == System.Math.Max(0, count - 2);
    }

    private void ApplyStageEffects(DutySurgeryStage stage, EntityUid user, EntityUid target)
    {
        if (stage.Damage != null)
            _damageable.TryChangeDamage(target, stage.Damage, ignoreResistances: true, interruptsDoAfters: false, origin: user);

        if (stage.Bleed != 0f)
            _bloodstream.TryModifyBleedAmount(target, stage.Bleed);

        if (!stage.Pain || IsAnesthetized(target))
            return;

        _chat.TryEmoteWithChat(target, "Scream", ignoreActionBlocker: true, forceEmote: true);
        _popup.PopupEntity(Loc.GetString("duty-surgery-pain"), target, target, PopupType.LargeCaution);
        // Красная виньетка — ощущение самого пациента.
        RaiseNetworkEvent(new DutyPainFlashEvent(), target);
    }

    /// <returns>false — операция прервана (имплант пропал, отказ).</returns>
    private bool PerformAction(Entity<DutyImplantSurgeryKitComponent> kit, EntityUid user, EntityUid target, bool remove, NetEntity? implantNet)
    {
        return remove ? DoRemove(kit, target, implantNet) : DoInsert(kit, user, target);
    }

    private bool DoInsert(Entity<DutyImplantSurgeryKitComponent> kit, EntityUid user, EntityUid target)
    {
        if (!_kitSystem.TryGetLoadedImplant(kit, out var implant)
            || !_container.TryGetContainer(kit, DutyImplantSurgeryKitComponent.ContainerId, out var slot))
        {
            return false;
        }

        // Пока резали, мог встать другой имплант (4-я пластина) — проверяем ещё раз перед самой вставкой.
        if (!CheckImplantAttempt(implant.Value, target, user))
            return false;

        _container.Remove(implant.Value, slot);
        _implants.ForceImplant(target, implant.Value);
        return true;
    }

    private bool DoRemove(Entity<DutyImplantSurgeryKitComponent> kit, EntityUid target, NetEntity? implantNet)
    {
        if (implantNet == null
            || !TryGetEntity(implantNet.Value, out var implant)
            || !TryComp<ImplantedComponent>(target, out var implanted)
            || !implanted.ImplantContainer.Contains(implant.Value)
            || !_container.TryGetContainer(kit, DutyImplantSurgeryKitComponent.ContainerId, out var slot)
            || slot.ContainedEntities.Count > 0)
        {
            return false;
        }

        _container.Remove(implant.Value, implanted.ImplantContainer);
        _container.Insert(implant.Value, slot);
        return true;
    }

    #endregion
}
