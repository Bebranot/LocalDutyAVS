using Content.Shared.Vehicle.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared._RMC14.Vehicle;

public sealed class VehicleWeaponSupportSystem : EntitySystem
{
    [Dependency] private readonly VehicleTopologySystem _topology = default!;

    public override void Initialize()
    {
        // _Duty: пара (GunComponent, GunRefreshModifiersEvent) уже занята SharedDutyRecoilSystem —
        // вторая directed-подписка роняет сервер. Оружие техники всегда хардпоинт, вешаемся на него.
        SubscribeLocalEvent<HardpointItemComponent, GunRefreshModifiersEvent>(OnGunRefresh);
    }

    private void OnGunRefresh(Entity<HardpointItemComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (!_topology.TryGetVehicle(ent.Owner, out var vehicle))
            return;

        if (!TryComp(vehicle, out VehicleWeaponSupportModifierComponent? mods))
            return;

        args.FireRate *= mods.FireRateMultiplier;

        // _Duty: у нас нет GetWeaponAccuracyEvent RMC — точность СУО выражаем через разброс.
        var accuracy = (float) mods.AccuracyMultiplier;
        if (accuracy > 0f)
        {
            args.MinAngle = new Angle(args.MinAngle.Theta / accuracy);
            args.MaxAngle = new Angle(args.MaxAngle.Theta / accuracy);
        }
    }
}
