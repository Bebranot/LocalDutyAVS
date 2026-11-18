// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Duty.Block;
using Content.Shared._Duty.Block.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands;
using Content.Shared.Input;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: тесты активного блока (Block): клиент-серверный ввод и синхронизация окна,
/// гейты оружия (двуручное wielded / unwielded / одноручное / без оружия), полный и ослабленный
/// уровни, наказание атакующего и восстановление движения (регрессия cca86ca2eb), кулдауны
/// (промах/попадание/повторная активация, регрессия facd4eefa1), досрочная отмена и штраф за огнестрел.
/// </summary>
[TestFixture]
[TestOf(typeof(BlockSystem))]
public sealed class DutyBlockTests : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private const string HalberdProto = "DutyBSOHalberd";
    private const string KnifeProto = "CombatKnife";
    private const string ShotgunProto = "WeaponShotgunKammerer";
    private const string NonWeaponProto = "Paper";

    /// <summary>
    /// Регрессия cccc854286: реальное нажатие Block открывает окно на сервере и синхронизирует
    /// Weapon/EndTime/FullTier на клиент. Не-оружие и несжатая двуручка не открывают блок;
    /// одноручное оружие даёт ослабленный уровень (FullTier == false).
    /// </summary>
    [Test]
    public async Task InputGatesAndClientSync()
    {
        // Предмет без MeleeWeaponComponent — блок недоступен.
        await PlaceInHands(NonWeaponProto);
        await PressKey(ContentKeyFunctions.Block);
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<BlockComponent>(SPlayer), Is.False,
                "Предмет без MeleeWeaponComponent не должен ставить блок.");
        });

        // Двуручное оружие, но не сжато двумя руками (Wielded == false) — отказ.
        var halberd = SEntMan.GetEntity(await PlaceInHands(HalberdProto));
        await RunTicks(3);
        await PressKey(ContentKeyFunctions.Block);
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<BlockComponent>(SPlayer), Is.False,
                "Несжатое двуручное оружие не должно ставить блок.");
        });

        // Сжимаем двумя руками -> полный уровень блока, синхронизируется на клиент.
        await Server.WaitPost(() =>
        {
            var wieldable = SEntMan.GetComponent<WieldableComponent>(halberd);
            Assert.That(SEntMan.System<SharedWieldableSystem>().TryWield((halberd, wieldable), SPlayer), Is.True);
        });
        await RunTicks(3);
        await PressKey(ContentKeyFunctions.Block);
        await RunTicks(5);

        TimeSpan serverEnd = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<BlockComponent>(SPlayer, out var block), Is.True,
                "Сжатое двуручное оружие должно открывать окно блока.");
            Assert.Multiple(() =>
            {
                Assert.That(block!.FullTier, Is.True);
                Assert.That(block.Weapon, Is.EqualTo(halberd));
                Assert.That(block.EndTime, Is.GreaterThan(Server.ResolveDependency<IGameTiming>().CurTime));
                Assert.That(SEntMan.HasComponent<BlockWeaponComponent>(halberd), Is.True);
            });
            serverEnd = block!.EndTime;
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.TryGetComponent<BlockComponent>(CPlayer, out var clientBlock), Is.True,
                "Окно блока не синхронизировалось на клиент.");
            Assert.Multiple(() =>
            {
                Assert.That(clientBlock!.FullTier, Is.True);
                Assert.That(clientBlock.EndTime, Is.EqualTo(serverEnd));
            });
        });

        // Ждём закрытия окна и окончания кулдауна промаха (1.5с), затем проверяем одноручное (FullTier == false).
        await RunSeconds(2.7f);
        var knife = SEntMan.GetEntity(await PlaceInHands(KnifeProto));
        await RunTicks(3);
        await PressKey(ContentKeyFunctions.Block);
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<BlockComponent>(SPlayer, out var block), Is.True,
                "Одноручное оружие должно открывать ослабленный блок.");
            Assert.Multiple(() =>
            {
                Assert.That(block!.FullTier, Is.False);
                Assert.That(block.Weapon, Is.EqualTo(knife));
            });
        });
    }

    /// <summary>
    /// Полный уровень: удар <= урона оружия блокирующего полностью гасится (0 урона),
    /// удар > урона оружия протекает на 20%; в обоих случаях атакующий получает 1с стан.
    /// </summary>
    [Test]
    public async Task FullTierNegatesOrLeaksAndPunishStunsAttacker()
    {
        var halberd = SEntMan.GetEntity(await PlaceInHands(HalberdProto));
        var attacker = EntityUid.Invalid;
        var attackerWeapon = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            var wieldable = SEntMan.GetComponent<WieldableComponent>(halberd);
            Assert.That(SEntMan.System<SharedWieldableSystem>().TryWield((halberd, wieldable), SPlayer), Is.True);

            attacker = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(1f, 0f)));
            attackerWeapon = SEntMan.SpawnEntity(KnifeProto, MapData.GridCoords.Offset(new Vector2(1f, 0f)));
        });
        await RunTicks(3);

        await PressKey(ContentKeyFunctions.Block);
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            var dmgSys = SEntMan.System<DamageableSystem>();
            var before = dmgSys.GetTotalDamage(SPlayer).Float();

            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;

            // Удар слабее/равен оружию блокирующего (10 <= урона алебарды) -> 0 урона + стан атакующего.
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, new AttackedEvent(attackerWeapon, attacker, coords));
            dmgSys.TryChangeDamage(SPlayer, new DamageSpecifier { DamageDict = { ["Slash"] = 10 } },
                out var weakHit, ignoreResistances: false, origin: attacker);

            Assert.Multiple(() =>
            {
                Assert.That(weakHit.GetTotal().Float(), Is.EqualTo(0f).Within(0.01f),
                    "Полный блок должен полностью гасить слабый/равный удар.");
                Assert.That(dmgSys.GetTotalDamage(SPlayer).Float(), Is.EqualTo(before).Within(0.01f));
                Assert.That(SEntMan.HasComponent<BlockPunishStunComponent>(attacker), Is.True,
                    "Атакующий должен получить BlockPunishStun.");
                Assert.That(SEntMan.GetComponent<BlockComponent>(SPlayer).HitLanded, Is.True);
            });

            // Удар сильнее оружия блокирующего (например 100) -> протечка 20% (20 урона).
            SEntMan.RemoveComponent<BlockPunishStunComponent>(attacker);
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, new AttackedEvent(attackerWeapon, attacker, coords));
            dmgSys.TryChangeDamage(SPlayer, new DamageSpecifier { DamageDict = { ["Slash"] = 100 } },
                out var heavyHit, ignoreResistances: false, origin: attacker);

            Assert.Multiple(() =>
            {
                Assert.That(heavyHit.GetTotal().Float(), Is.EqualTo(20f).Within(0.1f),
                    "На полном блоке при превосходящем ударе должно протекать ровно 20% урона.");
                Assert.That(SEntMan.HasComponent<BlockPunishStunComponent>(attacker), Is.True);
            });
        });
    }

    /// <summary>
    /// Ослабленный уровень: режет урон на 40% (множитель 0.6), не станит атакующего и
    /// гасит все удары в пределах одного окна.
    /// </summary>
    [Test]
    public async Task ReducedTierReducesMultipleHitsWithoutStun()
    {
        var knife = SEntMan.GetEntity(await PlaceInHands(KnifeProto));
        var attacker = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            attacker = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(1f, 0f)));
        });
        await RunTicks(3);

        await PressKey(ContentKeyFunctions.Block);
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            var dmgSys = SEntMan.System<DamageableSystem>();

            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;

            for (var i = 0; i < 2; i++)
            {
                SEntMan.EventBus.RaiseLocalEvent(SPlayer, new AttackedEvent(knife, attacker, coords));
                dmgSys.TryChangeDamage(SPlayer, new DamageSpecifier { DamageDict = { ["Slash"] = 10 } },
                    out var hit, ignoreResistances: false, origin: attacker);

                Assert.That(hit.GetTotal().Float(), Is.EqualTo(6f).Within(0.1f),
                    $"Удар #{i + 1} в одном окне ослабленного блока должен резаться на 40% (10 -> 6).");
            }

            Assert.That(SEntMan.HasComponent<BlockPunishStunComponent>(attacker), Is.False,
                "Ослабленный блок не должен оглушать атакующего.");
        });
    }

    /// <summary>
    /// Регрессия cca86ca2eb: во время BlockPunishStun запрещены движение, атака и взаимодействие,
    /// а после истечения 1с движение восстанавливается (раньше сущность застревала навсегда).
    /// </summary>
    [Test]
    public async Task PunishStunBlocksActionsAndRestoresMovementAfterExpiry()
    {
        var attacker = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            attacker = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(1f, 0f)));
            SEntMan.System<BlockPunishStunSystem>().ApplyPunishStun(attacker, TimeSpan.FromSeconds(1));
        });
        await RunTicks(2);

        await Server.WaitAssertion(() =>
        {
            var blocker = SEntMan.System<ActionBlockerSystem>();
            Assert.That(SEntMan.HasComponent<BlockPunishStunComponent>(attacker), Is.True);
            Assert.That(blocker.CanMove(attacker), Is.False, "Оглушённый не должен двигаться.");

            var attackEv = new AttackAttemptEvent(attacker);
            SEntMan.EventBus.RaiseLocalEvent(attacker, attackEv);
            Assert.That(attackEv.Cancelled, Is.True, "Оглушённый не должен атаковать.");

            var interactEv = new InteractionAttemptEvent(attacker, attacker);
            SEntMan.EventBus.RaiseLocalEvent(attacker, ref interactEv);
            Assert.That(interactEv.Cancelled, Is.True, "Оглушённый не должен взаимодействовать.");
        });

        await RunSeconds(1.2f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<BlockPunishStunComponent>(attacker), Is.False, "Стан не снялся по таймеру.");
            Assert.That(SEntMan.System<ActionBlockerSystem>().CanMove(attacker), Is.True,
                "После снятия BlockPunishStun движение должно восстановиться.");
        });
    }

    /// <summary>
    /// Закрытие окна вешает пост-блок лок атаки (0.2с) и кулдаун: 1.5с при промахе (d1a71b99ad)
    /// или 3.5с при попадании. Регрессия facd4eefa1: после кулдауна следующий блок ставится стабильно.
    /// </summary>
    [Test]
    public async Task CooldownsForMissAndHitAndConsecutiveBlocksWork()
    {
        await PlaceInHands(KnifeProto);
        await RunTicks(3);

        // 1. Окно без попадания -> лок атаки 0.2с + кулдаун 1.5с.
        await PressKey(ContentKeyFunctions.Block);
        await RunSeconds(1.05f);

        await Server.WaitAssertion(() =>
        {
            var now = Server.ResolveDependency<IGameTiming>().CurTime;
            Assert.That(SEntMan.HasComponent<BlockComponent>(SPlayer), Is.False, "Окно должно закрыться через 1с.");
            Assert.That(SEntMan.HasComponent<BlockAttackLockComponent>(SPlayer), Is.True, "После окна должен висеть лок атаки.");
            Assert.That(SEntMan.TryGetComponent<BlockCooldownComponent>(SPlayer, out var cd), Is.True);
            Assert.That((cd!.EndTime - now).TotalSeconds, Is.InRange(1.2, 1.6), "Кулдаун промаха должен быть 1.5с.");
        });

        // Повторное нажатие на кулдауне не открывает окно.
        await PressKey(ContentKeyFunctions.Block);
        await RunTicks(4);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<BlockComponent>(SPlayer), Is.False);
        });

        // Ждём спадения кулдауна (1.5с) -> второй блок подряд обязан открыться (facd4eefa1).
        await RunSeconds(1.6f);
        await PressKey(ContentKeyFunctions.Block);
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<BlockComponent>(SPlayer), Is.True,
                "Повторный блок после окончания кулдауна не открылся.");

            // Наносим реальный удар в это окно -> HitLanded станет true, кулдаун после закрытия станет 3.5с.
            var attacker = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(1f, 0f)));
            var weapon = SEntMan.SpawnEntity(KnifeProto, MapData.GridCoords.Offset(new Vector2(1f, 0f)));
            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, new AttackedEvent(weapon, attacker, coords));
            SEntMan.System<DamageableSystem>().TryChangeDamage(SPlayer, new DamageSpecifier { DamageDict = { ["Slash"] = 10 } },
                out _, ignoreResistances: false, origin: attacker);
        });

        await RunSeconds(1.1f);
        await Server.WaitAssertion(() =>
        {
            var now = Server.ResolveDependency<IGameTiming>().CurTime;
            Assert.That(SEntMan.TryGetComponent<BlockCooldownComponent>(SPlayer, out var cd), Is.True);
            Assert.That((cd!.EndTime - now).TotalSeconds, Is.InRange(3.1, 3.6), "Кулдаун после попадания должен быть 3.5с.");
        });
    }

    /// <summary>
    /// Досрочная отмена окна при разжатии двуручки (ItemUnwieldedEvent) или сбитии с ног (DownedEvent):
    /// окно и маркер снимаются сразу, вешается лок атаки, но кулдаун не вешается.
    /// </summary>
    [Test]
    public async Task EarlyCancelOnUnwieldOrDownedSkipsCooldown()
    {
        var halberd = SEntMan.GetEntity(await PlaceInHands(HalberdProto));
        await Server.WaitPost(() =>
        {
            var wieldable = SEntMan.GetComponent<WieldableComponent>(halberd);
            Assert.That(SEntMan.System<SharedWieldableSystem>().TryWield((halberd, wieldable), SPlayer), Is.True);
        });
        await RunTicks(3);

        // Случай 1: разжатие двуручки в окне блока.
        await PressKey(ContentKeyFunctions.Block);
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<BlockComponent>(SPlayer), Is.True);
            var wieldable = SEntMan.GetComponent<WieldableComponent>(halberd);
            Assert.That(SEntMan.System<SharedWieldableSystem>().TryUnwield((halberd, wieldable), SPlayer), Is.True);
        });
        await RunTicks(1);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<BlockComponent>(SPlayer), Is.False, "Разжатие должно отменять блок.");
                Assert.That(SEntMan.HasComponent<BlockAttackLockComponent>(SPlayer), Is.True);
                Assert.That(SEntMan.HasComponent<BlockCooldownComponent>(SPlayer), Is.False,
                    "При досрочной отмене кулдаун не вешается.");
            });
        });

        // Ждём спадения лока атаки и UseDelay на Wieldable, снова сжимаем и ставим блок, затем сбиваем с ног.
        await RunSeconds(1.1f);
        await Server.WaitPost(() =>
        {
            var wieldable = SEntMan.GetComponent<WieldableComponent>(halberd);
            Assert.That(SEntMan.System<SharedWieldableSystem>().TryWield((halberd, wieldable), SPlayer), Is.True);
        });
        await RunTicks(3);

        await PressKey(ContentKeyFunctions.Block);
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<BlockComponent>(SPlayer), Is.True);
            SEntMan.System<SharedStunSystem>().TryKnockdown(SPlayer, TimeSpan.FromSeconds(1), refresh: true, autoStand: true, drop: false);
        });
        await RunTicks(1);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<BlockComponent>(SPlayer), Is.False, "Сбитие с ног должно отменять блок.");
                Assert.That(SEntMan.HasComponent<BlockAttackLockComponent>(SPlayer), Is.True);
                Assert.That(SEntMan.HasComponent<BlockCooldownComponent>(SPlayer), Is.False,
                    "При сбитии с ног кулдаун не вешается.");
            });
        });
    }

    /// <summary>
    /// Полный блок огнестрелом вешает BlockGunLockComponent на 3с: запрещает выброс, подбор,
    /// экипировку и разэкипировку до истечения таймера.
    /// </summary>
    [Test]
    public async Task GunBlockAppliesGunLockForThreeSeconds()
    {
        var shotgun = SEntMan.GetEntity(await PlaceInHands(ShotgunProto));
        await Server.WaitPost(() =>
        {
            var wieldable = SEntMan.GetComponent<WieldableComponent>(shotgun);
            Assert.That(SEntMan.System<SharedWieldableSystem>().TryWield((shotgun, wieldable), SPlayer), Is.True);
        });
        await RunTicks(3);

        await PressKey(ContentKeyFunctions.Block);
        await RunTicks(4);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<BlockComponent>(SPlayer), Is.True);
            Assert.That(SEntMan.HasComponent<BlockGunLockComponent>(SPlayer), Is.True,
                "Блок двуручным огнестрелом должен вешать BlockGunLock.");

            var dropEv = new DropAttemptEvent();
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, dropEv);
            Assert.That(dropEv.Cancelled, Is.True, "Под GunLock нельзя бросать предмет.");

            var pickupEv = new PickupAttemptEvent(SPlayer, shotgun, false);
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, pickupEv);
            Assert.That(pickupEv.Cancelled, Is.True, "Под GunLock нельзя подбирать предметы.");

            Assert.That(SEntMan.System<InventorySystem>().TryGetSlot(SPlayer, "head", out var slot), Is.True);

            var equipEv = new IsEquippingAttemptEvent(SPlayer, SPlayer, shotgun, slot!);
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, equipEv);
            Assert.That(equipEv.Cancelled, Is.True, "Под GunLock нельзя экипировать.");

            var unequipEv = new IsUnequippingAttemptEvent(SPlayer, SPlayer, shotgun, slot!);
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, unequipEv);
            Assert.That(unequipEv.Cancelled, Is.True, "Под GunLock нельзя снимать экипировку.");
        });

        await RunSeconds(3.1f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<BlockGunLockComponent>(SPlayer), Is.False,
                "BlockGunLock должен сняться через 3с.");
        });
    }
}
