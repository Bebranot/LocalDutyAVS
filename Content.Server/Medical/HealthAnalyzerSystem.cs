using Content.Server.Medical.Components;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage.Components;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.MedicalScanner;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.PowerCell;
using Content.Shared.Temperature.Components;
using Content.Shared.Traits.Assorted;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;
using Content.Server.Body.Systems;
using Content.Shared.Mobs; // _Duty
using Content.Server._Duty.HealthAnalyzer; // _Duty
using Content.Shared._Duty.HealthAnalyzer; // _Duty
using Content.Shared._Duty.Heartbeat; // _Duty
using Content.Shared._Duty.Trauma.Systems; // _Duty

namespace Content.Server.Medical;

public sealed class HealthAnalyzerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PowerCellSystem _cell = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstreamSystem = default!;
    [Dependency] private readonly SharedHeartbeatSystem _heartbeat = default!; // _Duty
    [Dependency] private readonly TraumaAnalyzerSystem _traumaAnalyzer = default!; // _Duty

    /// <summary>
    /// _Duty: последнее отправленное сканирующему звуковое состояние пульса, по анализатору.
    /// Нужно, чтобы не слать <see cref="HealthAnalyzerAudioEvent"/> на каждый Update (раз в
    /// UpdateInterval), а только при реальной смене уровня/крита/грани смерти — как уже
    /// сделано для networked-состояния в <c>HeartbeatSystem.Recalculate</c>.
    /// </summary>
    private readonly Dictionary<EntityUid, (HeartbeatLevel Level, bool InCrit, bool NearDeath, bool Flatline)> _lastSentAudio = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<HealthAnalyzerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<HealthAnalyzerComponent, HealthAnalyzerDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<HealthAnalyzerComponent, EntGotInsertedIntoContainerMessage>(OnInsertedIntoContainer);
        SubscribeLocalEvent<HealthAnalyzerComponent, ItemToggledEvent>(OnToggled);
        SubscribeLocalEvent<HealthAnalyzerComponent, DroppedEvent>(OnDropped);
        SubscribeLocalEvent<HealthAnalyzerComponent, ComponentShutdown>(OnShutdown); // _Duty
        SubscribeLocalEvent<HealthAnalyzerComponent, BoundUIClosedEvent>(OnUiClosed); // _Duty
    }

    /// <summary>
    /// _Duty: закрытие окна анализатора глушит только звук у того, кто его закрыл — прибор
    /// продолжает сканировать в фоне (ванильное поведение), меняется лишь наш кастомный звук.
    /// </summary>
    private void OnUiClosed(Entity<HealthAnalyzerComponent> ent, ref BoundUIClosedEvent args)
    {
        if (args.UiKey is not HealthAnalyzerUiKey || ent.Comp.ScannerUser != args.Actor)
            return;

        _lastSentAudio.Remove(ent.Owner);
        RaiseNetworkEvent(new HealthAnalyzerStopAudioEvent(), args.Actor);
    }

    /// <summary>
    /// _Duty: анализатор уничтожается во время скана — чистим наш кэш звука и глушим сканирующего,
    /// иначе в <see cref="_lastSentAudio"/> осталась бы висячая запись по удалённому EntityUid.
    /// </summary>
    private void OnShutdown(Entity<HealthAnalyzerComponent> ent, ref ComponentShutdown args)
    {
        _lastSentAudio.Remove(ent.Owner);

        if (ent.Comp.ScannerUser is { } scannerUser)
            RaiseNetworkEvent(new HealthAnalyzerStopAudioEvent(), scannerUser);
    }

    public override void Update(float frameTime)
    {
        var analyzerQuery = EntityQueryEnumerator<HealthAnalyzerComponent, TransformComponent>();
        while (analyzerQuery.MoveNext(out var uid, out var component, out var transform))
        {
            //Update rate limited to 1 second
            if (component.NextUpdate > _timing.CurTime)
                continue;

            if (component.ScannedEntity is not { } patient)
                continue;

            if (Deleted(patient))
            {
                StopAnalyzingEntity((uid, component), patient);
                continue;
            }

            component.NextUpdate = _timing.CurTime + component.UpdateInterval;

            //Get distance between health analyzer and the scanned entity
            //null is infinite range
            var patientCoordinates = Transform(patient).Coordinates;
            if (component.MaxScanRange != null && !_transformSystem.InRange(patientCoordinates, transform.Coordinates, component.MaxScanRange.Value))
            {
                //Range too far, disable updates until they are back in range
                PauseAnalyzingEntity((uid, component), patient);
                continue;
            }

            component.IsAnalyzerActive = true;
            UpdateScannedUser(uid, patient, true);
        }
    }

    /// <summary>
    /// Trigger the doafter for scanning
    /// </summary>
    private void OnAfterInteract(Entity<HealthAnalyzerComponent> uid, ref AfterInteractEvent args)
    {
        if (args.Target == null || !args.CanReach || !HasComp<MobStateComponent>(args.Target) || !_cell.HasDrawCharge(uid.Owner, user: args.User))
            return;

        _audio.PlayPvs(uid.Comp.ScanningBeginSound, uid);

        var doAfterCancelled = !_doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, uid.Comp.ScanDelay, new HealthAnalyzerDoAfterEvent(), uid, target: args.Target, used: uid)
        {
            NeedHand = true,
            BreakOnMove = true,
        });

        if (args.Target == args.User || doAfterCancelled || uid.Comp.Silent)
            return;

        var msg = Loc.GetString("health-analyzer-popup-scan-target", ("user", Identity.Entity(args.User, EntityManager)));
        _popupSystem.PopupEntity(msg, args.Target.Value, args.Target.Value, PopupType.Medium);
    }

    private void OnDoAfter(Entity<HealthAnalyzerComponent> uid, ref HealthAnalyzerDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target == null || !_cell.HasDrawCharge(uid.Owner, user: args.User))
            return;

        if (!uid.Comp.Silent)
            _audio.PlayPvs(uid.Comp.ScanningEndSound, uid);

        OpenUserInterface(args.User, uid);
        BeginAnalyzingEntity(uid, args.Target.Value, args.User);

        // _Duty: сразу запускаем сердцебиение цели у сканирующего
        SendHeartbeatAudio(uid.Owner, args.Target.Value, args.User, true);

        args.Handled = true;
    }

    /// <summary>
    /// Turn off when placed into a storage item or moved between slots/hands
    /// </summary>
    private void OnInsertedIntoContainer(Entity<HealthAnalyzerComponent> uid, ref EntGotInsertedIntoContainerMessage args)
    {
        if (uid.Comp.ScannedEntity is { } patient)
            _toggle.TryDeactivate(uid.Owner);

        // _Duty
        if (uid.Comp.ScannerUser is { } scannerUser)
        {
            _lastSentAudio.Remove(uid.Owner);
            RaiseNetworkEvent(new HealthAnalyzerStopAudioEvent(), scannerUser);
        }
    }

    /// <summary>
    /// Disable continuous updates once turned off
    /// </summary>
    private void OnToggled(Entity<HealthAnalyzerComponent> ent, ref ItemToggledEvent args)
    {
        if (!args.Activated && ent.Comp.ScannedEntity is { } patient)
            StopAnalyzingEntity(ent, patient);

        // _Duty
        if (!args.Activated && ent.Comp.ScannerUser is { } scannerUser)
        {
            _lastSentAudio.Remove(ent.Owner);
            RaiseNetworkEvent(new HealthAnalyzerStopAudioEvent(), scannerUser);
        }
    }

    /// <summary>
    /// Turn off the analyser when dropped
    /// </summary>
    private void OnDropped(Entity<HealthAnalyzerComponent> uid, ref DroppedEvent args)
    {
        if (uid.Comp.ScannedEntity is { } patient)
            _toggle.TryDeactivate(uid.Owner);

        // _Duty
        if (uid.Comp.ScannerUser is { } scannerUser)
        {
            _lastSentAudio.Remove(uid.Owner);
            RaiseNetworkEvent(new HealthAnalyzerStopAudioEvent(), scannerUser);
        }
    }

    private void OpenUserInterface(EntityUid user, EntityUid analyzer)
    {
        if (!_uiSystem.HasUi(analyzer, HealthAnalyzerUiKey.Key))
            return;

        _uiSystem.OpenUi(analyzer, HealthAnalyzerUiKey.Key, user);
    }

    /// <summary>
    /// Mark the entity as having its health analyzed, and link the analyzer to it
    /// </summary>
    /// <param name="healthAnalyzer">The health analyzer that should receive the updates</param>
    /// <param name="target">The entity to start analyzing</param>
    private void BeginAnalyzingEntity(Entity<HealthAnalyzerComponent> healthAnalyzer, EntityUid target, EntityUid user)
    {
        //Link the health analyzer to the scanned entity
        healthAnalyzer.Comp.ScannedEntity = target;
        healthAnalyzer.Comp.ScannerUser = user; // _Duty

        _toggle.TryActivate(healthAnalyzer.Owner);

        UpdateScannedUser(healthAnalyzer, target, true);
    }

    /// <summary>
    /// Remove the analyzer from the active list, and remove the component if it has no active analyzers
    /// </summary>
    /// <param name="healthAnalyzer">The health analyzer that's receiving the updates</param>
    /// <param name="target">The entity to analyze</param>
    private void StopAnalyzingEntity(Entity<HealthAnalyzerComponent> healthAnalyzer, EntityUid target)
    {
        var scannerUser = healthAnalyzer.Comp.ScannerUser; // _Duty

        //Unlink the analyzer
        healthAnalyzer.Comp.ScannedEntity = null;
        healthAnalyzer.Comp.ScannerUser = null; // _Duty

        if (scannerUser is { } user)
        {
            _lastSentAudio.Remove(healthAnalyzer.Owner); // _Duty
            RaiseNetworkEvent(new HealthAnalyzerStopAudioEvent(), user); // _Duty
        }

        _toggle.TryDeactivate(healthAnalyzer.Owner);

        UpdateScannedUser(healthAnalyzer, target, false);
    }


    /// <summary>
    /// If the scanner is active, sends one last update and sets it to inactive.
    /// </summary>
    /// <param name="healthAnalyzer">The health analyzer that's receiving the updates</param>
    /// <param name="target">The entity to analyze</param>
    private void PauseAnalyzingEntity(Entity<HealthAnalyzerComponent> healthAnalyzer, EntityUid target)
    {
        // _Duty
        if (healthAnalyzer.Comp.ScannerUser is { } scannerUser)
        {
            _lastSentAudio.Remove(healthAnalyzer.Owner);
            RaiseNetworkEvent(new HealthAnalyzerStopAudioEvent(), scannerUser);
        }

        if (!healthAnalyzer.Comp.IsAnalyzerActive)
            return;

        UpdateScannedUser(healthAnalyzer, target, false);
        healthAnalyzer.Comp.IsAnalyzerActive = false;
    }

    /// <summary>
    /// Send an update for the target to the healthAnalyzer
    /// </summary>
    /// <param name="healthAnalyzer">The health analyzer</param>
    /// <param name="target">The entity being scanned</param>
    /// <param name="scanMode">True makes the UI show ACTIVE, False makes the UI show INACTIVE</param>
    public void UpdateScannedUser(EntityUid healthAnalyzer, EntityUid target, bool scanMode)
    {
        if (!_uiSystem.HasUi(healthAnalyzer, HealthAnalyzerUiKey.Key)
            || !HasComp<DamageableComponent>(target))
            return;

        var uiState = GetHealthAnalyzerUiState(target);
        uiState.ScanMode = scanMode;

        _uiSystem.ServerSendUiMessage(
            healthAnalyzer,
            HealthAnalyzerUiKey.Key,
            new HealthAnalyzerScannedUserMessage(uiState)
        );

        // _Duty: обновляем сердцебиение цели у сканирующего
        if (TryComp<HealthAnalyzerComponent>(healthAnalyzer, out var analyzer) && analyzer.ScannerUser is { } scannerUser)
            SendHeartbeatAudio(healthAnalyzer, target, scannerUser, false);
    }

    /// <summary>
    /// _Duty: шлёт сканирующему уровень сердцебиения цели (уровень пульса, крит, грань смерти).
    /// </summary>
    private void SendHeartbeatAudio(EntityUid healthAnalyzer, EntityUid target, EntityUid user, bool forceRestart)
    {
        // _Duty: «вторая жизнь» (Лазарус) активна — глушим все наши звуки у сканирующего.
        if (_heartbeat.IsSuppressed(target))
        {
            _lastSentAudio.Remove(healthAnalyzer);
            RaiseNetworkEvent(new HealthAnalyzerStopAudioEvent(), user);
            return;
        }

        var level = _heartbeat.GetLevel(target);
        var inCrit = _heartbeat.IsInCrit(target);
        var nearDeath = _heartbeat.GetVitalFraction(target) < SharedHeartbeatSystem.NearDeathFraction;
        var flatline = TryComp<MobStateComponent>(target, out var mob) && mob.CurrentState == MobState.Dead;

        // _Duty: ровная линия — один раз на КАЖДОГО зрителя. Память о том, кто уже слышал, живёт
        // на пациенте (см. HealthAnalyzerFlatlineHeardComponent): переживает паузу/повторный
        // анализ, поэтому тот же медик звук не переиграет, а другой, впервые сканирующий тело,
        // услышит его один раз. Компонент умирает вместе с телом — утечки нет.
        var playFlatline = false;
        if (flatline)
        {
            var heard = EnsureComp<HealthAnalyzerFlatlineHeardComponent>(target);
            if (heard.Users.Add(user))
                playFlatline = true;
        }
        else
        {
            // Цель жива/воскрешена — сбрасываем «уже слышали» у всех, чтобы новая смерть снова прозвучала.
            RemComp<HealthAnalyzerFlatlineHeardComponent>(target);
        }

        var state = (level, inCrit, nearDeath, flatline);
        // playFlatline — одноразовый импульс, поэтому его наличие всегда пробивает дедуп.
        if (!forceRestart && !playFlatline && _lastSentAudio.TryGetValue(healthAnalyzer, out var last) && last == state)
            return;

        _lastSentAudio[healthAnalyzer] = state;
        RaiseNetworkEvent(new HealthAnalyzerAudioEvent(level, inCrit, nearDeath, flatline, playFlatline, forceRestart), user);
    }

    /// <summary>
    /// Creates a HealthAnalyzerState based on the current state of an entity.
    /// </summary>
    /// <param name="target">The entity being scanned</param>
    /// <returns></returns>
    public HealthAnalyzerUiState GetHealthAnalyzerUiState(EntityUid? target)
    {
        if (!target.HasValue || !HasComp<DamageableComponent>(target))
            return new HealthAnalyzerUiState();

        var entity = target.Value;
        var bodyTemperature = float.NaN;

        if (TryComp<TemperatureComponent>(entity, out var temp))
            bodyTemperature = temp.CurrentTemperature;

        var bloodAmount = float.NaN;
        var bleeding = false;
        var unrevivable = false;

        if (TryComp<BloodstreamComponent>(entity, out var bloodstream) &&
            _solutionContainerSystem.ResolveSolution(entity, bloodstream.BloodSolutionName,
                ref bloodstream.BloodSolution, out var bloodSolution))
        {
            bloodAmount = _bloodstreamSystem.GetBloodLevel(entity);
            bleeding = bloodstream.BleedAmount > 0;
        }

        if (TryComp<UnrevivableComponent>(entity, out var unrevivableComp) && unrevivableComp.Analyzable)
            unrevivable = true;

        // _Duty: состояние цели для эмбиент-звука в анализаторе
        var mobState = MobState.Alive;
        if (TryComp<MobStateComponent>(target, out var mob))
            mobState = mob.CurrentState;

        // _Duty: «живучесть» для мигающей крит-таблички (1 = полное HP, ≤0 = крит).
        var healthFraction = _heartbeat.GetVitalFraction(entity);

        // ADT-Tweak start: - Get a list of metabolizing chemicals
        List<(string ReagentId, FixedPoint2 Quantity)>? metabolizingReagents = null;
        if (TryComp<BloodstreamComponent>(target, out var bloodstreamComp) &&
            _solutionContainerSystem.TryGetSolution(target.Value, BloodstreamComponent.DefaultBloodSolutionName, out _, out var chemicalsSolution))
        {
            metabolizingReagents = new List<(string, FixedPoint2)>();
            foreach (var (reagent, quantity) in chemicalsSolution.Contents)
            {
                metabolizingReagents.Add((reagent.Prototype, quantity));
            }
        }
        // ADT-Tweak end

        // _Duty: тяжёлые травмы (переломы с тирами, вывихи, артериальное кровотечение).
        var traumas = _traumaAnalyzer.GetEntries(entity);

        return new HealthAnalyzerUiState(
            GetNetEntity(entity),
            bodyTemperature,
            bloodAmount,
            null,
            bleeding,
            unrevivable,
            metabolizingReagents, // ADT-Tweak
            mobState, // _Duty
            healthFraction, // _Duty
            traumas // _Duty
        );
    }
}
