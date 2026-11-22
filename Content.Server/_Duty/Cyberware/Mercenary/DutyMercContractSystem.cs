// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Server.Electrocution;
using Content.Server.Objectives;
using Content.Server.Objectives.Systems;
using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Mercenary;
using Content.Shared._Duty.Trauma.Events;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Jittering;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Objectives.Components;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Cyberware.Mercenary;

/// <summary>
/// _Duty: «Контракт» (наведение на цель), «Ограничитель сопутствующего ущерба» и «Подтверждение» (закрытие + эвакуация).
/// Три импланта в одной системе: они делят одно состояние — контракт на ядре.
/// </summary>
public sealed partial class DutyMercContractSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private ObjectivesSystem _objectives = default!;
    [Dependency] private TargetObjectiveSystem _targetObjective = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ElectrocutionSystem _electrocution = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    private static readonly SoundSpecifier DenySound = new SoundCollectionSpecifier("DutyCyberDeny");
    private static readonly SoundSpecifier ClosedSound = new SoundCollectionSpecifier("DutyMercContractClosed");

    private const float UpdateInterval = 0.5f;
    private float _accumulator;

    private EntityQuery<DutyMercContractImplantComponent> _contractQuery;
    private EntityQuery<DutyMercLimiterImplantComponent> _limiterQuery;
    private EntityQuery<DutyMercConfirmImplantComponent> _confirmQuery;
    private EntityQuery<ImplantedComponent> _implantedQuery;
    private EntityQuery<MindComponent> _mindQuery;
    private EntityQuery<EntityStorageComponent> _storageQuery;

    public override void Initialize()
    {
        base.Initialize();

        _contractQuery = GetEntityQuery<DutyMercContractImplantComponent>();
        _limiterQuery = GetEntityQuery<DutyMercLimiterImplantComponent>();
        _confirmQuery = GetEntityQuery<DutyMercConfirmImplantComponent>();
        _implantedQuery = GetEntityQuery<ImplantedComponent>();
        _mindQuery = GetEntityQuery<MindComponent>();
        _storageQuery = GetEntityQuery<EntityStorageComponent>();

        // Broadcast: убийство не-цели ловим по всем мобам, а не подпиской на носителя.
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);

        SubscribeLocalEvent<DutyMercContractObjectiveComponent, ObjectiveGetProgressEvent>(OnObjectiveProgress,
            after: new[] { typeof(KillPersonConditionSystem) });

        SubscribeLocalEvent<DutyMercConfirmImplantComponent, DutyMercEvacuateActionEvent>(OnEvacuate);
        SubscribeLocalEvent<DutyMercContractImplantComponent, ImplantRemovedEvent>(OnContractRemoved);
    }

    /// <summary>
    /// Ядро вырезали — клиентский оверлей берёт трекер носителя напрямую, поэтому трекер убираем целиком,
    /// иначе метка так и висит на последней известной позиции цели.
    /// </summary>
    private void OnContractRemoved(Entity<DutyMercContractImplantComponent> ent, ref ImplantRemovedEvent args)
    {
        RemComp<DutyMercContractTrackerComponent>(args.Implanted);
    }

    #region API

    /// <summary>Привязать цель к ядру и выдать наёмнику objective «ликвидировать».</summary>
    public void AssignContract(EntityUid implant, EntityUid mercenary, EntityUid targetMind, string targetName, EntProtoId objectiveProto)
    {
        if (!_contractQuery.TryComp(implant, out var contract))
            return;

        contract.TargetMind = targetMind;
        contract.TargetName = targetName;
        contract.Closed = false;
        contract.Terminated = false;
        contract.Violations = 0;

        EnsureComp<DutyMercContractTrackerComponent>(mercenary);

        if (_mind.TryGetMind(mercenary, out var mindId, out var mind)
            && _objectives.TryCreateObjective(mindId, mind, objectiveProto) is { } objective)
        {
            // TargetObjective ставит заголовок на ObjectiveAfterAssignEvent, а цели тогда ещё нет:
            // задаём цель и поднимаем событие ещё раз, чтобы заголовок собрался с именем жертвы.
            _targetObjective.SetTarget(objective, targetMind);
            if (TryComp<ObjectiveComponent>(objective, out var objComp))
            {
                var afterEv = new ObjectiveAfterAssignEvent(mindId, mind, objComp, MetaData(objective));
                RaiseLocalEvent(objective, ref afterEv);
            }

            EnsureComp<DutyMercContractObjectiveComponent>(objective);
            _mind.AddObjective(mindId, mind, objective);
            contract.Objective = objective;
        }

        _cyber.SetModuleDetail(implant, Loc.GetString("duty-merc-hud-searching"));
        _adminLog.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(mercenary):player} получил контракт на {targetName} ({ToPrettyString(targetMind)})");
    }

    /// <summary>Ядро «Контракта» в теле носителя, если есть.</summary>
    public bool TryGetContract(EntityUid host, out Entity<DutyMercContractImplantComponent> contract)
    {
        contract = default;
        if (!_implantedQuery.TryComp(host, out var implanted))
            return false;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (_contractQuery.TryComp(implant, out var comp))
            {
                contract = (implant, comp);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Расторгнуть контракт: метка гаснет, objective проваливается. Зовут «Ограничитель» (третье нарушение)
    /// и «Протокол стирания».
    /// </summary>
    public void Terminate(Entity<DutyMercContractImplantComponent> contract, EntityUid host, string reasonLoc, bool notify = true)
    {
        if (contract.Comp.Terminated)
            return;

        contract.Comp.Terminated = true;

        if (contract.Comp.Objective is { } objective && TryComp<DutyMercContractObjectiveComponent>(objective, out var objComp))
            objComp.Failed = true;

        ResetTracker(host);
        _cyber.SetModuleState(contract, DutyCyberModuleState.Burned, null, Loc.GetString("duty-merc-hud-terminated"));

        if (notify)
            RaiseNetworkEvent(new DutyMercContractEndedEvent(true, contract.Comp.TargetName), host);

        _popup.PopupEntity(Loc.GetString(reasonLoc), host, host, PopupType.LargeCaution);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"Контракт наёмника {ToPrettyString(host):player} на {contract.Comp.TargetName} расторгнут ({reasonLoc})");
    }

    #endregion

    private void ResetTracker(EntityUid host)
    {
        if (!TryComp<DutyMercContractTrackerComponent>(host, out var tracker) || tracker.Mode == DutyMercTrackMode.None)
            return;

        tracker.Mode = DutyMercTrackMode.None;
        Dirty(host, tracker);
    }

    private void Close(Entity<DutyMercContractImplantComponent> contract, EntityUid host)
    {
        contract.Comp.Closed = true;
        ResetTracker(host);
        _cyber.SetModuleState(contract, DutyCyberModuleState.Online, null, Loc.GetString("duty-merc-hud-closed"));

        // Экран «КОНТРАКТ ЗАКРЫТ» и эвакуация — работа «Подтверждения». Без него наёмник просто теряет метку.
        var confirmed = false;
        if (_implantedQuery.TryComp(host, out var implanted))
        {
            foreach (var implant in implanted.ImplantContainer.ContainedEntities)
            {
                if (!_confirmQuery.HasComp(implant))
                    continue;

                confirmed = true;
                _cyber.SetModuleState(implant, DutyCyberModuleState.Active, null, Loc.GetString("duty-merc-hud-evac-ready"));
            }
        }

        if (confirmed)
        {
            RaiseNetworkEvent(new DutyMercContractEndedEvent(false, contract.Comp.TargetName), host);
            _audio.PlayGlobal(ClosedSound, host);
        }

        _popup.PopupEntity(Loc.GetString("duty-merc-closed-popup"), host, host, PopupType.Large);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"Контракт наёмника {ToPrettyString(host):player} на {contract.Comp.TargetName} закрыт: цель мертва");
    }

    #region Ограничитель

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || args.Origin is not { } killer || killer == args.Target)
            return;

        // Наказываем только за людей: карпы и мыши — не «сопутствующий ущерб».
        if (!HasComp<HumanoidProfileComponent>(args.Target))
            return;

        if (!_implantedQuery.TryComp(killer, out var implanted))
            return;

        EntityUid? limiter = null;
        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (_limiterQuery.HasComp(implant))
            {
                limiter = implant;
                break;
            }
        }

        if (limiter is not { } limiterUid || !TryGetContract(killer, out var contract))
            return;

        if (contract.Comp.Closed || contract.Comp.Terminated)
            return;

        // Цель — законная работа.
        if (contract.Comp.TargetMind is { } targetMind
            && _mindQuery.TryComp(targetMind, out var mind)
            && mind.OwnedEntity == args.Target)
            return;

        // Ограничитель работает даже под ЭМИ: это предохранитель, а не функция.
        var limiterComp = _limiterQuery.Comp(limiterUid);
        contract.Comp.Violations++;
        var count = contract.Comp.Violations;
        var max = contract.Comp.MaxViolations;

        _electrocution.TryDoElectrocution(killer, null, limiterComp.ShockDamage, limiterComp.ShockTime, true, ignoreInsulation: true);
        _jitter.DoJitter(killer, TimeSpan.FromSeconds(4), true, 14f, 6f);
        RaiseNetworkEvent(new DutyPainFlashEvent(0.9f, 2f), killer);

        _cyber.SetModuleState(limiterUid, DutyCyberModuleState.Cooldown, _timing.CurTime + limiterComp.ScrambleTime,
            Loc.GetString("duty-merc-hud-violation", ("count", count), ("max", max)));

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"Наёмник {ToPrettyString(killer):player} убил не-цель {ToPrettyString(args.Target):target}: нарушение {count}/{max}");

        if (count >= max)
        {
            Terminate(contract, killer, "duty-merc-limiter-terminated");
            return;
        }

        _popup.PopupEntity(Loc.GetString("duty-merc-limiter-violation", ("count", count), ("max", max)), killer, killer, PopupType.LargeCaution);
    }

    #endregion

    private void OnObjectiveProgress(Entity<DutyMercContractObjectiveComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        if (ent.Comp.Failed)
            args.Progress = 0f;
    }

    #region Подтверждение

    private void OnEvacuate(Entity<DutyMercConfirmImplantComponent> ent, ref DutyMercEvacuateActionEvent args)
    {
        if (args.Handled)
            return;

        var host = args.Performer;
        if (!_cyber.IsModuleOnline(ent))
        {
            Deny(host, "duty-merc-module-offline");
            return;
        }

        if (!TryGetContract(host, out var contract) || !contract.Comp.Closed)
        {
            Deny(host, "duty-merc-evac-not-ready");
            return;
        }

        args.Handled = true;

        // Самоустранение: железо «выгорает» вместе с носителем — ни тела, ни снаряжения для допроса.
        var coords = Transform(host).Coordinates;
        Spawn(ent.Comp.EvacEffect, coords);
        _audio.PlayPvs(ent.Comp.EvacSound, coords);
        _popup.PopupCoordinates(Loc.GetString("duty-merc-evac-others", ("user", host)), coords, PopupType.Large);

        _adminLog.Add(LogType.Action, LogImpact.High, $"Наёмник {ToPrettyString(host):player} эвакуировался (самоустранение)");

        // Тело удаляется; MindSystem сам выкинет игрока в призрака.
        QueueDel(host);
    }

    private void Deny(EntityUid host, string loc)
    {
        _popup.PopupEntity(Loc.GetString(loc), host, host, PopupType.SmallCaution);
        _audio.PlayGlobal(DenySound, host);
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;
        _accumulator = 0f;

        var now = _timing.CurTime;

        // Скрамбл «Ограничителя» отыграл — модуль снова в норме, счёт нарушений остаётся в строке.
        var limiters = EntityQueryEnumerator<DutyMercLimiterImplantComponent, DutyCyberwareComponent>();
        while (limiters.MoveNext(out var uid, out _, out var cyber))
        {
            if (cyber.State != DutyCyberModuleState.Cooldown || cyber.CooldownEnd is not { } end || end > now)
                continue;

            _cyber.SetModuleState(uid, DutyCyberModuleState.Online, null, cyber.Detail, cyber);
        }

        var query = EntityQueryEnumerator<DutyMercContractImplantComponent, SubdermalImplantComponent>();
        while (query.MoveNext(out var uid, out var contract, out var sub))
        {
            if (contract.Closed || contract.Terminated || sub.ImplantedEntity is not { } host)
                continue;

            if (contract.TargetMind is not { } targetMind || !_mindQuery.TryComp(targetMind, out var mind))
                continue;

            if (_mind.IsCharacterDeadIc(mind))
            {
                Close((uid, contract), host);
                continue;
            }

            UpdateTracking((uid, contract), host, mind);
        }
    }

    private void UpdateTracking(Entity<DutyMercContractImplantComponent> contract, EntityUid host, MindComponent mind)
    {
        var tracker = EnsureComp<DutyMercContractTrackerComponent>(host);
        var mode = DutyMercTrackMode.Lost;
        var pos = Vector2.Zero;
        var distance = 0f;

        if (mind.OwnedEntity is { } target && _cyber.IsModuleOnline(contract) && !IsShielded(target))
        {
            var hostPos = _transform.GetMapCoordinates(host);
            var targetPos = _transform.GetMapCoordinates(target);

            if (hostPos.MapId == targetPos.MapId)
            {
                var delta = targetPos.Position - hostPos.Position;
                var exact = delta.Length();

                if (exact <= contract.Comp.ExactRange)
                {
                    mode = DutyMercTrackMode.Exact;
                    pos = targetPos.Position;
                    distance = MathF.Round(exact);
                }
                else if (exact <= contract.Comp.DirectionRange)
                {
                    // Ромб «где-то тут»: сетка 3×3 тайла, дистанция с шагом 5 м.
                    mode = DutyMercTrackMode.Approximate;
                    pos = new Vector2(MathF.Floor(targetPos.Position.X / 3f) * 3f + 1.5f,
                        MathF.Floor(targetPos.Position.Y / 3f) * 3f + 1.5f);
                    distance = MathF.Round(exact / 5f) * 5f;
                }
                else
                {
                    // Далеко: только направление с шагом 15° и дистанция с шагом 10 м.
                    mode = DutyMercTrackMode.Direction;
                    const float step = MathF.PI / 12f;
                    var angle = MathF.Round(MathF.Atan2(delta.Y, delta.X) / step) * step;
                    distance = MathF.Round(exact / 10f) * 10f;
                    pos = hostPos.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
                }
            }
        }

        if (tracker.Mode != mode || tracker.TargetPos != pos || MathF.Abs(tracker.Distance - distance) > 0.01f)
        {
            tracker.Mode = mode;
            tracker.TargetPos = pos;
            tracker.Distance = distance;
            Dirty(host, tracker);
        }

        var meters = ((int) distance).ToString();
        var detail = mode switch
        {
            DutyMercTrackMode.Exact => Loc.GetString("duty-merc-hud-exact", ("meters", meters)),
            DutyMercTrackMode.Approximate or DutyMercTrackMode.Direction => Loc.GetString("duty-merc-hud-approx", ("meters", meters)),
            _ => Loc.GetString("duty-merc-hud-lost"),
        };
        _cyber.SetModuleDetail(contract, detail);
    }

    /// <summary>
    /// «Клетки Фарадея» в игре нет — её роль играет любой закрытый ящик (шкаф, гроб, морг, мешок для тел):
    /// металл глушит маячок. Даёт цели и СБ понятный контрплей — спрятать человека.
    /// </summary>
    private bool IsShielded(EntityUid target)
    {
        var current = target;
        // Пара уровней вложенности хватает (тело в мешке в морге), дальше не ищем — это горячий путь.
        for (var i = 0; i < 3; i++)
        {
            if (!_container.TryGetContainingContainer(current, out var container))
                return false;

            if (_storageQuery.HasComp(container.Owner))
                return true;

            current = container.Owner;
        }

        return false;
    }
}
