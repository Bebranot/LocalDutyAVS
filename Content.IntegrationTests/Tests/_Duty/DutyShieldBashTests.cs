// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Duty.ShieldBash;
using Content.Shared._Duty.ShieldBash;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: тесты способности «Удар по щиту» (ShieldBash): гейт «щит + оружие в разных руках»,
/// эффекты баффа (резист, скорость, игнор замедления от урона, динамический бонус ближнего боя),
/// досрочное снятие при потере последнего щита, личный кулдаун и истечение по таймеру.
/// </summary>
[TestFixture]
[TestOf(typeof(ShieldBashSystem))]
public sealed class DutyShieldBashTests : GameTest
{
    [SidedDependency(Side.Server)] private readonly SharedHandsSystem _hands = null!;
    [SidedDependency(Side.Server)] private readonly SharedActionsSystem _actions = null!;
    [SidedDependency(Side.Server)] private readonly DamageableSystem _damageable = null!;
    [SidedDependency(Side.Server)] private readonly SharedMeleeWeaponSystem _melee = null!;
    [SidedDependency(Side.Server)] private readonly IGameTiming _timing = null!;

    private const string ShieldProto = "RiotShield";
    private const string KnifeProto = "CombatKnife";
    private const string SecondKnifeProto = "KitchenKnife";
    private const string GunProto = "WeaponPistolMk58";
    private const string NonMeleeProto = "Paper";

    /// <summary>
    /// Action выдаётся только когда в одной руке щит, а в другой — настоящее оружие
    /// (огнестрел или ближний бой с базовым уроном >= MinMeleeDamage).
    /// </summary>
    [Test]
    public async Task GateRequiresShieldAndQualifyingWeapon()
    {
        var map = await Pair.CreateTestMap();
        var coords = map.GridCoords;

        await Server.WaitAssertion(() =>
        {
            var human = SEntMan.SpawnEntity("MobHuman", coords);
            var shield = SEntMan.SpawnEntity(ShieldProto, coords);
            var weakWeapon = SEntMan.SpawnEntity(KnifeProto, coords);
            var knife = SEntMan.SpawnEntity(KnifeProto, coords);
            var gun = SEntMan.SpawnEntity(GunProto, coords);

            // Опускаем урон первого ножа ниже порога MinMeleeDamage (10), чтобы проверить отсечение.
            var weakMelee = SEntMan.GetComponent<MeleeWeaponComponent>(weakWeapon);
            weakMelee.Damage = new DamageSpecifier { DamageDict = { ["Slash"] = 5 } };

            Assert.That(_hands.TryPickupAnyHand(human, shield), Is.True);
            var shieldComp = SEntMan.GetComponent<ShieldBashComponent>(shield);
            Assert.That(shieldComp.ActionEntity, Is.Null, "Без оружия в другой руке Action выдаваться не должен.");

            Assert.That(_hands.TryPickupAnyHand(human, weakWeapon), Is.True);
            Assert.That(shieldComp.ActionEntity, Is.Null, "Оружие с уроном < MinMeleeDamage не должно открывать Action.");
            Assert.That(_hands.TryDrop(human, weakWeapon), Is.True);

            Assert.That(_hands.TryPickupAnyHand(human, knife), Is.True);
            Assert.That(shieldComp.ActionEntity, Is.Not.Null, "Нож с уроном >= MinMeleeDamage должен выдавать Action.");
            Assert.That(SEntMan.HasComponent<ShieldBasherComponent>(human), Is.True);
            Assert.That(_hands.TryDrop(human, knife), Is.True);
            Assert.That(shieldComp.ActionEntity, Is.Null, "После сброса оружия Action должен сниматься.");

            Assert.That(_hands.TryPickupAnyHand(human, gun), Is.True);
            Assert.That(shieldComp.ActionEntity, Is.Not.Null, "Огнестрел в свободной руке должен выдавать Action.");
        });
    }

