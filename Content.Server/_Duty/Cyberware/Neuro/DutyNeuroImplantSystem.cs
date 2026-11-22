// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Duty.Neurointerface;
using Content.Server.Administration.Logs;
using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Neuro;
using Content.Shared._Duty.Neurointerface;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Database;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Overlays;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Cyberware.Neuro;

/// <summary>
/// _Duty: серверная часть нейросетевых имплантов. Сами эффекты (отряды, метки, скрамбл HUD) — в
/// <see cref="DutyNeurointerfaceSystem"/>; здесь только «когда включить/выключить» и действия имплантов.
/// </summary>
public sealed partial class DutyNeuroImplantSystem : EntitySystem
{
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private DutyNeurointerfaceSystem _neuro = default!;
    [Dependency] private DutyNeuroSquadManagerSystem _squads = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly SoundSpecifier DenySound = new SoundCollectionSpecifier("DutyCyberDeny");
    private static readonly SoundSpecifier BootSound = new SoundCollectionSpecifier("DutyCyberBoot");
    private static readonly SoundSpecifier PanicSound = new SoundCollectionSpecifier("DutyNeuroPanicBeep");

    private const float UpdateInterval = 1f;
    private float _accumulator;

    private EntityQuery<DutyCyberwareComponent> _cyberQuery;

    public override void Initialize()
    {
        base.Initialize();

        _cyberQuery = GetEntityQuery<DutyCyberwareComponent>();

        // Нейроинтерфейс-имплант (NT-SEC внутренний / тактическая сеть Горлекса).
        SubscribeLocalEvent<DutyNeuroLinkImplantComponent, ImplantImplantedEvent>(OnLinkImplanted);
        SubscribeLocalEvent<DutyNeuroLinkImplantComponent, ImplantRemovedEvent>(OnLinkRemoved);
        SubscribeLocalEvent<DutyNeuroLinkImplantComponent, DutyCyberEmpEvent>(OnLinkEmp);
        SubscribeLocalEvent<DutyNeuroLinkImplantComponent, DutyCyberEmpEndedEvent>(OnLinkEmpEnded);
        SubscribeLocalEvent<DutyNeuroLinkImplantComponent, DutyGorlexOrderActionEvent>(OnLeaderOrder);

        // Трекер СБ v2.
        SubscribeLocalEvent<DutyTrackerV2ImplantComponent, ImplantImplantedEvent>(OnTrackerImplanted);
        SubscribeLocalEvent<DutyTrackerV2ImplantComponent, ImplantRemovedEvent>(OnTrackerRemoved);
        SubscribeLocalEvent<DutyTrackerV2ImplantComponent, DutyCyberEmpEvent>(OnTrackerEmp);
        SubscribeLocalEvent<DutyTrackerV2ImplantComponent, DutyCyberEmpEndedEvent>(OnTrackerEmpEnded);

        // Тревожная кнопка.
        SubscribeLocalEvent<DutyPanicButtonImplantComponent, DutyPanicButtonActionEvent>(OnPanic);

        // Распознаватель угроз.
        SubscribeLocalEvent<DutyThreatScannerImplantComponent, ImplantImplantedEvent>(OnThreatImplanted);
        SubscribeLocalEvent<DutyThreatScannerImplantComponent, ImplantRemovedEvent>(OnThreatRemoved);
        SubscribeLocalEvent<DutyThreatScannerImplantComponent, DutyCyberEmpEvent>(OnThreatEmp);
        SubscribeLocalEvent<DutyThreatScannerImplantComponent, DutyCyberEmpEndedEvent>(OnThreatEmpEnded);

        // Призрак в сети.
        SubscribeLocalEvent<DutyNeuroGhostImplantComponent, ImplantImplantedEvent>(OnGhostImplanted);
        SubscribeLocalEvent<DutyNeuroGhostImplantComponent, ImplantRemovedEvent>(OnGhostRemoved);
        SubscribeLocalEvent<DutyNeuroGhostImplantComponent, DutyCyberEmpEvent>(OnGhostEmp);

        InitializeNavigator();
        InitializeIce();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;
        _accumulator = 0f;

        var now = _timing.CurTime;

        // Перезарядка — состояние модуля в HUD; вернуть «ОНЛАЙН», когда таймер вышел.
        var panics = EntityQueryEnumerator<DutyPanicButtonImplantComponent, DutyCyberwareComponent>();
        while (panics.MoveNext(out var uid, out _, out var cyber))
        {
            ResetCooldownIfDone(uid, cyber, now);
        }

        var worms = EntityQueryEnumerator<DutyIceWormImplantComponent, DutyCyberwareComponent>();
        while (worms.MoveNext(out var uid, out _, out var cyber))
        {
            ResetCooldownIfDone(uid, cyber, now);
        }

        // Маскировка «Призрака» возвращается через минуту после ЭМИ.
        var ghosts = EntityQueryEnumerator<DutyNeuroGhostComponent>();
        while (ghosts.MoveNext(out var uid, out var ghost))
        {
            if (!ghost.Exposed || ghost.ExposedUntil is not { } until || until > now)
                continue;

            ghost.Exposed = false;
            ghost.ExposedUntil = null;
            Dirty(uid, ghost);
            _popup.PopupEntity(Loc.GetString("duty-neuro-ghost-restored"), uid, uid, PopupType.Small);
        }
    }

