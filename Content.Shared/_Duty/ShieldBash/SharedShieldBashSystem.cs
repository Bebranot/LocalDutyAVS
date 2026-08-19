using Content.Shared.Alert;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Network;

namespace Content.Shared._Duty.ShieldBash;

/// <summary>
/// _Duty: общая (предсказываемая) математика баффа «Удар по щиту» —
/// резист к урону и скорость передвижения. Игнор замедления от урона и иконка-статус —
/// серверные, спрятаны под <c>_net.IsServer</c> (см. <see cref="SharedMoraleBuffSystem"/> как образец).
/// Выдача Action по гейту «щит + оружие в разных руках», наложение/снятие баффа по таймеру и
/// динамический бонус ближнего боя — в серверном <c>ShieldBashSystem</c>.
/// </summary>
public sealed class SharedShieldBashSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShieldBashBuffComponent, ComponentStartup>(OnBuffStartup);
        SubscribeLocalEvent<ShieldBashBuffComponent, ComponentShutdown>(OnBuffShutdown);
        SubscribeLocalEvent<ShieldBashBuffComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<ShieldBashBuffComponent, BeforeDamageChangedEvent>(OnBeforeDamage);

        SubscribeLocalEvent<ShieldBashMeleeBonusComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
        SubscribeLocalEvent<ShieldBashMeleeBonusComponent, GetMeleeAttackRateEvent>(OnGetMeleeAttackRate);
    }

    private void OnBuffStartup(Entity<ShieldBashBuffComponent> ent, ref ComponentStartup args)
    {
        _movement.RefreshMovementSpeedModifiers(ent);

        if (!_net.IsServer)
            return;

        if (!HasComp<IgnoreSlowOnDamageComponent>(ent))
        {
            AddComp<IgnoreSlowOnDamageComponent>(ent);
            ent.Comp.AddedIgnoreSlowOnDamage = true;
        }
    }

    private void OnBuffShutdown(Entity<ShieldBashBuffComponent> ent, ref ComponentShutdown args)
    {
        _movement.RefreshMovementSpeedModifiers(ent);

        if (!_net.IsServer)
            return;

        if (ent.Comp.AddedIgnoreSlowOnDamage)
            RemComp<IgnoreSlowOnDamageComponent>(ent);

        if (ent.Comp.BonusedWeapon is { } weapon && Exists(weapon))
            RemComp<ShieldBashMeleeBonusComponent>(weapon);

        _alerts.ClearAlert(ent.Owner, ent.Comp.Alert);
    }

    private void OnRefreshSpeed(Entity<ShieldBashBuffComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.SpeedModifier, ent.Comp.SpeedModifier);
    }

    private void OnBeforeDamage(Entity<ShieldBashBuffComponent> ent, ref BeforeDamageChangedEvent args)
    {
        args.Damage *= (FixedPoint2) (1f - ent.Comp.DamageResist);
    }

    /// <summary>
    /// +N% урона ближнего боя. Коэффициенты собираются по фактическим типам урона оружия на
    /// момент удара, чтобы бонус работал для любого набора типов без хардкода списка.
    /// </summary>
    private void OnGetMeleeDamage(Entity<ShieldBashMeleeBonusComponent> ent, ref GetMeleeDamageEvent args)
    {
        var modifierSet = new DamageModifierSet();
        foreach (var damageType in args.Damage.DamageDict.Keys)
            modifierSet.Coefficients[damageType.Id] = ent.Comp.DamageMultiplier;

        args.Modifiers.Add(modifierSet);
    }

    private void OnGetMeleeAttackRate(Entity<ShieldBashMeleeBonusComponent> ent, ref GetMeleeAttackRateEvent args)
    {
        args.Multipliers *= ent.Comp.AttackRateMultiplier;
    }

    /// <summary>
    /// Считается ли предмет «настоящим оружием» для гейта способности: суммарный базовый урон
    /// MeleeWeaponComponent не ниже порога (кулак — 5, кухонный/боевой нож — 10, отсекается только
    /// безоружный удар и совсем слабые «оружия» вроде канцелярии), либо любой огнестрел.
    /// </summary>
    public bool IsQualifyingWeapon(EntityUid item, int minMeleeDamage)
    {
        if (HasComp<GunComponent>(item))
            return true;

        if (TryComp<MeleeWeaponComponent>(item, out var melee) && melee.Damage.GetTotal() >= minMeleeDamage)
            return true;

        return false;
    }
}
