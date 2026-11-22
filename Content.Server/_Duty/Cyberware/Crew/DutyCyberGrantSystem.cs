// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Crew;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Popups;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Duty.Cyberware.Crew;

/// <summary>
/// _Duty: выдача/снятие компонентов носителю по <see cref="DutyCyberGrantComponent"/> —
/// мед-визор, мезоны, сварочная линза, хим-анализатор, печень, терморегулятор, ночное зрение, магнитные ступни и т.д.
/// Снимаются только те компоненты, которые добавил сам имплант (см. комментарий у компонента).
/// </summary>
public sealed partial class DutyCyberGrantSystem : EntitySystem
{
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private static readonly SoundSpecifier DenySound = new SoundCollectionSpecifier("DutyCyberDeny");
    private static readonly SoundSpecifier BootSound = new SoundCollectionSpecifier("DutyCyberBoot");
    private static readonly EntProtoId BlindnessEffect = "StatusEffectBlindness";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyCyberGrantComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<DutyCyberGrantComponent, ImplantRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<DutyCyberGrantComponent, DutyCyberEmpEvent>(OnEmp);
        SubscribeLocalEvent<DutyCyberGrantComponent, DutyCyberEmpEndedEvent>(OnEmpEnded);
        SubscribeLocalEvent<DutyCyberGrantComponent, DutyCyberGrantToggleEvent>(OnToggle);
    }

    private void OnImplanted(Entity<DutyCyberGrantComponent> ent, ref ImplantImplantedEvent args)
    {
        if (ent.Comp.Enabled)
            Apply(ent, args.Implanted);
        UpdateState(ent);
    }

    private void OnRemoved(Entity<DutyCyberGrantComponent> ent, ref ImplantRemovedEvent args)
    {
        Remove(ent, args.Implanted);

        // Если на теле остался второй такой же имплант, его компоненты, которые мы только что сняли, надо вернуть.
        if (!TryComp<ImplantedComponent>(args.Implanted, out var implanted))
            return;

        foreach (var other in new List<EntityUid>(implanted.ImplantContainer.ContainedEntities))
        {
            if (other == ent.Owner || !TryComp<DutyCyberGrantComponent>(other, out var otherGrant) || !otherGrant.Enabled)
                continue;

            Apply((other, otherGrant), args.Implanted);
        }
    }

    private void OnEmp(Entity<DutyCyberGrantComponent> ent, ref DutyCyberEmpEvent args)
    {
        if (!TryComp<DutyCyberwareComponent>(ent, out var cyber) || !cyber.EmpSensitive)
            return;

        Remove(ent, args.Host);

        if (ent.Comp.EmpBlind > TimeSpan.Zero)
            _status.TryAddStatusEffectDuration(args.Host, BlindnessEffect, ent.Comp.EmpBlind);
    }

    private void OnEmpEnded(Entity<DutyCyberGrantComponent> ent, ref DutyCyberEmpEndedEvent args)
    {
        if (ent.Comp.Enabled)
            Apply(ent, args.Host);
    }

    private void OnToggle(Entity<DutyCyberGrantComponent> ent, ref DutyCyberGrantToggleEvent args)
    {
        if (args.Handled || !ent.Comp.Toggleable)
            return;

        args.Handled = true;

        // Offline здесь — это «выключен вручную», его и переключаем; отказ только по выгоранию и ЭМИ.
        if (!_cyber.TryGetHost(ent.Owner, out var host) || _cyber.IsHostEmped(host) ||
            TryComp<DutyCyberwareComponent>(ent, out var cyberState) && cyberState.State == DutyCyberModuleState.Burned)
        {
            _popup.PopupEntity(Loc.GetString("duty-crew-offline"), args.Performer, args.Performer, PopupType.SmallCaution);
            _audio.PlayPvs(DenySound, args.Performer);
            return;
        }

        ent.Comp.Enabled = !ent.Comp.Enabled;
        if (ent.Comp.Enabled)
            Apply(ent, host);
        else
            Remove(ent, host);

        UpdateState(ent);
        _audio.PlayPvs(BootSound, args.Performer);
    }

    private void UpdateState(Entity<DutyCyberGrantComponent> ent)
    {
        if (!ent.Comp.Toggleable)
            return;

        _cyber.SetModuleState(ent.Owner, ent.Comp.Enabled ? DutyCyberModuleState.Online : DutyCyberModuleState.Offline);
    }

    private void Apply(Entity<DutyCyberGrantComponent> ent, EntityUid host)
    {
        var changed = false;
        foreach (var (name, entry) in ent.Comp.Components)
        {
            if (ent.Comp.Added.Contains(name))
                continue;

            var type = _factory.GetRegistration(name).Type;
            if (HasComp(host, type))
                continue;

            EntityManager.AddComponents(host, new ComponentRegistry { [name] = entry }, false);
            ent.Comp.Added.Add(name);
            changed = true;
        }

        if (changed)
        {
            var ev = new DutyCyberGrantChangedEvent(host, true);
            RaiseLocalEvent(ent, ref ev);
        }
    }

    private void Remove(Entity<DutyCyberGrantComponent> ent, EntityUid host)
    {
        if (ent.Comp.Added.Count == 0)
            return;

        foreach (var name in ent.Comp.Added)
        {
            var type = _factory.GetRegistration(name).Type;
            if (HasComp(host, type))
                RemComp(host, type);
        }

        ent.Comp.Added.Clear();

        var ev = new DutyCyberGrantChangedEvent(host, false);
        RaiseLocalEvent(ent, ref ev);
    }
}