    private void ResetCooldownIfDone(EntityUid implant, DutyCyberwareComponent cyber, TimeSpan now)
    {
        if (cyber.State != DutyCyberModuleState.Cooldown || cyber.CooldownEnd is { } end && end > now)
            return;

        _cyber.SetModuleState(implant, DutyCyberModuleState.Online, null, comp: cyber);
    }

    #region Helpers

    /// <summary>Общая проверка активного импланта: работает (не ЭМИ, не выгорел) и не на перезарядке.</summary>
    private bool CheckReady(EntityUid implant, EntityUid user)
    {
        if (!_cyberQuery.TryComp(implant, out var cyber) || !_cyber.IsModuleOnline(implant, cyber))
        {
            Deny(user, "duty-neuro-implant-offline");
            return false;
        }

        if (cyber.State == DutyCyberModuleState.Cooldown && cyber.CooldownEnd is { } end && end > _timing.CurTime)
        {
            var seconds = (int) Math.Ceiling((end - _timing.CurTime).TotalSeconds);
            Deny(user, "duty-neuro-implant-cooldown", seconds);
            return false;
        }

        return true;
    }

    private void Deny(EntityUid user, string loc, int seconds = 0)
    {
        _popup.PopupEntity(Loc.GetString(loc, ("seconds", seconds)), user, user, PopupType.SmallCaution);
        _audio.PlayEntity(DenySound, user, user);
    }

    /// <summary>Кулдаун и в HUD киберсистемы (таймер), и на кнопке хотбара.</summary>
    private void StartCooldown(EntityUid implant, Entity<ActionComponent> action, TimeSpan cooldown)
    {
        _cyber.SetModuleState(implant, DutyCyberModuleState.Cooldown, _timing.CurTime + cooldown);
        _actions.SetCooldown(action.Owner, cooldown);
    }

    private bool HasOtherImplant<T>(EntityUid host, EntityUid except) where T : IComponent
    {
        if (!TryComp<ImplantedComponent>(host, out var implanted))
            return false;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (implant != except && HasComp<T>(implant))
                return true;
        }

