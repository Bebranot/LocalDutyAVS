using Content.Shared._Duty.Weapons.WispBuff;
using Content.Shared.Hands;

namespace Content.Server._Duty.Weapons.WispBuff;

/// <summary>
/// _Duty: жизненный цикл баффа огонька — перевешивание бонусного урона на новое оружие при смене
/// рук и снятие бонуса при потере <see cref="WispBuffComponent"/> (возврат виспа в фонарь —
/// см. <c>ADTWispLanternSystem</c>).
/// </summary>
public sealed class WispBuffSystem : EntitySystem
{
    [Dependency] private readonly SharedWispBuffSystem _wispBuff = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WispBuffComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WispBuffComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WispBuffComponent, DidEquipHandEvent>(OnHandEquipped);
        SubscribeLocalEvent<WispBuffComponent, DidUnequipHandEvent>(OnHandUnequipped);
    }

    private void OnStartup(Entity<WispBuffComponent> ent, ref ComponentStartup args)
    {
        _wispBuff.RefreshMeleeBonus(ent);
    }

    private void OnShutdown(Entity<WispBuffComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.BonusedWeapon is { } weapon && Exists(weapon))
            RemComp<WispMeleeBonusComponent>(weapon);
    }

    private void OnHandEquipped(Entity<WispBuffComponent> ent, ref DidEquipHandEvent args)
    {
        _wispBuff.RefreshMeleeBonus(ent);
    }

    private void OnHandUnequipped(Entity<WispBuffComponent> ent, ref DidUnequipHandEvent args)
    {
        _wispBuff.RefreshMeleeBonus(ent);
    }
}
