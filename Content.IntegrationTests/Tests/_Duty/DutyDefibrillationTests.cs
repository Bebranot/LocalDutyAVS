// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Duty.Defibrillation;
using Content.Shared.ADT.Atmos.Miasma;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Medical;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.Traits.Assorted;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: интеграционные тесты дефибрилляторов LifePak-40s / LifePak-45-A,
/// электродного геля, кардиоверсии и статусов (<see cref="SharedDutyDefibrillationSystem"/>).
/// </summary>
[TestFixture]
[TestOf(typeof(SharedDutyDefibrillationSystem))]
public sealed class DutyDefibrillationTests : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private static readonly EntProtoId LifePak40sProto = "DutyDefibrillatorLifePak40s";
    private static readonly EntProtoId LifePak45AProto = "DutyDefibrillatorLifePak45A";
    private static readonly EntProtoId GelProto = "DutyDefibGel";
    private static readonly EntProtoId HardsuitProto = "ClothingOuterHardsuitAtmos";
    private static readonly EntProtoId NonMobProto = "Crowbar";
    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";
    private static readonly ProtoId<DamageTypePrototype> AsphyxiationDamage = "Asphyxiation";
    private static readonly ProtoId<DamageTypePrototype> HeatDamage = "Heat";

    private async Task<EntityUid> SpawnDeadHuman(FixedPoint2 damageAmount)
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");

        var target = SEntMan.GetEntity(Target!.Value);
        var damageable = SEntMan.System<DamageableSystem>();
        var mobThresholds = SEntMan.System<MobThresholdSystem>();

        await Server.WaitPost(() =>
        {
            var deadThreshold = mobThresholds.GetThresholdForState(target, MobState.Dead);
            var killDamage = new DamageSpecifier(ProtoMan.Index(BluntDamage), deadThreshold);
            damageable.SetDamage(target, killDamage);
        });
        await RunTicks(3);

        await Server.WaitPost(() =>
        {
            var dmg = new DamageSpecifier(ProtoMan.Index(AsphyxiationDamage), damageAmount);
            damageable.SetDamage(target, dmg);
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<MobStateComponent>(target).CurrentState, Is.EqualTo(MobState.Dead),
                "Цель должна оставаться мёртвой перед проверкой дефибриллятора.");
        });

        return target;
    }

    private void CycleEnergy(EntityUid device, EntityUid user)
    {
        var ev = new GetVerbsEvent<AlternativeVerb>(user, device, null, null, true, true, true, new());
        SEntMan.EventBus.RaiseLocalEvent(device, ev);
        var verb = ev.Verbs.FirstOrDefault(v => v.Act != null);
        Assert.That(verb, Is.Not.Null, "У LifePak-45-A должен быть AlternativeVerb смены энергии.");
        verb!.Act!.Invoke();
    }

    private float? GetBatteryCharge(EntityUid device)
    {
        var powerCell = SEntMan.System<PowerCellSystem>();
        return powerCell.TryGetBatteryFromSlot(device, out var battery) ? battery.Value.Comp.LastCharge : null;
    }

    /// <summary>
    /// Уровни энергии LifePak-45-A: стартовый Standard, циклическое переключение
    /// Standard -> High -> Low -> Standard и запрет переключения во время активного doafter.
    /// </summary>
    [Test]
    public async Task LifePak45A_EnergyLevelsSyncAndCycle()
    {
        var device = SEntMan.GetEntity(await PlaceInHands(LifePak45AProto, enableToggleable: true));

        await Server.WaitAssertion(() =>
        {
            var profile = SEntMan.GetComponent<DutyDefibProfileComponent>(device);
            var defib = SEntMan.GetComponent<DefibrillatorComponent>(device);
            var draw = SEntMan.GetComponent<PowerCellDrawComponent>(device);

            Assert.Multiple(() =>
            {
                Assert.That(profile.CurrentEnergy, Is.EqualTo(1), "Стартовый уровень 45-A должен быть Standard (индекс 1).");
                Assert.That(defib.DoAfterDuration, Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(defib.ZapDamage, Is.EqualTo(5));
                Assert.That(draw.UseCharge, Is.EqualTo(100f));
            });

            // Standard -> High
            CycleEnergy(device, SPlayer);
            Assert.Multiple(() =>
            {
                Assert.That(profile.CurrentEnergy, Is.EqualTo(2));
                Assert.That(defib.DoAfterDuration, Is.EqualTo(TimeSpan.FromSeconds(6)));
                Assert.That(defib.ZapDamage, Is.EqualTo(10));
                Assert.That(draw.UseCharge, Is.EqualTo(180f));
            });
        });

        await RunSeconds(0.4f);

        await Server.WaitAssertion(() =>
        {
            var profile = SEntMan.GetComponent<DutyDefibProfileComponent>(device);
            var defib = SEntMan.GetComponent<DefibrillatorComponent>(device);
            var draw = SEntMan.GetComponent<PowerCellDrawComponent>(device);

            // High -> Low (кардиоверсия)
            CycleEnergy(device, SPlayer);
            Assert.Multiple(() =>
            {
                Assert.That(profile.CurrentEnergy, Is.EqualTo(0));
                Assert.That(defib.DoAfterDuration, Is.EqualTo(TimeSpan.FromSeconds(3)));
                Assert.That(defib.ZapDamage, Is.EqualTo(2));
                Assert.That(draw.UseCharge, Is.EqualTo(50f));
                Assert.That(profile.Energies[0].Cardioversion, Is.True);
            });
        });

        await RunSeconds(0.4f);

        // Пока идёт doafter, энергия не должна переключаться.
        await SpawnDeadHuman(180);
        var target = SEntMan.GetEntity(Target!.Value);

        await Server.WaitAssertion(() =>
        {
            var sharedDefib = SEntMan.System<SharedDefibrillatorSystem>();
            var started = sharedDefib.TryStartZap(device, target, SPlayer);
            Assert.That(started, Is.True);

            var profile = SEntMan.GetComponent<DutyDefibProfileComponent>(device);
            CycleEnergy(device, SPlayer);
            Assert.That(profile.CurrentEnergy, Is.EqualTo(0),
                "Смена энергии во время активного doafter должна блокироваться.");
        });
    }

    /// <summary>
    /// TryAnalyze корректно классифицирует все состояния цели без побочных эффектов.
    /// </summary>
    [Test]
    public async Task TryAnalyze_CoversAllOutcomes()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        var target = SEntMan.GetEntity(Target!.Value);
        EntityUid nonMob = default;
        await Server.WaitPost(() => nonMob = SEntMan.SpawnEntity(NonMobProto, SEntMan.GetComponent<TransformComponent>(target).Coordinates));
        var device = SEntMan.GetEntity(await PlaceInHands(LifePak45AProto, enableToggleable: true));

        await Server.WaitAssertion(() =>
        {
            var dutyDefib = SEntMan.System<SharedDutyDefibrillationSystem>();
            var profile = SEntMan.GetComponent<DutyDefibProfileComponent>(device);

            // Не-моб -> Blocked.
            Assert.That(dutyDefib.TryAnalyze((device, profile), nonMob).Kind, Is.EqualTo(DutyDefibAnalysisKind.Blocked));

            // Живой без и с аритмией.
            var alive = dutyDefib.TryAnalyze((device, profile), target);
            Assert.That(alive.Kind, Is.EqualTo(DutyDefibAnalysisKind.Alive));
            Assert.That(alive.Arrhythmia, Is.False);

            SEntMan.AddComponent<DutyArrhythmiaComponent>(target);
            var aliveArrhythmia = dutyDefib.TryAnalyze((device, profile), target);
            Assert.That(aliveArrhythmia.Kind, Is.EqualTo(DutyDefibAnalysisKind.Alive));
            Assert.That(aliveArrhythmia.Arrhythmia, Is.True);
            SEntMan.RemoveComponent<DutyArrhythmiaComponent>(target);
        });

        // Мёртвый с избыточным уроном -> NoShock с дефицитом.
        await SpawnDeadHuman(280);
        target = SEntMan.GetEntity(Target!.Value);

        await Server.WaitAssertion(() =>
        {
            var dutyDefib = SEntMan.System<SharedDutyDefibrillationSystem>();
            var profile = SEntMan.GetComponent<DutyDefibProfileComponent>(device);

            var tooDamaged = dutyDefib.TryAnalyze((device, profile), target);
            Assert.That(tooDamaged.Kind, Is.EqualTo(DutyDefibAnalysisKind.NoShock));
            Assert.That(tooDamaged.Deficit, Is.GreaterThan(FixedPoint2.Zero));

            // Бальзамированный / невоскрешаемый -> Blocked.
            SEntMan.AddComponent<EmbalmedComponent>(target);
            Assert.That(dutyDefib.TryAnalyze((device, profile), target).Kind, Is.EqualTo(DutyDefibAnalysisKind.Blocked));
            SEntMan.RemoveComponent<EmbalmedComponent>(target);

            SEntMan.AddComponent<UnrevivableComponent>(target);
            Assert.That(dutyDefib.TryAnalyze((device, profile), target).Kind, Is.EqualTo(DutyDefibAnalysisKind.Blocked));
            SEntMan.RemoveComponent<UnrevivableComponent>(target);
        });

        // Мёртвый в пределах лечения, но без игрока -> SoftBlocked, при ignorePresence -> ShockAdvised.
        await Server.WaitPost(() =>
        {
            var dmg = new DamageSpecifier(ProtoMan.Index(AsphyxiationDamage), 180);
            SEntMan.System<DamageableSystem>().SetDamage(target, dmg);
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            var dutyDefib = SEntMan.System<SharedDutyDefibrillationSystem>();
            var profile = SEntMan.GetComponent<DutyDefibProfileComponent>(device);

            Assert.That(dutyDefib.TryAnalyze((device, profile), target, ignorePresence: false).Kind,
                Is.EqualTo(DutyDefibAnalysisKind.SoftBlocked));
            Assert.That(dutyDefib.TryAnalyze((device, profile), target, ignorePresence: true).Kind,
                Is.EqualTo(DutyDefibAnalysisKind.ShockAdvised));

            // Кардиоверсия (Low) на мёртвом -> NoShock.
            CycleEnergy(device, SPlayer); // Standard -> High
            profile.NextEnergySwitch = TimeSpan.Zero;
            CycleEnergy(device, SPlayer); // High -> Low
            Assert.That(dutyDefib.TryAnalyze((device, profile), target, ignorePresence: true).Kind,
                Is.EqualTo(DutyDefibAnalysisKind.NoShock));
        });
    }

    /// <summary>
    /// Успешное оживление 45-A (принудительный повтор после SSD-предупреждения):
    /// цель выходит из Dead, получает слабость, историю ударов и ожог электродами без геля.
    /// </summary>
    [Test]
    public async Task LifePak45A_RevivesDeadTargetAndAppliesWeaknessAndBurn()
    {
        var target = await SpawnDeadHuman(210);
        var device = SEntMan.GetEntity(await PlaceInHands(LifePak45AProto, enableToggleable: true));

        var zapDelay = SEntMan.GetComponent<DefibrillatorComponent>(device).ZapDelay;
        await RunSeconds((float) zapDelay.TotalSeconds + 0.1f);

        // Первая попытка на SSD-цели получает SoftBlocked на этапе ZapLead (5с).
        await Server.WaitAssertion(() =>
        {
            var sharedDefib = SEntMan.System<SharedDefibrillatorSystem>();
            Assert.That(sharedDefib.TryStartZap(device, target, SPlayer), Is.True);
        });
        await RunSeconds(6f);

        await Server.WaitAssertion(() =>
        {
            var profile = SEntMan.GetComponent<DutyDefibProfileComponent>(device);
            Assert.That(profile.SoftWarnedTarget, Is.EqualTo(target),
                "После SSD-предупреждения цель должна запомниться для повторного разряда.");
            Assert.That(SEntMan.GetComponent<MobStateComponent>(target).CurrentState, Is.EqualTo(MobState.Dead));
        });

        // Ждём RetryCooldown (2с) и повторяем — на этот раз разряд проходит полностью (10с).
        await RunSeconds(2.2f);
        await Server.WaitAssertion(() =>
        {
            var sharedDefib = SEntMan.System<SharedDefibrillatorSystem>();
            Assert.That(sharedDefib.TryStartZap(device, target, SPlayer), Is.True);
        });
        await RunSeconds(10.5f);

        await Server.WaitAssertion(() =>
        {
            var mobState = SEntMan.GetComponent<MobStateComponent>(target).CurrentState;
            var profile = SEntMan.GetComponent<DutyDefibProfileComponent>(device);
            var damageable = SEntMan.GetComponent<DamageableComponent>(target);

            Assert.Multiple(() =>
            {
                Assert.That(mobState, Is.EqualTo(MobState.Critical).Or.EqualTo(MobState.SoftCritical),
                    "После успешного разряда цель должна выйти из Dead.");
                Assert.That(SEntMan.HasComponent<DutyRevivalWeaknessComponent>(target), Is.True,
                    "После реанимации должна вешаться слабость.");
                Assert.That(SEntMan.HasComponent<DutyDefibHistoryComponent>(target), Is.True);
                Assert.That(SEntMan.GetComponent<DutyDefibHistoryComponent>(target).Count, Is.EqualTo(1));
                Assert.That(profile.SoftWarnedTarget, Is.Null, "Разрешение на SSD должно сбрасываться после удара.");
                Assert.That(SEntMan.System<DamageableSystem>().GetAllDamage(target).DamageDict.TryGetValue(HeatDamage, out var heat) ? heat : FixedPoint2.Zero,
                    Is.InRange(FixedPoint2.New(profile.BurnMin), FixedPoint2.New(profile.BurnMax)),
                    "Без геля электроды должны оставлять термический ожог в пределах [BurnMin, BurnMax].");
            });
        });
    }

    /// <summary>
    /// Электродный гель снимает ожог от электродов, усиливает лечение и расходуется при первом ударе;
    /// поверх скафандра гель не наносится.
    /// </summary>
    [Test]
    public async Task Gel_PreventsBurnAndBlockedByHardsuit()
    {
        var target = await SpawnDeadHuman(210);
        await Server.WaitPost(() =>
        {
            var hardsuit = SEntMan.SpawnEntity(HardsuitProto, SEntMan.GetComponent<TransformComponent>(target).Coordinates);
            SEntMan.System<InventorySystem>().TryEquip(target, hardsuit, "outerClothing", silent: true, force: true);
        });

        var gel = SEntMan.GetEntity(await PlaceInHands(GelProto));
        var usesBefore = SEntMan.GetComponent<DutyDefibGelComponent>(gel).Uses;

        // В скафандре гель не наносится.
        await Interact();
        await RunSeconds(2.5f);

        await Server.WaitAssertion(() =>
        {
            var gelComp = SEntMan.GetComponent<DutyDefibGelComponent>(gel);
            Assert.That(SEntMan.HasComponent<DutyDefibGelledComponent>(target), Is.False,
                "Через скафандр гель наноситься не должен.");
            Assert.That(gelComp.Uses, Is.EqualTo(usesBefore));

            // Снимаем скафандр.
            Assert.That(SEntMan.System<InventorySystem>().TryUnequip(target, "outerClothing", silent: true, force: true), Is.True);
        });

        // Без скафандра гель наносится и расходует 1 применение.
        await Interact();
        await RunSeconds(2.5f);

        await Server.WaitAssertion(() =>
        {
            var gelComp = SEntMan.GetComponent<DutyDefibGelComponent>(gel);
            Assert.That(SEntMan.HasComponent<DutyDefibGelledComponent>(target), Is.True);
            Assert.That(gelComp.Uses, Is.EqualTo(usesBefore - 1));
        });

        // Удар с гелем: гель снимается, ожога Heat нет.
        var device = SEntMan.GetEntity(await PlaceInHands(LifePak45AProto, enableToggleable: true));
        await RunSeconds((float) SEntMan.GetComponent<DefibrillatorComponent>(device).ZapDelay.TotalSeconds + 0.1f);

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<SharedDefibrillatorSystem>().Zap(device, target, SPlayer);

            var allDamage = SEntMan.System<DamageableSystem>().GetAllDamage(target);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<DutyDefibGelledComponent>(target), Is.False,
                    "Гель должен сниматься первым же разрядом.");
                Assert.That(allDamage.DamageDict.TryGetValue(HeatDamage, out var heat) ? heat : FixedPoint2.Zero, Is.EqualTo(FixedPoint2.Zero),
                    "С гелем ожога от электродов быть не должно.");
            });
        });
    }

    /// <summary>
    /// Скафандр полностью блокирует LifePak-40s (до траты заряда), но пропускает LifePak-45-A
    /// с урезанным лечением и усиленным шоком.
    /// </summary>
    [Test]
    public async Task ChestCover_SuitBlocks40sAndReduces45A()
    {
        var target = await SpawnDeadHuman(210);
        await Server.WaitPost(() =>
        {
            var hardsuit = SEntMan.SpawnEntity(HardsuitProto, SEntMan.GetComponent<TransformComponent>(target).Coordinates);
            SEntMan.System<InventorySystem>().TryEquip(target, hardsuit, "outerClothing", silent: true, force: true);
        });

        // LifePak-40s: SuitBlocks = true.
        var dev40 = SEntMan.GetEntity(await PlaceInHands(LifePak40sProto, enableToggleable: true));
        await RunSeconds((float) SEntMan.GetComponent<DefibrillatorComponent>(dev40).ZapDelay.TotalSeconds + 0.1f);

        await Server.WaitAssertion(() =>
        {
            var dutyDefib = SEntMan.System<SharedDutyDefibrillationSystem>();
            var sharedDefib = SEntMan.System<SharedDefibrillatorSystem>();
            var profile40 = SEntMan.GetComponent<DutyDefibProfileComponent>(dev40);
            var chargeBefore = GetBatteryCharge(dev40);

            Assert.That(dutyDefib.GetChestCover(target), Is.EqualTo(DutyDefibChestCover.Block));
            Assert.That(dutyDefib.TryAnalyze((dev40, profile40), target, ignorePresence: true).Kind,
                Is.EqualTo(DutyDefibAnalysisKind.ShockAdvised));
            Assert.That(sharedDefib.CanZap(dev40, target, SPlayer), Is.False,
                "40s не должен бить сквозь скафандр.");

            // TryStartZap возвращает true (обработано профилем с отказом), но doafter и трату заряда не запускает.
            sharedDefib.TryStartZap(dev40, target, SPlayer);
            Assert.That(SEntMan.GetComponent<DoAfterComponent>(SPlayer).DoAfters.Values.Any(d => !d.Cancelled && !d.Completed),
                Is.False, "Отказ по скафандру не должен запускать doafter.");
            Assert.That(GetBatteryCharge(dev40), Is.EqualTo(chargeBefore),
                "При отказе по скафандру батарея не должна тратиться.");
        });

        // LifePak-45-A: SuitBlocks = false, лечение режется в 2 раза.
        var dev45 = SEntMan.GetEntity(await PlaceInHands(LifePak45AProto, enableToggleable: true));
        await RunSeconds((float) SEntMan.GetComponent<DefibrillatorComponent>(dev45).ZapDelay.TotalSeconds + 0.1f);

        await Server.WaitAssertion(() =>
        {
            var sharedDefib = SEntMan.System<SharedDefibrillatorSystem>();
            var damageable = SEntMan.System<DamageableSystem>();
            var profile45 = SEntMan.GetComponent<DutyDefibProfileComponent>(dev45);
            var dutyDefib = SEntMan.System<SharedDutyDefibrillationSystem>();

            Assert.That(sharedDefib.CanZap(dev45, target, SPlayer), Is.True,
                "45-A должен пробивать скафандр.");

            // При 210 Asphyxiation и базовом лечении -40 * 0.5 = -20 остаётся 190 (+ ожог), что < 200 (Dead).
            var damageBefore = damageable.GetTotalDamage(target);
            Assert.That(dutyDefib.WouldRevive((dev45, profile45), target, out _), Is.True);

            sharedDefib.Zap(dev45, target, SPlayer);
            var damageAfter = damageable.GetTotalDamage(target);
            // Базовое лечение 40 -> через скафандр 20, плюс ожог 3..5 -> снижение не более чем на 20.
            Assert.That(damageBefore - damageAfter, Is.LessThanOrEqualTo(FixedPoint2.New(20)),
                "Через скафандр лечение 45-A должно резаться вдвое.");
        });
    }

    /// <summary>
    /// Двухшаговый режим LifePak-40s: первый шаг проводит анализ без траты заряда и
    /// кеширует разрешение, второй шаг разряжает и сбрасывает кеш. На живой цели анализ отменяется.
    /// </summary>
    [Test]
    public async Task LifePak40s_TwoStepAnalysisAndRejectsAlive()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        var target = SEntMan.GetEntity(Target!.Value);
        var dev40 = SEntMan.GetEntity(await PlaceInHands(LifePak40sProto, enableToggleable: true));

        // На живой цели анализ отменяется на этапе AnalysisLead (~1.5с).
        await Server.WaitAssertion(() =>
        {
            var sharedDefib = SEntMan.System<SharedDefibrillatorSystem>();
            Assert.That(sharedDefib.TryStartZap(dev40, target, SPlayer), Is.True);
        });
        await RunSeconds(2.5f);

        await Server.WaitAssertion(() =>
        {
            var profile = SEntMan.GetComponent<DutyDefibProfileComponent>(dev40);
            Assert.That(profile.ShockAdvised, Is.False,
                "На живой цели анализ должен отменяться без разрешения на разряд.");
        });

        await RunSeconds(2.2f); // ждём RetryCooldown (2с)

        // Убиваем цель и проходим шаг 1 (анализ 4с) -> шаг 2 (разряд 15с).
        target = await SpawnDeadHuman(205);
        await RunSeconds((float) SEntMan.GetComponent<DefibrillatorComponent>(dev40).ZapDelay.TotalSeconds + 0.1f);

        float? chargeBeforeAnalysis = null;
        await Server.WaitAssertion(() =>
        {
            chargeBeforeAnalysis = GetBatteryCharge(dev40);
            Assert.That(SEntMan.System<SharedDefibrillatorSystem>().TryStartZap(dev40, target, SPlayer), Is.True);
        });
        await RunSeconds(4.5f);

        await Server.WaitAssertion(() =>
        {
            var profile = SEntMan.GetComponent<DutyDefibProfileComponent>(dev40);
            Assert.Multiple(() =>
            {
                Assert.That(profile.ShockAdvised, Is.True, "После анализа мёртвой цели (SSD) должен разрешиться повторный разряд.");
                Assert.That(profile.LastAnalyzedTarget, Is.EqualTo(target));
                Assert.That(GetBatteryCharge(dev40), Is.EqualTo(chargeBeforeAnalysis),
                    "Шаг анализа 40s не должен тратить заряд батареи.");
            });

            // Шаг 2: разряд по кешу.
            Assert.That(SEntMan.System<SharedDefibrillatorSystem>().TryStartZap(dev40, target, SPlayer), Is.True);
        });
        await RunSeconds(15.5f);

        await Server.WaitAssertion(() =>
        {
            var profile = SEntMan.GetComponent<DutyDefibProfileComponent>(dev40);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<MobStateComponent>(target).CurrentState, Is.Not.EqualTo(MobState.Dead),
                    "Второй шаг 40s должен оживить цель.");
                Assert.That(profile.ShockAdvised, Is.False, "После удара кеш анализа должен сбрасываться.");
                Assert.That(profile.LastAnalyzedTarget, Is.Null);
            });
        });
    }

    /// <summary>
    /// Кардиоверсия (Low на 45-A): снимает аритмию с живого без слабости после реанимации,
    /// отказывает на здоровом живом и на мёртвом.
    /// </summary>
    [Test]
    public async Task Cardioversion_CuresArrhythmiaAndRejectsHealthyOrDead()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        var target = SEntMan.GetEntity(Target!.Value);
        var device = SEntMan.GetEntity(await PlaceInHands(LifePak45AProto, enableToggleable: true));

        await Server.WaitAssertion(() =>
        {
            var profile = SEntMan.GetComponent<DutyDefibProfileComponent>(device);
            CycleEnergy(device, SPlayer); // Standard -> High
            profile.NextEnergySwitch = TimeSpan.Zero;
            CycleEnergy(device, SPlayer); // High -> Low (Cardioversion)
            Assert.That(profile.Energies[profile.CurrentEnergy].Cardioversion, Is.True);
        });

        await RunSeconds((float) SEntMan.GetComponent<DefibrillatorComponent>(device).ZapDelay.TotalSeconds + 0.1f);

        // Живой без аритмии -> CanZap false.
        await Server.WaitAssertion(() =>
        {
            var sharedDefib = SEntMan.System<SharedDefibrillatorSystem>();
            Assert.That(sharedDefib.CanZap(device, target, SPlayer), Is.False,
                "Кардиоверсия не должна бить живого без аритмии.");

            // Вешаем аритмию -> CanZap true, Zap снимает аритмию без слабости.
            var arr = SEntMan.AddComponent<DutyArrhythmiaComponent>(target);
            arr.EndTime = SGameTiming.CurTime + TimeSpan.FromMinutes(5);

            Assert.That(sharedDefib.CanZap(device, target, SPlayer), Is.True);
            sharedDefib.Zap(device, target, SPlayer);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<DutyArrhythmiaComponent>(target), Is.False,
                    "Кардиоверсия должна снимать аритмию.");
                Assert.That(SEntMan.HasComponent<DutyRevivalWeaknessComponent>(target), Is.False,
                    "Кардиоверсия по живому не должна вешать слабость после реанимации.");
                Assert.That(SEntMan.GetComponent<MobStateComponent>(target).CurrentState, Is.EqualTo(MobState.Alive));
            });
        });

        // Мёртвый с аритмией -> CanZap false в режиме кардиоверсии.
        target = await SpawnDeadHuman(205);
        await RunSeconds((float) SEntMan.GetComponent<DefibrillatorComponent>(device).ZapDelay.TotalSeconds + 0.1f);

        await Server.WaitAssertion(() =>
        {
            var arr = SEntMan.AddComponent<DutyArrhythmiaComponent>(target);
            arr.EndTime = SGameTiming.CurTime + TimeSpan.FromMinutes(5);
            Assert.That(SEntMan.System<SharedDefibrillatorSystem>().CanZap(device, target, SPlayer), Is.False,
                "В режиме кардиоверсии нельзя бить мёртвого.");
        });
    }

    /// <summary>
    /// Живая (без аритмии) и неподходящая (не-моб) цели не получают побочных эффектов:
    /// на живом doafter отменяется на этапе ZapLead до траты заряда и урона.
    /// </summary>
    [Test]
    public async Task AliveAndNonMobTargets_HaveNoSideEffects()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        var target = SEntMan.GetEntity(Target!.Value);
        EntityUid nonMob = default;
        await Server.WaitPost(() =>
            nonMob = SEntMan.SpawnEntity(NonMobProto, SEntMan.GetComponent<TransformComponent>(target).Coordinates));
        var device = SEntMan.GetEntity(await PlaceInHands(LifePak45AProto, enableToggleable: true));

        float? chargeBefore = null;
        await Server.WaitAssertion(() =>
        {
            var sharedDefib = SEntMan.System<SharedDefibrillatorSystem>();
            chargeBefore = GetBatteryCharge(device);

            Assert.That(sharedDefib.CanZap(device, nonMob, SPlayer), Is.False);
            Assert.That(sharedDefib.TryStartZap(device, nonMob, SPlayer), Is.False);

            // На живом в Standard: TryStartZap запускает вступление (5с), затем отменяется сервером.
            Assert.That(sharedDefib.CanZap(device, target, SPlayer), Is.False);
            Assert.That(sharedDefib.TryStartZap(device, target, SPlayer), Is.True);
        });

        await RunSeconds(6f);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(GetBatteryCharge(device), Is.EqualTo(chargeBefore),
                    "Отмена на живом не должна тратить заряд батареи.");
                Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(target), Is.EqualTo(FixedPoint2.Zero),
                    "Живой цели не должен наноситься урон от отменённого разряда.");
                Assert.That(SEntMan.HasComponent<DutyDefibHistoryComponent>(target), Is.False);
                Assert.That(SEntMan.HasComponent<DutyRevivalWeaknessComponent>(target), Is.False);
            });
        });
    }

    /// <summary>
    /// Статусы (слабость, гель, аритмия) снимаются по истечении времени, аритмия в крите
    /// добавляет удушье, а серия ударов сбрасывается после SpamResetTime.
    /// </summary>
    [Test]
    public async Task StatusEffectsExpire_ArrhythmiaTicksInCrit_HistoryResets()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        var target = SEntMan.GetEntity(Target!.Value);

        await Server.WaitPost(() =>
        {
            var now = SGameTiming.CurTime;
            var weakness = SEntMan.AddComponent<DutyRevivalWeaknessComponent>(target);
            weakness.EndTime = now + TimeSpan.FromSeconds(1.5);

            var gelled = SEntMan.AddComponent<DutyDefibGelledComponent>(target);
            gelled.EndTime = now + TimeSpan.FromSeconds(1.5);

            var arr = SEntMan.AddComponent<DutyArrhythmiaComponent>(target);
            arr.EndTime = now + TimeSpan.FromSeconds(4);

            // Переводим в крит (порог 100), чтобы проверить тик удушья от аритмии.
            var crit = SEntMan.System<MobThresholdSystem>().GetThresholdForState(target, MobState.Critical);
            SEntMan.System<DamageableSystem>().SetDamage(target, new DamageSpecifier(ProtoMan.Index(BluntDamage), crit));
        });

        await RunSeconds(2.5f);

        await Server.WaitAssertion(() =>
        {
            var allDamage = SEntMan.System<DamageableSystem>().GetAllDamage(target);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<DutyRevivalWeaknessComponent>(target), Is.False,
                    "Слабость после реанимации должна сняться после EndTime.");
                Assert.That(SEntMan.HasComponent<DutyDefibGelledComponent>(target), Is.False,
                    "Просроченный гель должен сняться после EndTime.");
                Assert.That(SEntMan.HasComponent<DutyArrhythmiaComponent>(target), Is.True,
                    "Аритмия ещё не должна истечь.");
                Assert.That(allDamage.DamageDict.TryGetValue(AsphyxiationDamage, out var asphyx) ? asphyx : FixedPoint2.Zero,
                    Is.GreaterThan(FixedPoint2.Zero),
                    "Аритмия в крите должна добавлять удушье.");
            });
        });

        await RunSeconds(2.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<DutyArrhythmiaComponent>(target), Is.False,
                "Аритмия должна сняться после EndTime.");
        });
    }
}
