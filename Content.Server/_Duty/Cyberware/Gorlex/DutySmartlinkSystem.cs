// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Gorlex;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;

namespace Content.Server._Duty.Cyberware.Gorlex;

/// <summary>
/// _Duty: «Смартлинк». Множитель разброса лежит на самом оружии (метка Горлекса), поэтому при взятии/выбросе
/// и при смене состояния импланта (ЭМИ, вживление) пересчитываем оружие в руках носителя.
/// Трофейное оружие без метки не затрагивается.
/// </summary>
public sealed partial class DutySmartlinkSystem : EntitySystem
{
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private DutyGorlexSharedSystem _shared = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    private EntityQuery<DutySmartlinkComponent> _linkQuery;
    private EntityQuery<DutyGorlexSmartlinkWeaponComponent> _weaponQuery;

    public override void Initialize()
    {
        base.Initialize();

        _linkQuery = GetEntityQuery<DutySmartlinkComponent>();
        _weaponQuery = GetEntityQuery<DutyGorlexSmartlinkWeaponComponent>();

        SubscribeLocalEvent<DutySmartlinkComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<DutySmartlinkComponent, ImplantRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<DutySmartlinkComponent, DutyCyberEmpEvent>(OnEmp);
        SubscribeLocalEvent<DutySmartlinkComponent, DutyCyberEmpEndedEvent>(OnEmpEnded);

        SubscribeLocalEvent<DutyGorlexSmartlinkWeaponComponent, GotEquippedHandEvent>(OnWeaponEquipped);
        SubscribeLocalEvent<DutyGorlexSmartlinkWeaponComponent, GotUnequippedHandEvent>(OnWeaponUnequipped);
    }

    private void OnImplanted(Entity<DutySmartlinkComponent> ent, ref ImplantImplantedEvent args) => RefreshHost(args.Implanted);
    private void OnRemoved(Entity<DutySmartlinkComponent> ent, ref ImplantRemovedEvent args) => RefreshHost(args.Implanted);
    private void OnEmp(Entity<DutySmartlinkComponent> ent, ref DutyCyberEmpEvent args) => RefreshHost(args.Host);
    private void OnEmpEnded(Entity<DutySmartlinkComponent> ent, ref DutyCyberEmpEndedEvent args) => RefreshHost(args.Host);

    private void OnWeaponEquipped(Entity<DutyGorlexSmartlinkWeaponComponent> ent, ref GotEquippedHandEvent args)
    {
        _shared.SetSmartlinkMultiplier(ent, GetMultiplier(args.User));
    }

    private void OnWeaponUnequipped(Entity<DutyGorlexSmartlinkWeaponComponent> ent, ref GotUnequippedHandEvent args)
    {
        _shared.SetSmartlinkMultiplier(ent, 1f);
    }

    private void RefreshHost(EntityUid host)
    {
        var multiplier = GetMultiplier(host);
        foreach (var held in _hands.EnumerateHeld(host))
        {
            if (_weaponQuery.TryComp(held, out var weapon))
                _shared.SetSmartlinkMultiplier((held, weapon), multiplier);
        }
    }

    /// <summary>Лучший (меньший) множитель среди смартлинков носителя; под ЭМИ — штраф.</summary>
    private float GetMultiplier(EntityUid host)
    {
        if (!TryComp<ImplantedComponent>(host, out var implanted))
            return 1f;

        var emped = _cyber.IsHostEmped(host);
        var best = 1f;
        var found = false;
        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (!_linkQuery.TryComp(implant, out var link))
                continue;

            var m = emped ? link.EmpSpreadMultiplier : link.SpreadMultiplier;
            // Без ЭМИ бонусы не складываются, берём лучший; со штрафом — худший, как вся «глючная» связка.
            best = !found ? m : (emped ? MathF.Max(best, m) : MathF.Min(best, m));
            found = true;
        }

        return best;
    }
}
