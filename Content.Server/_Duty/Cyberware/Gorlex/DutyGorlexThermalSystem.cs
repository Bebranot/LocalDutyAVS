// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Gorlex;
using Content.Shared.Actions;
using Content.Shared.Flash;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Duty.Cyberware.Gorlex;

/// <summary>
/// _Duty: «Термовизор». Переключатель включает на носителе маркер с флагом Active — клиент рисует тепловые силуэты
/// мобов в радиусе сквозь стены. Цена: ускоренный голод и более долгое ослепление вспышкой.
/// </summary>
public sealed partial class DutyGorlexThermalSystem : EntitySystem
{
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SatiationSystem _satiation = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;

    private const float UpdateInterval = 1f;
    private float _accumulator;

    private EntityQuery<DutyGorlexThermalHostComponent> _hostQuery;
    private EntityQuery<SatiationComponent> _satiationQuery;

    public override void Initialize()
    {
        base.Initialize();

        _hostQuery = GetEntityQuery<DutyGorlexThermalHostComponent>();
        _satiationQuery = GetEntityQuery<SatiationComponent>();

        SubscribeLocalEvent<DutyGorlexThermalComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<DutyGorlexThermalComponent, ImplantRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<DutyGorlexThermalComponent, DutyGorlexThermalToggleEvent>(OnToggle);
        SubscribeLocalEvent<DutyGorlexThermalComponent, DutyCyberEmpEvent>(OnEmp);

        SubscribeLocalEvent<DutyGorlexThermalHostComponent, AfterFlashedEvent>(OnFlashed);
    }

    private void OnImplanted(Entity<DutyGorlexThermalComponent> ent, ref ImplantImplantedEvent args)
    {
        var host = EnsureComp<DutyGorlexThermalHostComponent>(args.Implanted);
        host.Range = ent.Comp.Range;
        host.HungerPerSecond = ent.Comp.HungerPerSecond;
        host.ExtraFlashDuration = ent.Comp.ExtraFlashDuration;
        host.Implant = ent;
        host.Active = false;
        Dirty(args.Implanted, host);
    }

    private void OnRemoved(Entity<DutyGorlexThermalComponent> ent, ref ImplantRemovedEvent args)
    {
        RemComp<DutyGorlexThermalHostComponent>(args.Implanted);
    }

    private void OnToggle(Entity<DutyGorlexThermalComponent> ent, ref DutyGorlexThermalToggleEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var host = args.Performer;
        if (!_hostQuery.TryComp(host, out var comp))
            return;

        if (!_cyber.IsModuleOnline(ent))
        {
            _popup.PopupEntity(Loc.GetString("duty-gorlex-module-deny"), host, host, PopupType.SmallCaution);
            _audio.PlayPvs(DenySound, host);
            return;
        }

        SetActive(ent, host, comp, !comp.Active);
    }

    private void OnEmp(Entity<DutyGorlexThermalComponent> ent, ref DutyCyberEmpEvent args)
    {
        if (_hostQuery.TryComp(args.Host, out var comp) && comp.Active)
            SetActive(ent, args.Host, comp, false);
    }

    private void SetActive(EntityUid implant, EntityUid host, DutyGorlexThermalHostComponent comp, bool active)
    {
        comp.Active = active;
        Dirty(host, comp);

        if (TryComp<SubdermalImplantComponent>(implant, out var sub))
            _actions.SetToggled(sub.Action, active);

        _cyber.SetModuleState(implant, active ? DutyCyberModuleState.Active : DutyCyberModuleState.Online);
        _popup.PopupEntity(Loc.GetString(active ? "duty-gorlex-thermal-on" : "duty-gorlex-thermal-off"), host, host);
    }

    private void OnFlashed(Entity<DutyGorlexThermalHostComponent> ent, ref AfterFlashedEvent args)
    {
        if (!ent.Comp.Active)
            return;

        // Тепловая матрица заливает сетчатку: вспышка в активном режиме держится дольше.
        _statusEffects.TryAddStatusEffectDuration(ent, SharedFlashSystem.FlashedKey, ent.Comp.ExtraFlashDuration);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        var dt = _accumulator;
        _accumulator = 0f;

        var query = EntityQueryEnumerator<DutyGorlexThermalHostComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Active)
                continue;

            if (_satiationQuery.TryComp(uid, out var satiation))
                _satiation.ModifyValue((uid, satiation), SatiationSystem.Hunger, -comp.HungerPerSecond * dt);
        }
    }

    private static readonly Robust.Shared.Audio.SoundSpecifier DenySound = new Robust.Shared.Audio.SoundCollectionSpecifier("DutyCyberDeny");
}
