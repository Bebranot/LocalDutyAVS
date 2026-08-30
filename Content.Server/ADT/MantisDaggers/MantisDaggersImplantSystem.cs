using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.ADT.Implants;
using Content.Shared.ADT.MantisDaggers;
using Content.Shared.Weapons.Reflect;
using Content.Shared.Movement.Components;
using Content.Shared._Duty.SecurityImplants; // _Duty

namespace Content.Server.ADT.MantisDaggers;

public sealed class MantisDaggersImplantSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MantisDaggersImplantComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<MantisDaggersImplantComponent, ImplantRemovedEvent>(OnRemoved);
    }

    private void OnImplanted(EntityUid uid, MantisDaggersImplantComponent comp, ref ImplantImplantedEvent args)
    {
        var owner = args.Implanted;

        EnsureComp<MantisDaggersComponent>(owner);
    }

    private void OnRemoved(EntityUid uid, MantisDaggersImplantComponent comp, ref ImplantRemovedEvent args)
    {
        var owner = args.Implanted;

        // _Duty: MantisDaggersComponent общий с урезанным СБ-клоном (см.
        // DutySecMantisBladesImplantSystem) — если он ещё стоит на теле, не гасим
        // способность снятием ЭТОГО импланта.
        if (HasSiblingMantisImplant(owner))
            return;

        if (TryComp<MantisDaggersComponent>(owner, out var mantisComp))
        {
            RemComp<MantisDaggersComponent>(owner);

            if (TryComp<ReflectComponent>(owner, out var reflectComp))
            {
                RemComp<ReflectComponent>(owner);
            }
        }
    }

    // _Duty: true, если на теле остался Duty-клон Клинков Богомола. На момент вызова
    // OnRemoved снимаемый имплант уже убран из ImplantContainer (EntGotRemovedFromContainerMessage
    // приходит постфактум), поэтому просто ищем среди оставшихся.
    private bool HasSiblingMantisImplant(EntityUid owner)
    {
        if (!TryComp<ImplantedComponent>(owner, out var implanted))
            return false;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (HasComp<DutySecMantisBladesImplantComponent>(implant))
                return true;
        }

        return false;
    }
}



