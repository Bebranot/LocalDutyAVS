// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Syndicate;
using Content.Shared.Implants;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;

namespace Content.Server._Duty.Cyberware.Syndicate;

/// <summary>
/// _Duty: «Ошейник» — поддельный трекер. Действие «пришпиливает» ложную точку на месте носителя:
/// монитор экипажа (суит-сенсор) дальше показывает его там, пока носитель идёт куда угодно. Повторное нажатие снимает подмену.
/// Хук — <see cref="DutyFakeTrackerQueryEvent"/> в SharedSuitSensorSystem / ADTSharedSuitSensorSystem.
/// Нейроинтерфейсу нужен свой хук на это же событие (трекер NT-SEC): подписка — на <see cref="DutyFakeTrackerHostComponent"/>.
/// </summary>
public sealed partial class DutyFakeTrackerSystem : EntitySystem
{
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private EntityQuery<DutyFakeTrackerComponent> _trackerQuery;

    public override void Initialize()
    {
        base.Initialize();

        _trackerQuery = GetEntityQuery<DutyFakeTrackerComponent>();

        SubscribeLocalEvent<DutyFakeTrackerComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<DutyFakeTrackerComponent, ImplantRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<DutyFakeTrackerComponent, DutyFakeTrackerActionEvent>(OnAction);
        SubscribeLocalEvent<DutyFakeTrackerHostComponent, DutyFakeTrackerQueryEvent>(OnQuery);
    }

    private void OnImplanted(Entity<DutyFakeTrackerComponent> ent, ref ImplantImplantedEvent args)
    {
        EnsureComp<DutyFakeTrackerHostComponent>(args.Implanted).Implant = ent;
    }

    private void OnRemoved(Entity<DutyFakeTrackerComponent> ent, ref ImplantRemovedEvent args)
    {
        if (_cyber.FindImplant<DutyFakeTrackerComponent>(args.Implanted) is not { } other)
            RemComp<DutyFakeTrackerHostComponent>(args.Implanted);
        else if (TryComp<DutyFakeTrackerHostComponent>(args.Implanted, out var host) && host.Implant == ent.Owner)
            host.Implant = other;
        _cyber.SetModuleState(ent, DutyCyberModuleState.Online);
    }

    private void OnAction(Entity<DutyFakeTrackerComponent> ent, ref DutyFakeTrackerActionEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;

        var user = args.Performer;
        if (!_cyber.IsModuleOnline(ent) || !TryComp<DutyFakeTrackerHostComponent>(user, out var host))
        {
            _popup.PopupEntity(Loc.GetString("duty-syndicate-offline"), user, user, PopupType.SmallCaution);
            _audio.PlayPvs(new SoundCollectionSpecifier("DutyCyberDeny"), user);
            return;
        }

        if (host.Fake != null)
        {
            host.Fake = null;
            _cyber.SetModuleState(ent, DutyCyberModuleState.Online, detail: string.Empty);
            _popup.PopupEntity(Loc.GetString("duty-faketracker-off"), user, user);
            return;
        }

        var xform = Transform(user);
        if (xform.GridUid is not { } grid)
        {
            // Подмена координат осмысленна только на станции/шаттле — там, где работают мониторы.
            _popup.PopupEntity(Loc.GetString("duty-faketracker-no-grid"), user, user, PopupType.SmallCaution);
            return;
        }

        host.Fake = new EntityCoordinates(grid, Vector2.Transform(_transform.GetWorldPosition(xform), _transform.GetInvWorldMatrix(grid)));
        _cyber.SetModuleState(ent, DutyCyberModuleState.Active, detail: Loc.GetString("duty-faketracker-detail"));
        _popup.PopupEntity(Loc.GetString("duty-faketracker-on"), user, user);
    }

    private void OnQuery(Entity<DutyFakeTrackerHostComponent> ent, ref DutyFakeTrackerQueryEvent args)
    {
        if (ent.Comp.Fake is not { } fake || !Exists(fake.EntityId))
            return;

        // Под ЭМИ подделка слетает — монитор снова видит правду.
        if (_cyber.IsHostEmped(ent))
            return;

        args.Coordinates = fake;
    }
}