    /// <summary>
    /// Активация вешает бафф: резист -15% к урону, ускорение +5%, IgnoreSlowOnDamage
    /// (который снимается после баффа, если его не было изначально, и остаётся, если был).
    /// </summary>
    [Test]
    public async Task ActivationAppliesBuffResistSpeedAndIgnoreSlow()
    {
        var human = await Spawn("MobHuman");
        var shield = await Spawn(ShieldProto);
        var knife = await Spawn(KnifeProto);

        await Server.WaitAssertion(() =>
        {
            var baseSpeed = SEntMan.GetComponent<MovementSpeedModifierComponent>(human).CurrentWalkSpeed;

            Assert.That(_hands.TryPickupAnyHand(human, shield), Is.True);
            Assert.That(_hands.TryPickupAnyHand(human, knife), Is.True);

            var shieldComp = SEntMan.GetComponent<ShieldBashComponent>(shield);
            var actionEnt = shieldComp.ActionEntity!.Value;
            var actionComp = SEntMan.GetComponent<ActionComponent>(actionEnt);

            _actions.PerformAction(human, (actionEnt, actionComp), predicted: false);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.TryGetComponent<ShieldBashBuffComponent>(human, out var buff), Is.True, "Бафф не повесился.");
                Assert.That(buff!.EndTime - _timing.CurTime, Is.InRange(shieldComp.MinBuffDuration, shieldComp.MaxBuffDuration));
                Assert.That(SEntMan.HasComponent<IgnoreSlowOnDamageComponent>(human), Is.True, "Не повесился IgnoreSlowOnDamage.");
                Assert.That(buff.AddedIgnoreSlowOnDamage, Is.True);
                Assert.That(SEntMan.GetComponent<MovementSpeedModifierComponent>(human).CurrentWalkSpeed,
                    Is.EqualTo(baseSpeed * shieldComp.SpeedModifier).Within(0.01f),
                    "Скорость передвижения не увеличилась на 5%.");
            });

            _damageable.TryChangeDamage(human, new DamageSpecifier { DamageDict = { ["Blunt"] = 20 } }, out var dealt, ignoreResistances: false);
            Assert.That(dealt.GetTotal().Float(), Is.EqualTo(17f).Within(0.1f),
                "Бафф должен резать входящий урон на 15% (20 -> 17).");

            // Если IgnoreSlowOnDamage уже был на сущности (трейт/броня), снятие баффа его не трогает.
            SEntMan.RemoveComponent<ShieldBashBuffComponent>(human);
            Assert.That(SEntMan.HasComponent<IgnoreSlowOnDamageComponent>(human), Is.False);

            SEntMan.AddComponent<IgnoreSlowOnDamageComponent>(human);
            SEntMan.GetComponent<ShieldBasherComponent>(human).NextBashTime = TimeSpan.Zero;
            _actions.PerformAction(human, (actionEnt, actionComp), predicted: false);
            Assert.That(SEntMan.GetComponent<ShieldBashBuffComponent>(human).AddedIgnoreSlowOnDamage, Is.False);

