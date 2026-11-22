// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Examine;
using Content.Shared.Implants;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Network;

namespace Content.Shared._Duty.Cyberware.Syndicate;

/// <summary>
/// _Duty: общая часть «Керензикова» (Cybersun): маркер носителя и предиктящиеся модификаторы — скорость движения
/// и темп стрельбы. Срабатывание, таймеры, темп ближнего боя и чат — в серверной DutyKerenzikovServerSystem.
/// Заодно здесь строка осмотра «виден порт» для подкожного автоинъектора (маркер носителя общий для Shared).
/// </summary>
public sealed partial class DutyKerenzikovSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedDutyCyberwareSystem _cyber = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyKerenzikovComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<DutyKerenzikovComponent, ImplantRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<DutyKerenzikovHostComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<DutyKerenzikovBoostedGunComponent, GunRefreshModifiersEvent>(OnGunRefresh);
        SubscribeLocalEvent<DutyAutoInjectorHostComponent, ExaminedEvent>(OnInjectorExamined);
    }

    private void OnImplanted(Entity<DutyKerenzikovComponent> ent, ref ImplantImplantedEvent args)
    {
        if (_net.IsServer)
            EnsureComp<DutyKerenzikovHostComponent>(args.Implanted);
    }

    private void OnRemoved(Entity<DutyKerenzikovComponent> ent, ref ImplantRemovedEvent args)
    {
        if (!_net.IsServer)
            return;

        // Имплант уже вне контейнера: если в теле остался второй — маркер нужен.
        // Разгон и маркеры на оружии снимает серверная система по ComponentShutdown носителя.
        if (_cyber.FindImplant<DutyKerenzikovComponent>(args.Implanted) == null)
            RemComp<DutyKerenzikovHostComponent>(args.Implanted);
    }

    private void OnRefreshSpeed(Entity<DutyKerenzikovHostComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Active)
            args.ModifySpeed(ent.Comp.Multiplier, ent.Comp.Multiplier);
    }

    private void OnGunRefresh(Entity<DutyKerenzikovBoostedGunComponent> ent, ref GunRefreshModifiersEvent args)
    {
        args.FireRate *= ent.Comp.Multiplier;
    }

    private void OnInjectorExamined(Entity<DutyAutoInjectorHostComponent> ent, ref ExaminedEvent args)
    {
        // Порт заметен только вблизи и только тем, кто смотрит на чужого носителя.
        if (args.Examiner == ent.Owner || !args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("duty-autoinjector-examine-port"));
    }
}
