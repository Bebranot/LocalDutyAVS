// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Shared._Duty.Clothing;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: загрязнение одежды (порт Space Onyx).
///
/// Первая версия порта умела только отмывать: ни один источник грязи (кровь, брызги, лужи) не был
/// подключён, и одежда в игре не пачкалась никогда. Тесты ниже проверяют каждый источник по
/// отдельности, чтобы такое больше не проходило незамеченным.
/// </summary>
[TestFixture]
[TestOf(typeof(ClothingDirtSystem))]
public sealed class ClothingDirtTests : GameTest
{
    private const string Blood = "Blood";
    private const string Vomit = "Vomit";

    private static Solution MakeSolution(string reagent, float amount)
    {
        var solution = new Solution();
        solution.AddReagent(reagent, FixedPoint2.New(amount));
        return solution;
    }

    /// <summary>Порт ClothingDirtTest из Onyx: лимиты раствора, стирка, добавление моющего.</summary>
    [Test]
    public async Task DirtCapsAndWashes()
    {
        var clothing = await Spawn("ClothingUniformJumpsuitColorGrey");

        await Server.WaitAssertion(() =>
        {
            var dirt = SEntMan.System<ClothingDirtSystem>();
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            var source = new Solution();
            source.AddReagent(Blood, FixedPoint2.New(15));
            source.AddReagent(Vomit, FixedPoint2.New(5));

            Assert.That(dirt.TryDirtyClothing(clothing, source, FixedPoint2.New(20)), Is.True);
            Assert.That(solutions.TryGetSolution(clothing, ClothingDirtSystem.DefaultSolutionName, out _, out var stored), Is.True);
            Assert.That(stored!.GetTotalPrototypeQuantity(Blood), Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(stored.GetTotalPrototypeQuantity(Vomit), Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(SEntMan.GetComponent<ClothingDirtableComponent>(clothing).DirtColor, Is.Not.Null,
                "Грязь добавилась, но цвет пятна не выставился — визуала не будет.");

            Assert.That(dirt.TryWashClothing(clothing, new ReagentId("Water", null), FixedPoint2.New(15)), Is.True,
                "Вода не считается моющим средством — проверь CleanDirt в прототипе Water.");
            Assert.That(stored.Volume, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(SEntMan.GetComponent<ClothingDirtableComponent>(clothing).DirtColor, Is.Null);

            Assert.That(dirt.TryAddCleanerToClothing(clothing, new ReagentId("Water", null), FixedPoint2.New(1)), Is.True);
            Assert.That(stored.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(1)));
        });
    }

    /// <summary>Брызги пачкают внешний слой: жилет поверх комбинезона — пачкается жилет.</summary>
    [Test]
    public async Task SplashDirtiesOuterLayer()
    {
        var human = await Spawn("MobHuman");
        var jumpsuit = await Spawn("ClothingUniformJumpsuitColorGrey");
        var vest = await Spawn("ClothingOuterVest");
        var gloves = await Spawn("ClothingHandsGlovesColorBlack");

        await Server.WaitAssertion(() =>
        {
            var inventory = SEntMan.System<InventorySystem>();
            Assert.That(inventory.TryEquip(human, jumpsuit, "jumpsuit", force: true), Is.True);
            Assert.That(inventory.TryEquip(human, vest, "outerClothing", force: true), Is.True);
            Assert.That(inventory.TryEquip(human, gloves, "gloves", force: true), Is.True);

            var dirt = SEntMan.System<ClothingDirtSystem>();
            Assert.That(dirt.TryDirtyWornSplash(human, MakeSolution(Blood, 10), FixedPoint2.New(5)), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<ClothingDirtableComponent>(vest).DirtColor, Is.Not.Null,
                    "Внешний слой (жилет) должен запачкаться.");
                Assert.That(SEntMan.GetComponent<ClothingDirtableComponent>(gloves).DirtColor, Is.Not.Null,
                    "Перчатки закрывают кисти — брызги должны попасть и на них.");
                Assert.That(SEntMan.GetComponent<ClothingDirtableComponent>(jumpsuit).DirtColor, Is.Null,
                    "Комбинезон под жилетом закрыт — брызги до него доходить не должны.");
            });
        });
    }

    /// <summary>Кровотечение пачкает нижний слой одежды.</summary>
    [Test]
    public async Task BleedingDirtiesJumpsuit()
    {
        var human = await Spawn("MobHuman");
        var jumpsuit = await Spawn("ClothingUniformJumpsuitColorGrey");

        await Server.WaitAssertion(() =>
        {
            var inventory = SEntMan.System<InventorySystem>();
            Assert.That(inventory.TryEquip(human, jumpsuit, "jumpsuit", force: true), Is.True);

            var blood = SEntMan.System<SharedBloodstreamSystem>();
            Assert.That(blood.TryBleedOut((human, SEntMan.GetComponent<BloodstreamComponent>(human)), FixedPoint2.New(2)), Is.True);

            Assert.That(SEntMan.GetComponent<ClothingDirtableComponent>(jumpsuit).DirtColor, Is.Not.Null,
                "Кровотечение не запачкало комбинезон.");
        });
    }

    /// <summary>Шаг по луже пачкает только обувь; ползком — и остальную одежду.</summary>
    [Test]
    public async Task PuddleDirtiesShoesAndCrawlDirtiesBody()
    {
        var human = await Spawn("MobHuman");
        var jumpsuit = await Spawn("ClothingUniformJumpsuitColorGrey");
        var shoes = await Spawn("ClothingShoesColorBlack");

        await Server.WaitAssertion(() =>
        {
            var inventory = SEntMan.System<InventorySystem>();
            Assert.That(inventory.TryEquip(human, jumpsuit, "jumpsuit", force: true), Is.True);
            Assert.That(inventory.TryEquip(human, shoes, "shoes", force: true), Is.True);

            var dirt = SEntMan.System<ClothingDirtSystem>();
            var puddle = MakeSolution(Blood, 5);

            Assert.That(dirt.TryDirtyWornPuddleStep(human, puddle, FixedPoint2.New(0.25f)), Is.True);
            Assert.That(SEntMan.GetComponent<ClothingDirtableComponent>(shoes).DirtColor, Is.Not.Null,
                "Шаг по луже не запачкал обувь.");
            Assert.That(SEntMan.GetComponent<ClothingDirtableComponent>(jumpsuit).DirtColor, Is.Null,
                "Шаг по луже не должен пачкать комбинезон.");

            Assert.That(dirt.TryDirtyWornPuddleCrawl(human, puddle, FixedPoint2.New(0.25f)), Is.True);
            Assert.That(SEntMan.GetComponent<ClothingDirtableComponent>(jumpsuit).DirtColor, Is.Not.Null,
                "Ползком по луже комбинезон должен запачкаться.");
        });
    }

    /// <summary>У лужи должен быть источник грязи — иначе ходьба по лужам ничего не пачкает.</summary>
    [Test]
    public async Task PuddleIsDirtSource()
    {
        var puddle = await Spawn("Puddle");

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<SurfaceDirtSourceComponent>(puddle), Is.True);
        });
    }
}