            SEntMan.RemoveComponent<ShieldBashBuffComponent>(human);
            Assert.That(SEntMan.HasComponent<IgnoreSlowOnDamageComponent>(human), Is.True,
                "Чужой IgnoreSlowOnDamage не должен сниматься при окончании баффа.");
        });
    }

    /// <summary>
    /// Бонус урона и скорости атаки (+20%) динамически вешается на оружие ближнего боя в свободной руке
    /// и перевешивается при смене оружия.
    /// </summary>
    [Test]
    public async Task MeleeBonusAppliesAndFollowsWeaponInFreeHand()
    {
        var map = await Pair.CreateTestMap();
        var coords = map.GridCoords;

        await Server.WaitAssertion(() =>
        {
            var human = SEntMan.SpawnEntity("MobHuman", coords);
            var shield = SEntMan.SpawnEntity(ShieldProto, coords);
            var knife1 = SEntMan.SpawnEntity(KnifeProto, coords);
            var knife2 = SEntMan.SpawnEntity(SecondKnifeProto, coords);
            var nonMelee = SEntMan.SpawnEntity(NonMeleeProto, coords);

            var baseDamage = _melee.GetDamage(knife1, human).GetTotal().Float();
            var baseRate = _melee.GetAttackRate(knife1, human);

            Assert.That(_hands.TryPickupAnyHand(human, shield), Is.True);
            Assert.That(_hands.TryPickupAnyHand(human, knife1), Is.True);

            var shieldComp = SEntMan.GetComponent<ShieldBashComponent>(shield);
            var actionEnt = shieldComp.ActionEntity!.Value;
            _actions.PerformAction(human, (actionEnt, SEntMan.GetComponent<ActionComponent>(actionEnt)), predicted: false);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<ShieldBashMeleeBonusComponent>(knife1), Is.True);
                Assert.That(_melee.GetDamage(knife1, human).GetTotal().Float(),
                    Is.EqualTo(baseDamage * shieldComp.MeleeDamageMultiplier).Within(0.05f));
                Assert.That(_melee.GetAttackRate(knife1, human),
                    Is.EqualTo(baseRate * shieldComp.MeleeAttackRateMultiplier).Within(0.05f));
            });

            // Меняем оружие в свободной руке на второй нож — бонус переезжает на него.
            Assert.That(_hands.TryDrop(human, knife1), Is.True);
            Assert.That(SEntMan.HasComponent<ShieldBashMeleeBonusComponent>(knife1), Is.False, "Со сброшенного оружия бонус не снялся.");

            Assert.That(_hands.TryPickupAnyHand(human, knife2), Is.True);
            Assert.That(SEntMan.HasComponent<ShieldBashMeleeBonusComponent>(knife2), Is.True, "На новое оружие бонус не повесился.");

            // Меняем на предмет без MeleeWeaponComponent — бонус снимается.
            Assert.That(_hands.TryDrop(human, knife2), Is.True);
            Assert.That(_hands.TryPickupAnyHand(human, nonMelee), Is.True);
            Assert.That(SEntMan.HasComponent<ShieldBashMeleeBonusComponent>(knife2), Is.False);
            Assert.That(SEntMan.HasComponent<ShieldBashMeleeBonusComponent>(nonMelee), Is.False);
        });
    }

    /// <summary>Если убрать последний щит из рук, активный бафф и бонус на оружии снимаются немедленно.</summary>
    [Test]
    public async Task DroppingLastShieldRemovesBuffAndMeleeBonus()
    {
        var map = await Pair.CreateTestMap();
        var coords = map.GridCoords;

        await Server.WaitAssertion(() =>
        {
            var human = SEntMan.SpawnEntity("MobHuman", coords);
            var shield = SEntMan.SpawnEntity(ShieldProto, coords);
            var knife = SEntMan.SpawnEntity(KnifeProto, coords);

            Assert.That(_hands.TryPickupAnyHand(human, shield), Is.True);
            Assert.That(_hands.TryPickupAnyHand(human, knife), Is.True);

            var actionEnt = SEntMan.GetComponent<ShieldBashComponent>(shield).ActionEntity!.Value;
            _actions.PerformAction(human, (actionEnt, SEntMan.GetComponent<ActionComponent>(actionEnt)), predicted: false);

            Assert.That(SEntMan.HasComponent<ShieldBashBuffComponent>(human), Is.True);
            Assert.That(SEntMan.HasComponent<ShieldBashMeleeBonusComponent>(knife), Is.True);

            Assert.That(_hands.TryDrop(human, shield), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<ShieldBashBuffComponent>(human), Is.False, "Бафф не снялся при потере щита.");
                Assert.That(SEntMan.HasComponent<ShieldBasherComponent>(human), Is.False);
                Assert.That(SEntMan.HasComponent<ShieldBashMeleeBonusComponent>(knife), Is.False, "Бонус на оружии не снялся.");
                Assert.That(SEntMan.HasComponent<IgnoreSlowOnDamageComponent>(human), Is.False);
            });
        });
    }

    /// <summary>
    /// Личный кулдаун хранится на ShieldBasherComponent и восстанавливается при перевыдаче Action
    /// (при сбросе/взятии оружия или смене щита, пока в руках остаётся хотя бы один щит).
    /// </summary>
    [Test]
    public async Task PersonalCooldownSurvivesWeaponAndShieldSwap()
    {
        var map = await Pair.CreateTestMap();
        var coords = map.GridCoords;

        await Server.WaitAssertion(() =>
        {
            var human = SEntMan.SpawnEntity("MobHuman", coords);
            var shield1 = SEntMan.SpawnEntity(ShieldProto, coords);
            var shield2 = SEntMan.SpawnEntity(ShieldProto, coords);
            var knife = SEntMan.SpawnEntity(KnifeProto, coords);

            Assert.That(_hands.TryPickupAnyHand(human, shield1), Is.True);
            Assert.That(_hands.TryPickupAnyHand(human, knife), Is.True);

            var shieldComp1 = SEntMan.GetComponent<ShieldBashComponent>(shield1);
            var action1 = shieldComp1.ActionEntity!.Value;
            _actions.PerformAction(human, (action1, SEntMan.GetComponent<ActionComponent>(action1)), predicted: false);

            var expectedNextTime = SEntMan.GetComponent<ShieldBasherComponent>(human).NextBashTime;
            Assert.That(expectedNextTime, Is.GreaterThan(_timing.CurTime));

            // Убираем нож -> берём второй щит -> убираем первый щит -> возвращаем нож.
            Assert.That(_hands.TryDrop(human, knife), Is.True);
            Assert.That(_hands.TryPickupAnyHand(human, shield2), Is.True);
            Assert.That(_hands.TryDrop(human, shield1), Is.True);
            Assert.That(_hands.TryPickupAnyHand(human, knife), Is.True);

            var shieldComp2 = SEntMan.GetComponent<ShieldBashComponent>(shield2);
            Assert.That(shieldComp2.ActionEntity, Is.Not.Null, "Action должен перевыдаться от нового щита.");

            var actionComp2 = SEntMan.GetComponent<ActionComponent>(shieldComp2.ActionEntity!.Value);
            Assert.That(actionComp2.Cooldown?.End, Is.EqualTo(expectedNextTime),
                "Личный кулдаун не восстановился на пересозданном Action.");
        });
    }

    /// <summary>По истечении EndTime серверный Update снимает бафф и возвращает параметры в норму.</summary>
    [Test]
    public async Task BuffExpiresByTimer()
    {
        var human = await Spawn("MobHuman");
        var shield = await Spawn(ShieldProto);
        var knife = await Spawn(KnifeProto);

        var baseSpeed = 0f;
        await Server.WaitPost(() =>
        {
            baseSpeed = SEntMan.GetComponent<MovementSpeedModifierComponent>(human).CurrentWalkSpeed;

            var shieldComp = SEntMan.GetComponent<ShieldBashComponent>(shield);
            shieldComp.MinBuffDuration = TimeSpan.FromSeconds(0.4);
            shieldComp.MaxBuffDuration = TimeSpan.FromSeconds(0.4);

            Assert.That(_hands.TryPickupAnyHand(human, shield), Is.True);
            Assert.That(_hands.TryPickupAnyHand(human, knife), Is.True);

            var actionEnt = shieldComp.ActionEntity!.Value;
            _actions.PerformAction(human, (actionEnt, SEntMan.GetComponent<ActionComponent>(actionEnt)), predicted: false);
            Assert.That(SEntMan.HasComponent<ShieldBashBuffComponent>(human), Is.True);
        });

        await RunSeconds(0.6f);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<ShieldBashBuffComponent>(human), Is.False, "Бафф не истёк по таймеру.");
                Assert.That(SEntMan.HasComponent<ShieldBashMeleeBonusComponent>(knife), Is.False);
                Assert.That(SEntMan.HasComponent<IgnoreSlowOnDamageComponent>(human), Is.False);
                Assert.That(SEntMan.GetComponent<MovementSpeedModifierComponent>(human).CurrentWalkSpeed,
                    Is.EqualTo(baseSpeed).Within(0.01f));
            });
        });
    }

    /// <summary>Повторная активация на кулдауне или без оружия в другой руке отклоняется.</summary>
    [Test]
    public async Task CannotActivateOnCooldownOrWithoutWeapon()
    {
        var human = await Spawn("MobHuman");
        var shield = await Spawn(ShieldProto);
        var knife = await Spawn(KnifeProto);

        await Server.WaitAssertion(() =>
        {
            Assert.That(_hands.TryPickupAnyHand(human, shield), Is.True);

            // Прямой вызов без оружия в руке — отказ.
            var noWeaponEv = new ShieldBashActionEvent { Performer = human };
            SEntMan.EventBus.RaiseLocalEvent(human, noWeaponEv, broadcast: true);
            Assert.That(noWeaponEv.Handled, Is.False);
            Assert.That(SEntMan.HasComponent<ShieldBashBuffComponent>(human), Is.False);

            // С оружием — первый раз проходит, второй раз на кулдауне — отказ.
            Assert.That(_hands.TryPickupAnyHand(human, knife), Is.True);
            var firstEv = new ShieldBashActionEvent { Performer = human };
            SEntMan.EventBus.RaiseLocalEvent(human, firstEv, broadcast: true);
            Assert.That(firstEv.Handled, Is.True);

            var onCooldownEv = new ShieldBashActionEvent { Performer = human };
            SEntMan.EventBus.RaiseLocalEvent(human, onCooldownEv, broadcast: true);
            Assert.That(onCooldownEv.Handled, Is.False);
        });
    }
}
