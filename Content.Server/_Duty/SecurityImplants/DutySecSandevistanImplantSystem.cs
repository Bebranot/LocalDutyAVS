using Content.Server.ADT.Sandevistan;
using Content.Shared._Duty.SecurityImplants;
using Content.Shared.ADT.Sandevistan;
using Content.Shared.FixedPoint;
using Content.Shared.GrabProtection;
using Content.Shared.Implants;

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

        var user = EnsureComp<SandevistanUserComponent>(owner);
        user.MovementSpeedModifier = comp.MovementSpeedModifier;
        user.AttackSpeedModifier = comp.AttackSpeedModifier;
        user.ShiftDelay = comp.ShiftDelay;
        user.Thresholds = new SortedDictionary<SandevistanState, FixedPoint2>(comp.Thresholds);

        EnsureComp<GrabProtectionComponent>(owner);
    }

    private void OnRemoved(EntityUid uid, DutySecSandevistanImplantComponent comp, ref ImplantRemovedEvent args)
    {
        var owner = args.Implanted;

        if (TryComp<SandevistanUserComponent>(owner, out _))
        {
            RemComp<SandevistanUserComponent>(owner);
            RemComp<GrabProtectionComponent>(owner);
        }
    }
}
