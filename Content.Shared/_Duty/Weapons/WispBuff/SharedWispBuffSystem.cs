using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared._Duty.Weapons.WispBuff;

/// <summary>
/// _Duty: общая (предсказываемая) математика баффа огонька — резист ко входящему урону и бонусный
/// режущий урон в ближнем бою. Перевешивание бонуса на новое оружие при смене рук — серверное
/// (см. <c>WispBuffSystem</c>), сама математика удара — здесь, чтобы клиент предсказывал урон
/// одинаково с сервером (образец — <see cref="Content.Shared._Duty.ShieldBash.SharedShieldBashSystem"/>).
/// </summary>
public sealed class SharedWispBuffSystem : EntitySystem
{
    [Dependency] private readonly SharedMeleeWeaponSystem _meleeWeapon = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WispBuffComponent, BeforeDamageChangedEvent>(OnBeforeDamage);
        SubscribeLocalEvent<WispMeleeBonusComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
    }

    private void OnBeforeDamage(Entity<WispBuffComponent> ent, ref BeforeDamageChangedEvent args)
    {
        args.Damage *= (FixedPoint2) (1f - ent.Comp.DamageResist);
    }

    private void OnGetMeleeDamage(Entity<WispMeleeBonusComponent> ent, ref GetMeleeDamageEvent args)
    {
        args.Damage += ent.Comp.BonusDamage;
    }

    /// <summary>
    /// Перевешивает <see cref="WispMeleeBonusComponent"/> на текущее оружие владельца (то, что
    /// вернёт <see cref="SharedMeleeWeaponSystem.TryGetWeapon"/> — активный предмет в руке, перчатки
    /// или сам владелец при безоружном ударе). Вызывается сервером при активации баффа и при
    /// каждой смене содержимого рук.
    /// </summary>
    public void RefreshMeleeBonus(Entity<WispBuffComponent> ent)
    {
        EntityUid? weapon = _meleeWeapon.TryGetWeapon(ent.Owner, out var weaponUid, out _)
            ? weaponUid
            : null;

        if (ent.Comp.BonusedWeapon == weapon)
            return;

        if (ent.Comp.BonusedWeapon is { } old && Exists(old))
            RemComp<WispMeleeBonusComponent>(old);

        ent.Comp.BonusedWeapon = weapon;

        if (weapon is not { } newWeapon)
            return;

        var bonus = EnsureComp<WispMeleeBonusComponent>(newWeapon);
        bonus.BonusDamage = ent.Comp.MeleeBonusDamage;
        Dirty(newWeapon, bonus);
    }
}
