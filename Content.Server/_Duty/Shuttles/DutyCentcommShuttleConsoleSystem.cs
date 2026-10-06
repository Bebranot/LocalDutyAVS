// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Administration.Logs;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._Duty.Shuttles;
using Content.Shared.Access.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Database;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;

namespace Content.Server._Duty.Shuttles;

/// <summary>
/// _Duty: кнопка «БСС на ЦК / на станцию» консоли шаттла ЦК.
///
/// Сам прыжок — <see cref="DutyEventFtlSystem"/> (точка выбирается и стережётся там же). Здесь — те же
/// проверки, что делает обычная консоль перед FTL (питание, якорь, взаимодействие, ShuttleComponent.Enabled,
/// CanFTL), плюс доступ, если маппер повесил на консоль AccessReader. Запреты обычного FTL кнопка
/// не обходит — это делают только админ-команды.
/// </summary>
public sealed partial class DutyCentcommShuttleConsoleSystem : EntitySystem
{
    [Dependency] private DutyEventFtlSystem _eventFtl = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private ShuttleConsoleSystem _console = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyCentcommShuttleConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFTLCompleted);

        Subs.BuiEvents<DutyCentcommShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<DutyCentcommShuttleJumpMessage>(OnJump);
        });
    }

    private void OnMapInit(Entity<DutyCentcommShuttleConsoleComponent> ent, ref MapInitEvent args)
    {
        UpdateDestination(ent);
    }

    // Направление кнопки меняется только по прилёту. Консолей таких — единицы, пересчитать все дешевле,
    // чем выяснять, какие из них смотрят на прилетевший шаттл (дрон-консоли смотрят на чужой грид).
    private void OnFTLCompleted(ref FTLCompletedEvent args)
    {
        var query = EntityQueryEnumerator<DutyCentcommShuttleConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            UpdateDestination((uid, comp));
        }
    }

    // Подстраховка на случай, если шаттл переместили без FTL (админ-телепорт, загрузка карты).
    private void OnUiOpened(Entity<DutyCentcommShuttleConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateDestination(ent);
    }

    private void UpdateDestination(Entity<DutyCentcommShuttleConsoleComponent> ent)
    {
        var atCentcomm = GetShuttle(ent) is { } shuttle && _eventFtl.IsOnCentcomm(shuttle);
        if (ent.Comp.AtCentcomm == atCentcomm)
            return;

        ent.Comp.AtCentcomm = atCentcomm;
        Dirty(ent);
    }

    /// <summary>
    /// Грид, которым управляет консоль, — так же, как его ищет обычная консоль (с учётом дрон-консолей).
    /// </summary>
    private EntityUid? GetShuttle(EntityUid console)
    {
        var pilotConsole = _console.GetDroneConsole(console) ?? console;
        return Transform(pilotConsole).GridUid;
    }

    private void OnJump(Entity<DutyCentcommShuttleConsoleComponent> ent, ref DutyCentcommShuttleJumpMessage args)
    {
        var user = args.Actor;

        if (!_blocker.CanInteract(user, ent))
            return;

        if (!this.IsPowered(ent, EntityManager))
        {
            Popup(ent, user, "duty-centcomm-console-no-power");
            return;
        }

        if (TryComp<ShuttleConsoleComponent>(ent, out var console) && !console.IsHandheldConsole && !Transform(ent).Anchored)
            return;

        if (!_access.IsAllowed(user, ent))
        {
            Popup(ent, user, "duty-centcomm-console-access-denied");
            return;
        }

        if (GetShuttle(ent) is not { } shuttle || !TryComp<ShuttleComponent>(shuttle, out var shuttleComp) || !shuttleComp.Enabled)
        {
            Popup(ent, user, "duty-centcomm-console-no-shuttle");
            return;
        }

        if (!_shuttle.CanFTL(shuttle, out var reason))
        {
            _popup.PopupEntity(reason, ent, user, PopupType.SmallCaution);
            return;
        }

        // Направление решает сервер в момент нажатия, а не надпись на кнопке: клиент мог не успеть её обновить.
        var target = _eventFtl.IsOnCentcomm(shuttle) ? DutyEventFtlTarget.Station : DutyEventFtlTarget.Centcomm;
        var travelTime = ent.Comp.TravelTime ?? _eventFtl.DefaultTravelTime;

        if (!_eventFtl.TryStart(shuttle, target, travelTime, out _, out _, out var error))
        {
            _popup.PopupEntity(error, ent, user, PopupType.SmallCaution);
            return;
        }

        Popup(ent, user, target == DutyEventFtlTarget.Centcomm
            ? "duty-centcomm-console-started-centcomm"
            : "duty-centcomm-console-started-station");

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(user):player} запустил прыжок {ToPrettyString(shuttle)} с консоли ЦК {ToPrettyString(ent)}: {target}");
    }

    private void Popup(EntityUid console, EntityUid user, string key)
    {
        _popup.PopupEntity(Loc.GetString(key), console, user);
    }
}
