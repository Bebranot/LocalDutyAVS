// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Mercenary;
using Content.Shared.Implants;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Cyberware.Mercenary;

/// <summary>
/// _Duty: «Без следа» — вешает маркер на носителя (его читает Shared-форензика) и глушит шаги снятием тега.
/// Под ЭМИ маскировка слетает на минуту: следы и шаги — как у всех.
/// </summary>
public sealed partial class DutyMercTracelessServerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private DutyCyberwareSystem _cyber = default!;

    private static readonly ProtoId<TagPrototype> FootstepTag = "FootstepSound";

    private const float UpdateInterval = 1f;
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyMercTracelessImplantComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<DutyMercTracelessImplantComponent, ImplantRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<DutyMercTracelessImplantComponent, DutyCyberEmpEvent>(OnEmp);
    }

    private void OnImplanted(Entity<DutyMercTracelessImplantComponent> ent, ref ImplantImplantedEvent args)
    {
        var host = args.Implanted;
        if (HasComp<DutyMercTracelessComponent>(host))
            return;

        var comp = AddComp<DutyMercTracelessComponent>(host);
        comp.HadFootstepTag = _tags.HasTag(host, FootstepTag);
        Mute(host, comp);
        Dirty(host, comp);
    }

    private void OnRemoved(Entity<DutyMercTracelessImplantComponent> ent, ref ImplantRemovedEvent args)
    {
        var host = args.Implanted;
        if (!TryComp<DutyMercTracelessComponent>(host, out var comp))
            return;

        // Второй такой же имплант ещё в теле — следы так и не оставляем.
        if (_cyber.FindImplant<DutyMercTracelessImplantComponent>(host) != null)
            return;

        Unmute(host, comp);
        RemComp<DutyMercTracelessComponent>(host);
    }

    private void OnEmp(Entity<DutyMercTracelessImplantComponent> ent, ref DutyCyberEmpEvent args)
    {
        if (!TryComp<DutyMercTracelessComponent>(args.Host, out var comp))
            return;

        var until = _timing.CurTime + ent.Comp.EmpExposure;
        if (comp.ExposedUntil is { } current && current > until)
            until = current;

        comp.ExposedUntil = until;
        Unmute(args.Host, comp);
        Dirty(args.Host, comp);
        _cyber.SetModuleState(ent, DutyCyberModuleState.Offline, until);
    }

    private void Mute(EntityUid host, DutyMercTracelessComponent comp)
    {
        if (comp.FootstepsMuted || !comp.HadFootstepTag)
            return;

        _tags.RemoveTag(host, FootstepTag);
        comp.FootstepsMuted = true;
    }

    private void Unmute(EntityUid host, DutyMercTracelessComponent comp)
    {
        if (!comp.FootstepsMuted)
            return;

        _tags.AddTag(host, FootstepTag);
        comp.FootstepsMuted = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;
        _accumulator = 0f;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<DutyMercTracelessImplantComponent, DutyCyberwareComponent>();
        while (query.MoveNext(out var uid, out _, out var cyber))
        {
            if (cyber.State != DutyCyberModuleState.Offline || !_cyber.TryGetHost(uid, out var host))
                continue;

            if (!TryComp<DutyMercTracelessComponent>(host, out var comp) || comp.ExposedUntil is not { } until || until > now)
                continue;

            comp.ExposedUntil = null;
            Mute(host, comp);
            Dirty(host, comp);
            _cyber.SetModuleState(uid, DutyCyberModuleState.Online, null, null, cyber);
        }
    }
}
