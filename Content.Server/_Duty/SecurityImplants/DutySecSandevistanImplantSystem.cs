using Content.Server.ADT.Sandevistan;
using Content.Shared._Duty.SecurityImplants;
using Content.Shared.ADT.Sandevistan;
using Content.Shared.FixedPoint;
using Content.Shared.GrabProtection;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;

namespace Content.Server._Duty.SecurityImplants;

/// <summary>
/// Зеркалит Content.Server.ADT.Sandevistan.SandevistanImplantSystem, но накатывает на
/// SandevistanUserComponent урезанные значения из DutySecSandevistanImplantComponent —
/// сама механика (SandevistanSystem: перегрев, скорость, оверлей, EMP) переиспользуется как есть.
/// </summary>
public sealed class DutySecSandevistanImplantSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutySecSandevistanImplantComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<DutySecSandevistanImplantComponent, ImplantRemovedEvent>(OnRemoved);
    }

    private void OnImplanted(EntityUid uid, DutySecSandevistanImplantComponent comp, ref ImplantImplantedEvent args)
    {
        var owner = args.Implanted;

        // _Duty: если на теле уже есть более сильная (синдикатская) версия —
        // не затираем её параметры своими урезанными; способность продолжает
        // работать на уже применённых, более сильных значениях.
        var hadSandevistan = TryComp<SandevistanUserComponent>(owner, out _);

        var user = EnsureComp<SandevistanUserComponent>(owner);
        if (!hadSandevistan)
        {
            user.MovementSpeedModifier = comp.MovementSpeedModifier;
            user.AttackSpeedModifier = comp.AttackSpeedModifier;
            user.ShiftDelay = comp.ShiftDelay;
            user.Thresholds = new SortedDictionary<SandevistanState, FixedPoint2>(comp.Thresholds);
        }

        EnsureComp<GrabProtectionComponent>(owner);
    }

    private void OnRemoved(EntityUid uid, DutySecSandevistanImplantComponent comp, ref ImplantRemovedEvent args)
    {
        var owner = args.Implanted;

        // Симметрично SandevistanImplantSystem.OnRemoved (ADT): не гасим общий
        // SandevistanUserComponent, если на теле остался синдикатский имплант.
        if (HasSiblingSandevistanImplant(owner))
            return;

        if (TryComp<SandevistanUserComponent>(owner, out _))
        {
            RemComp<SandevistanUserComponent>(owner);
            RemComp<GrabProtectionComponent>(owner);
        }
    }

    private bool HasSiblingSandevistanImplant(EntityUid owner)
    {
        if (!TryComp<ImplantedComponent>(owner, out var implanted))
            return false;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (HasComp<SandevistanImplantComponent>(implant))
                return true;
        }

        return false;
    }
}