        return false;
    }

    #endregion

    #region Link (NT-SEC внутренний / тактическая сеть Горлекса)

    private void OnLinkImplanted(Entity<DutyNeuroLinkImplantComponent> ent, ref ImplantImplantedEvent args)
    {
        var host = args.Implanted;
        _neuro.AddSource(host, ent.Owner, ent.Comp.Network);

        // Действия маски (11-41, 10-4) и приказы командира — из прототипа импланта; привязаны к импланту,
        // поэтому при извлечении уходят вместе с ним.
        foreach (var proto in ent.Comp.ExtraActions)
        {
            if (_actions.AddAction(host, proto, ent.Owner) is { } action)
                ent.Comp.GrantedActions.Add(action);
        }

        if (_cyber.IsHostEmped(host))
            _neuro.SetJammed(host, true);

        _audio.PlayEntity(BootSound, host, host);
    }

    private void OnLinkRemoved(Entity<DutyNeuroLinkImplantComponent> ent, ref ImplantRemovedEvent args)
    {
        foreach (var action in ent.Comp.GrantedActions)
        {
            _actions.RemoveAction(action);
            QueueDel(action);
        }

        ent.Comp.GrantedActions.Clear();

        _neuro.SetJammed(args.Implanted, false);
        _neuro.RemoveSource(args.Implanted, ent.Owner);
    }

    private void OnLinkEmp(Entity<DutyNeuroLinkImplantComponent> ent, ref DutyCyberEmpEvent args)
    {
        _neuro.SetJammed(args.Host, true);
    }

    private void OnLinkEmpEnded(Entity<DutyNeuroLinkImplantComponent> ent, ref DutyCyberEmpEndedEvent args)
    {
        _neuro.SetJammed(args.Host, false);
    }

    private void OnLeaderOrder(Entity<DutyNeuroLinkImplantComponent> ent, ref DutyGorlexOrderActionEvent args)
    {
        if (args.Handled || !CheckReady(ent.Owner, args.Performer))
            return;

        if (!_neuro.TryCreateLeaderOrder(args.Performer, args.Order, args.Target))
            return;

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(args.Performer):user} set neuro order {args.Order} via implant at {args.Target}");
        args.Handled = true;
    }

    #endregion

    #region Tracker v2

    private void OnTrackerImplanted(Entity<DutyTrackerV2ImplantComponent> ent, ref ImplantImplantedEvent args)
    {
        if (!_cyber.IsHostEmped(args.Implanted))
            EnsureComp<DutyTrackerV2Component>(args.Implanted);
    }

    private void OnTrackerRemoved(Entity<DutyTrackerV2ImplantComponent> ent, ref ImplantRemovedEvent args)
    {
        if (!HasOtherImplant<DutyTrackerV2ImplantComponent>(args.Implanted, ent.Owner))
            RemComp<DutyTrackerV2Component>(args.Implanted);
    }

    // Под ЭМИ трекер молчит: маркер снимается, и нейроинтерфейс перестаёт его транслировать.
    private void OnTrackerEmp(Entity<DutyTrackerV2ImplantComponent> ent, ref DutyCyberEmpEvent args)
    {
        RemComp<DutyTrackerV2Component>(args.Host);
    }

    private void OnTrackerEmpEnded(Entity<DutyTrackerV2ImplantComponent> ent, ref DutyCyberEmpEndedEvent args)
    {
        EnsureComp<DutyTrackerV2Component>(args.Host);
    }

    #endregion

    #region Panic button

    private void OnPanic(Entity<DutyPanicButtonImplantComponent> ent, ref DutyPanicButtonActionEvent args)
    {
        if (args.Handled)
            return;

        var host = args.Performer;
        if (!CheckReady(ent.Owner, host))
            return;

        var zone = _neuro.GetZoneName(host);
        var name = _neuro.GetDisplayName(host);

        // В эфир говорит сам имплант — как трекер СБ: «имплант "Тревожная кнопка"» в канале.
        _squads.SendNetworkRadio(ent.Owner, Loc.GetString("duty-neuro-panic-radio", ("name", name), ("zone", zone)));
        _neuro.AddNetworkAlert(DutyNeuroNetwork.NtSec, DutyNeuroOrderType.Sos, _transform.GetMoverCoordinates(host), zone, name, ent.Comp.MarkerDuration);

        // Ложный вызов — повод для разбирательства: пишем, кто и где жал.
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(host):user} pressed the panic button implant (11-99) at {zone}");

        _popup.PopupEntity(Loc.GetString("duty-neuro-panic-self"), host, host, PopupType.Medium);
        _audio.PlayEntity(PanicSound, host, host);

        StartCooldown(ent.Owner, args.Action, ent.Comp.Cooldown);
        args.Handled = true;
    }

    #endregion

    #region Threat scanner

    private void OnThreatImplanted(Entity<DutyThreatScannerImplantComponent> ent, ref ImplantImplantedEvent args)
    {
        var host = args.Implanted;
        var scanner = EnsureComp<DutyThreatScannerComponent>(host);

        // Иконки криминального статуса — ванильный ShowCriminalRecordIcons. Его могут дать и очки/маска на теле,
        // но на самом мобе он сам не встречается; всё равно снимаем только то, что поставили сами.
        if (!HasComp<ShowCriminalRecordIconsComponent>(host))
        {
            scanner.AddedCriminalIcons = true;
            if (!_cyber.IsHostEmped(host))
                AddComp<ShowCriminalRecordIconsComponent>(host);
        }
    }

    private void OnThreatRemoved(Entity<DutyThreatScannerImplantComponent> ent, ref ImplantRemovedEvent args)
    {
        var host = args.Implanted;
        if (HasOtherImplant<DutyThreatScannerImplantComponent>(host, ent.Owner))
            return;

        if (TryComp<DutyThreatScannerComponent>(host, out var scanner) && scanner.AddedCriminalIcons)
            RemComp<ShowCriminalRecordIconsComponent>(host);

        RemComp<DutyThreatScannerComponent>(host);
    }

    private void OnThreatEmp(Entity<DutyThreatScannerImplantComponent> ent, ref DutyCyberEmpEvent args)
    {
        if (TryComp<DutyThreatScannerComponent>(args.Host, out var scanner) && scanner.AddedCriminalIcons)
            RemComp<ShowCriminalRecordIconsComponent>(args.Host);
    }

    private void OnThreatEmpEnded(Entity<DutyThreatScannerImplantComponent> ent, ref DutyCyberEmpEndedEvent args)
    {
        if (TryComp<DutyThreatScannerComponent>(args.Host, out var scanner) && scanner.AddedCriminalIcons)
            EnsureComp<ShowCriminalRecordIconsComponent>(args.Host);
    }

    #endregion

    #region Ghost in the net

    private void OnGhostImplanted(Entity<DutyNeuroGhostImplantComponent> ent, ref ImplantImplantedEvent args)
    {
        var ghost = EnsureComp<DutyNeuroGhostComponent>(args.Implanted);
        if (_cyber.IsHostEmped(args.Implanted))
        {
            ghost.Exposed = true;
            ghost.ExposedUntil = _timing.CurTime + ent.Comp.EmpExposure;
        }

        Dirty(args.Implanted, ghost);
    }

    private void OnGhostRemoved(Entity<DutyNeuroGhostImplantComponent> ent, ref ImplantRemovedEvent args)
    {
        if (!HasOtherImplant<DutyNeuroGhostImplantComponent>(args.Implanted, ent.Owner))
            RemComp<DutyNeuroGhostComponent>(args.Implanted);
    }

    private void OnGhostEmp(Entity<DutyNeuroGhostImplantComponent> ent, ref DutyCyberEmpEvent args)
    {
        if (!TryComp<DutyNeuroGhostComponent>(args.Host, out var ghost))
            return;

        // Минута «в эфире» от ЭМИ, а не от конца сбоя: повторный импульс продлевает.
        var until = _timing.CurTime + ent.Comp.EmpExposure;
        if (ghost.ExposedUntil is { } current && current > until)
            until = current;

        if (!ghost.Exposed)
            _popup.PopupEntity(Loc.GetString("duty-neuro-ghost-exposed"), args.Host, args.Host, PopupType.LargeCaution);

        ghost.Exposed = true;
        ghost.ExposedUntil = until;
        Dirty(args.Host, ghost);
    }

    #endregion
}
