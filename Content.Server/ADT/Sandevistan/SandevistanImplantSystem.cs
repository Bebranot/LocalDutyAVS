using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.ADT.Sandevistan;
using Content.Shared.Humanoid;
using Content.Server.Humanoid;
using Content.Shared.GrabProtection;
using Content.Shared.Body;
using Content.Shared._Duty.SecurityImplants; // _Duty
using Robust.Shared;

namespace Content.Server.ADT.Sandevistan;

public sealed class SandevistanImplantSystem : EntitySystem
{
    // коммент до почина
    // [UISystemDependency] private readonly VisualBodySystem _visualBody = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SandevistanImplantComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<SandevistanImplantComponent, ImplantRemovedEvent>(OnRemoved);
    }

    private void OnImplanted(EntityUid uid, SandevistanImplantComponent comp, ref ImplantImplantedEvent args)
    {
        var owner = args.Implanted;

        EnsureComp<SandevistanUserComponent>(owner);
        EnsureComp<GrabProtectionComponent>(owner);

        // коммент до почина
        // if (!string.IsNullOrEmpty(comp.MarkingId) && TryComp<HumanoidProfileComponent>(owner, out var visualOrganMarkingsComponent))
        // {
        //     _visualBody.AddMarking(owner, comp.MarkingId, comp.MarkingColor, sync: true, forced: comp.ForcedMarking);
        // }
    }

    private void OnRemoved(EntityUid uid, SandevistanImplantComponent comp, ref ImplantRemovedEvent args)
    {
        var owner = args.Implanted;

        // _Duty: SandevistanUserComponent общий с урезанным СБ-клоном (см.
        // DutySecSandevistanImplantSystem) — если он ещё стоит на теле, не гасим
        // способность снятием ЭТОГО импланта.
        if (HasSiblingSandevistanImplant(owner))
            return;

        if (TryComp<SandevistanUserComponent>(owner, out var user))
        {
            RemComp<SandevistanUserComponent>(owner);
            RemComp<GrabProtectionComponent>(owner);

            // коммент до почина
            // if (!string.IsNullOrEmpty(comp.MarkingId) && TryComp<HumanoidProfileComponent>(owner, out var visualOrganMarkingsComponent))
            // {
            //     _visualBody.RemoveMarking(owner, comp.MarkingId, sync: true);
            // }
        }
    }

    // _Duty: true, если на теле остался Duty-клон сандевистана. OnRemoved вызывается
    // уже после того как снимаемый имплант убран из ImplantContainer.
    private bool HasSiblingSandevistanImplant(EntityUid owner)
    {
        if (!TryComp<ImplantedComponent>(owner, out var implanted))
            return false;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (HasComp<DutySecSandevistanImplantComponent>(implant))
                return true;
        }

        return false;
    }
}



