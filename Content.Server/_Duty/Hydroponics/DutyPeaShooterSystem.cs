using Content.Server.Botany.Components;
using Content.Server.Botany.Systems;
using Content.Server.Popups;
using Content.Shared.Popups;

namespace Content.Server._Duty.Hydroponics;

/// <summary>
/// _Duty: горохострел — мутация гороха (см. mutationPrototypes у "pea" в
/// Prototypes/Hydroponics/seeds.yml и Prototypes/_Duty/Entities/Objects/Specific/Hydroponics/pea_shooter.yml).
/// Пока растёт — с него собираются горошины вместо стручков (обычный харвест, отдельного кода не
/// требует). А если грядку выкопать лопатой, из неё выпадает сам горохострел-пистолет.
/// </summary>
public sealed class DutyPeaShooterSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popup = default!;

    /// <summary>PacketPrototype мутировавшего семени — так отличаем горохострел от обычного гороха,
    /// не трогая ванильный SeedData ради ещё одного маркерного поля.</summary>
    private const string PeaShooterSeedPacket = "DutyPeaShooterSeedsPacket";

    private const string PeaShooterPrototype = "DutyPeaShooter";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlantHolderComponent, PlantHolderShovelledEvent>(OnShovelled);
    }

    private void OnShovelled(Entity<PlantHolderComponent> ent, ref PlantHolderShovelledEvent args)
    {
        if (args.Seed.PacketPrototype != PeaShooterSeedPacket)
            return;

        Spawn(PeaShooterPrototype, Transform(ent).Coordinates);
        _popup.PopupEntity(Loc.GetString("duty-pea-shooter-dug-up"), ent, args.User, PopupType.Medium);
    }
}
