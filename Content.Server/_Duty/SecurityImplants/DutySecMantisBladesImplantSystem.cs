using Content.Shared._Duty.SecurityImplants;
using Content.Shared.ADT.Implants;
using Content.Shared.ADT.MantisDaggers;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Weapons.Reflect;

namespace Content.Server._Duty.SecurityImplants;

/// <summary>
/// Зеркалит Content.Server.ADT.MantisDaggers.MantisDaggersImplantSystem, но выдаёт
/// MantisDaggersComponent, настроенный на собственную (более слабую) сущность оружия
/// вместо синдикатской ADTMantisDaggers. Компонент собирается заранее и добавляется целиком —
/// значения WeaponProto/ContainerId должны быть выставлены до того, как MantisDaggersComponent
/// поймает свой MapInitEvent и заспавнит оружие, поэтому EnsureComp тут не подходит.
/// </summary>
public sealed class DutySecMantisBladesImplantSystem : EntitySystem
{
    private const string WeaponProto = "DutySecMantisBlades";
    private const string ContainerId = "DutySecMantisBladesContainer";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutySecMantisBladesImplantComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<DutySecMantisBladesImplantComponent, ImplantRemovedEvent>(OnRemoved);
    }

    private void OnImplanted(EntityUid uid, DutySecMantisBladesImplantComponent comp, ref ImplantImplantedEvent args)
    {
        var owner = args.Implanted;

        if (HasComp<MantisDaggersComponent>(owner))
            return;

        var mantis = new MantisDaggersComponent
        {
            WeaponProto = WeaponProto,
            ContainerId = ContainerId,
        };
        EntityManager.AddComponent(owner, mantis);
    }

    private void OnRemoved(EntityUid uid, DutySecMantisBladesImplantComponent comp, ref ImplantRemovedEvent args)
    {
        var owner = args.Implanted;

        // Симметрично MantisDaggersImplantSystem.OnRemoved (ADT): не гасим общий
        // MantisDaggersComponent, если на теле остался синдикатский имплант.
        if (HasSiblingMantisImplant(owner))
            return;

        if (HasComp<MantisDaggersComponent>(owner))
        {
            RemComp<MantisDaggersComponent>(owner);

            if (HasComp<ReflectComponent>(owner))
                RemComp<ReflectComponent>(owner);
        }
    }

    private bool HasSiblingMantisImplant(EntityUid owner)
    {
        if (!TryComp<ImplantedComponent>(owner, out var implanted))
            return false;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (HasComp<MantisDaggersImplantComponent>(implant))
                return true;
        }

        return false;
    }
}
