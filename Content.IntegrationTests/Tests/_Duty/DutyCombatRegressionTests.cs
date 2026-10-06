// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._Duty.Aiming;
using Content.Shared._Duty.Block;
using Content.Shared._Duty.Block.Components;
using Content.Shared._Duty.Stalker;
using Content.Shared._Duty.Trauma;
using Content.Shared._Duty.Trauma.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: боевые механики, которые молча не работали из-за неверной подписки на событие.
///
/// Все три случая выглядели в коде правильно и компилировались, но обработчик никогда не
/// вызывался: широковещательная подписка на событие, которое движок поднимает только directed
/// (AttemptShootEvent, GetMeleeDamageEvent), или отмена любого нокдауна вместо добровольного.
/// Тесты поднимают событие ровно так, как это делает ванилла, — проверяется сама проводка.
/// </summary>
[TestFixture]
[TestOf(typeof(BlockSystem))]
public sealed class DutyCombatRegressionTests : GameTest
{
    [SidedDependency(Side.Server)] private readonly SharedMeleeWeaponSystem _melee = null!;
    [SidedDependency(Side.Server)] private readonly SharedStunSystem _stun = null!;
    [SidedDependency(Side.Server)] private readonly SharedHandsSystem _hands = null!;
    [SidedDependency(Side.Server)] private readonly SharedWieldableSystem _wieldable = null!;

    private const string GunProto = "WeaponPistolMk58";

    /// <summary>Пока висит любой из блок-локов, ганы не должны стрелять.</summary>
    [Test]
    [TestCase(typeof(BlockComponent))]
    [TestCase(typeof(BlockAttackLockComponent))]
    [TestCase(typeof(BlockGunLockComponent))]
    [TestCase(typeof(BlockPunishStunComponent))]
    public async Task BlockLocksPreventShooting(Type lockType)
    {
        var human = await Spawn("MobHuman");
        var gun = await Spawn(GunProto);

        await Server.WaitAssertion(() =>
        {
            var gunComp = SEntMan.GetComponent<GunComponent>(gun);

            var free = new ShotAttemptedEvent { User = human, Used = (gun, gunComp) };
            SEntMan.EventBus.RaiseLocalEvent(human, ref free);
            Assert.That(free.Cancelled, Is.False, "Без блок-локов выстрел отменяться не должен.");

            var comp = SEntMan.ComponentFactory.GetComponent(lockType);
            SEntMan.AddComponent(human, comp);

            // ShotAttemptedEvent ванилла поднимает и на оружии, и на стрелке (SharedGunSystem.AttemptShoot).
            var locked = new ShotAttemptedEvent { User = human, Used = (gun, gunComp) };
            SEntMan.EventBus.RaiseLocalEvent(human, ref locked);
            Assert.That(locked.Cancelled, Is.True, $"{lockType.Name} не запретил стрельбу.");
        });
    }

    /// <summary>Сломанная рука режет урон ближнего боя (раньше обработчик не вызывался вовсе).</summary>
    [Test]
    public async Task BrokenArmReducesMeleeDamage()
    {
        var human = await Spawn("MobHuman");

        await Server.WaitAssertion(() =>
        {
            var healthy = _melee.GetDamage(human, human).GetTotal().Float();
            Assert.That(healthy, Is.GreaterThan(0f), "У человека нет урона без оружия — тест бессмыслен.");

            var fracture = SEntMan.EnsureComponent<FractureComponent>(human);
            fracture.Zones[BodyZone.LeftArm] = new FractureZoneState { Tier = FractureTier.Full };

            var broken = _melee.GetDamage(human, human).GetTotal().Float();
            Assert.That(broken, Is.LessThan(healthy), "Перелом руки не уменьшил урон ближнего боя.");
        });
    }

    /// <summary>
    /// Прицеливание стоя запрещает только самому лечь. Внешний нокдаун (толчок, взрыв) должен
    /// проходить и сбивать прицел — раньше прицеливающийся был к нему неуязвим.
    /// </summary>
    [Test]
    public async Task AimingDoesNotBlockExternalKnockdown()
    {
        var human = await Spawn("MobHuman");
        var gun = await Spawn(GunProto);

        await Server.WaitAssertion(() =>
        {
            var aiming = SEntMan.AddComponent<AimingComponent>(human);
            aiming.Gun = gun;

            // Добровольно лечь (как ToggleKnockdown) нельзя.
            _stun.TryKnockdown(human, TimeSpan.FromSeconds(2), refresh: true, autoStand: false, drop: false);
            Assert.That(SEntMan.HasComponent<KnockedDownComponent>(human), Is.False,
                "Прицеливающийся стоя смог лечь сам.");
            Assert.That(SEntMan.HasComponent<AimingComponent>(human), Is.True);

            // Внешний нокдаун проходит и снимает прицел.
            _stun.TryKnockdown(human, TimeSpan.FromSeconds(2), refresh: true, autoStand: true, drop: false);
            Assert.That(SEntMan.HasComponent<KnockedDownComponent>(human), Is.True,
                "Внешний нокдаун не сработал на прицеливающемся.");
            Assert.That(SEntMan.HasComponent<AimingComponent>(human), Is.False,
                "Нокдаун не сбил прицеливание.");
        });
    }

    /// <summary>
    /// Сталкерский ствол с хватом в две руки стреляет точнее ровно во столько раз, сколько указано в
    /// STWeaponAccuracy (раньше компонент был пустышкой, а углы в YAML рассчитаны на делитель).
    /// </summary>
    [Test]
    public async Task StalkerAccuracyDividesSpreadWhenWielded()
    {
        var human = await Spawn("MobHuman");
        var rifle = await Spawn("STWeaponRifleAK74M");

        await Server.WaitAssertion(() =>
        {
            Assert.That(_hands.TryPickupAnyHand(human, rifle), Is.True);

            var gun = SEntMan.GetComponent<GunComponent>(rifle);
            var accuracy = SEntMan.GetComponent<STWeaponAccuracyComponent>(rifle).AccuracyMultiplier;

            Assert.That(_wieldable.TryWield(rifle, SEntMan.GetComponent<WieldableComponent>(rifle), human), Is.True);
            var withAccuracy = gun.MaxAngleModified.Theta;

            // Тот же хват, но без компонента точности: у ствола есть и свои бонусы за хват, их
            // учитываем в обоих замерах и сравниваем только вклад точности.
            SEntMan.RemoveComponent<STWeaponAccuracyComponent>(rifle);
            SEntMan.System<SharedGunSystem>().RefreshModifiers(rifle);
            var without = gun.MaxAngleModified.Theta;

            Assert.That(withAccuracy, Is.EqualTo(without / accuracy).Within(without * 0.01),
                "Хват в две руки не поделил разброс на точность ствола.");
        });
    }
}
