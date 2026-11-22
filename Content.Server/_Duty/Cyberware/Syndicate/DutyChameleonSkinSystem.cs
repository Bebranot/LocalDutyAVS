// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Syndicate;
using Content.Shared.Atmos.Components;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;

namespace Content.Server._Duty.Cyberware.Syndicate;

/// <summary>
/// _Duty: «Хамелеон-кожа» — пока модуль в сети, носитель под ванильным StealthComponent + StealthOnMove:
/// стоя растворяется, шагом остаётся размытым пятном, бегом проявляется. Огонь, ЭМИ и смерть сбрасывают кожу полностью,
/// после чего она снова начинает прятать с нуля. Обводка NT-SEC — отдельный оверлей нейроинтерфейса и стелсом не гасится.
/// </summary>
public sealed partial class DutyChameleonSkinSystem : EntitySystem
{
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private SharedStealthSystem _stealth = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    private const float UpdateInterval = 0.5f;
    private float _accumulator;

    private EntityQuery<FlammableComponent> _flammableQuery;
    private EntityQuery<DutyChameleonSkinComponent> _skinQuery;
    private EntityQuery<StealthComponent> _stealthQuery;

    public override void Initialize()
    {
        base.Initialize();

        _flammableQuery = GetEntityQuery<FlammableComponent>();
        _skinQuery = GetEntityQuery<DutyChameleonSkinComponent>();
        _stealthQuery = GetEntityQuery<StealthComponent>();

        SubscribeLocalEvent<DutyChameleonSkinComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<DutyChameleonSkinComponent, ImplantRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<DutyChameleonSkinComponent, DutyCyberEmpEvent>(OnEmp);
    }

    private void OnImplanted(Entity<DutyChameleonSkinComponent> ent, ref ImplantImplantedEvent args)
    {
        var host = EnsureComp<DutyChameleonHostComponent>(args.Implanted);
        host.Implant = ent;
    }

    private void OnRemoved(Entity<DutyChameleonSkinComponent> ent, ref ImplantRemovedEvent args)
    {
        if (!TryComp<DutyChameleonHostComponent>(args.Implanted, out var host))
            return;

        if (_cyber.FindImplant<DutyChameleonSkinComponent>(args.Implanted) is { } other)
        {
            if (host.Implant == ent.Owner)
                host.Implant = other;
            return;
        }

        Uncloak((args.Implanted, host), false);
        _cyber.SetModuleState(ent, DutyCyberModuleState.Online);
        RemComp<DutyChameleonHostComponent>(args.Implanted);
    }

    private void OnEmp(Entity<DutyChameleonSkinComponent> ent, ref DutyCyberEmpEvent args)
    {
        if (TryComp<DutyChameleonHostComponent>(args.Host, out var host))
            Uncloak((args.Host, host), true);
    }

    private void Uncloak(Entity<DutyChameleonHostComponent> ent, bool popup)
    {
        if (!ent.Comp.Cloaked)
            return;

        ent.Comp.Cloaked = false;
        if (ent.Comp.AddedStealth)
        {
            RemComp<StealthOnMoveComponent>(ent);
            RemComp<StealthComponent>(ent);
            ent.Comp.AddedStealth = false;
        }

        if (Exists(ent.Comp.Implant))
            _cyber.SetModuleState(ent.Comp.Implant, DutyCyberModuleState.Online);

        if (popup)
            _popup.PopupEntity(Loc.GetString("duty-chameleon-off"), ent, ent, PopupType.Small);
    }

    private void Cloak(Entity<DutyChameleonHostComponent> ent, DutyChameleonSkinComponent skin)
    {
        // Чужой стелс (плащ, проектор) не трогаем.
        if (_stealthQuery.HasComp(ent))
            return;

        var stealth = EnsureComp<StealthComponent>(ent);
        var onMove = EnsureComp<StealthOnMoveComponent>(ent);
        onMove.PassiveVisibilityRate = skin.PassiveVisibilityRate;
        onMove.MovementVisibilityRate = skin.MovementVisibilityRate;
        Dirty(ent, onMove);

        ent.Comp.AddedStealth = true;
        ent.Comp.Cloaked = true;

        // Стартуем с полной видимости: кожа «подстраивается» постепенно, а не мгновенно.
        _stealth.SetVisibility(ent, 1f, stealth);
        _stealth.SetEnabled(ent, true, stealth);

        _cyber.SetModuleState(ent.Comp.Implant, DutyCyberModuleState.Active);
        _popup.PopupEntity(Loc.GetString("duty-chameleon-on"), ent, ent, PopupType.Small);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;
        _accumulator = 0f;

        var query = EntityQueryEnumerator<DutyChameleonHostComponent>();
        while (query.MoveNext(out var uid, out var host))
        {
            if (!Exists(host.Implant) || !_skinQuery.TryComp(host.Implant, out var skin))
                continue;

            var burning = _flammableQuery.TryComp(uid, out var flammable) && flammable.OnFire;
            var blocked = burning || !_cyber.IsModuleOnline(host.Implant) || _mobState.IsDead(uid);

            if (host.Cloaked)
            {
                if (blocked)
                    Uncloak((uid, host), true);
                continue;
            }

            if (!blocked)
                Cloak((uid, host), skin);
        }
    }
}
