// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Tests.Interaction;
using Content.Server._Duty.Lazarus;
using Content.Shared._Duty.Lazarus;
using Content.Shared.Administration.Systems;
using Content.Shared.Body.Components;
using Content.Shared.CCVar;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: интеграционные тесты эффекта Лазаруса (<see cref="LazarusSystem"/>):
/// прикрепление к гуманоидам, условия срабатывания, отложенное воскрешение,
/// шрам <see cref="LazarusScarComponent"/> и обрыв кинематики.
/// </summary>
[TestFixture]
[TestOf(typeof(LazarusSystem))]
public sealed class DutyLazarusTests : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";

    private async Task ConfigureLazarusCVars(
        bool enabled = true,
        float nearDeath = 0.5f,
        float chanceMin = 1f,
        float chanceMax = 1f,
        float cooldownMinutes = 25f,
        float nearbyRange = 0f)
    {
        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<LazarusComponent>(SPlayer);
            var cfg = Server.CfgMan;
            cfg.SetCVar(DutyCCVars.LazarusEnabled, enabled);
            cfg.SetCVar(DutyCCVars.LazarusNearDeathThreshold, nearDeath);
            cfg.SetCVar(DutyCCVars.LazarusChanceMin, chanceMin);
            cfg.SetCVar(DutyCCVars.LazarusChanceMax, chanceMax);
            cfg.SetCVar(DutyCCVars.LazarusCooldownMinutes, cooldownMinutes);
            cfg.SetCVar(DutyCCVars.LazarusNearbyPlayerRange, nearbyRange);
        });
    }

    private void SetBluntDamage(EntityUid uid, FixedPoint2 amount)
    {
        var dmg = new DamageSpecifier(ProtoMan.Index(BluntDamage), amount);
        SEntMan.System<DamageableSystem>().SetDamage(uid, dmg);
    }

    private void SetNearDeathDamage(EntityUid uid)
    {
        var thresholds = SEntMan.System<MobThresholdSystem>();
        var crit = thresholds.GetThresholdForState(uid, MobState.Critical);
        var dead = thresholds.GetThresholdForState(uid, MobState.Dead);
        SetBluntDamage(uid, (crit + dead) / 2 + FixedPoint2.New(10));
    }

    private FixedPoint2 SetDeadDamage(EntityUid uid)
    {
        var dead = SEntMan.System<MobThresholdSystem>().GetThresholdForState(uid, MobState.Dead);
        SetBluntDamage(uid, dead + FixedPoint2.New(5));
        return dead;
    }

    /// <summary>
    /// При вселении разума (MindAddedMessage) LazarusComponent вешается только на гуманоидов.
    /// </summary>
    [Test]
    public async Task MindAdded_AttachesLazarusOnlyToHumanoids()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        var human = SEntMan.GetEntity(Target!.Value);
        EntityUid mouse = default;
        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(human).Coordinates;
            mouse = SEntMan.SpawnEntity("MobMouse", coords);
        });

        await Server.WaitAssertion(() =>
        {
            var mindSys = SEntMan.System<SharedMindSystem>();

            Assert.That(SEntMan.HasComponent<LazarusComponent>(human), Is.False);
            Assert.That(SEntMan.HasComponent<LazarusComponent>(mouse), Is.False);

            var humanMind = mindSys.CreateMind(null);
            mindSys.TransferTo(humanMind, human);

            var mouseMind = mindSys.CreateMind(null);
            mindSys.TransferTo(mouseMind, mouse);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<LazarusComponent>(human), Is.True,
                    "На гуманоида при вселении разума должен вешаться LazarusComponent.");
                Assert.That(SEntMan.HasComponent<LazarusComponent>(mouse), Is.False,
                    "На не-гуманоида LazarusComponent вешаться не должен.");
            });
        });
    }

    /// <summary>
    /// Полный цикл срабатывания: крит в зоне смерти запускает отложенное вставание,
    /// по завершении ReviveDelay лечит из крита, вводит стимуляторы и вешает шрам (-10% порогов).
    /// </summary>
    [Test]
    public async Task CriticalNearDeath_RevivesAfterDelay_InjectsStimulantsAndAppliesScar()
    {
        await AddAtmosphere();
        await ConfigureLazarusCVars(enabled: true, nearDeath: 0.5f, chanceMin: 1f, chanceMax: 1f, nearbyRange: 0f);

        FixedPoint2 origCrit = default;
        FixedPoint2 origDead = default;

        await Server.WaitPost(() =>
        {
            var thresholds = SEntMan.System<MobThresholdSystem>();
            origCrit = thresholds.GetThresholdForState(SPlayer, MobState.Critical);
            origDead = thresholds.GetThresholdForState(SPlayer, MobState.Dead);

            // 160 урона при порогах 100/200 -> remaining = 0.4 <= 0.5 (зона смерти).
            SetBluntDamage(SPlayer, (origCrit + origDead) / 2 + FixedPoint2.New(10));
        });

        // Ждём 0.6с (> ScanInterval 0.25с): Trigger должен сработать, но вставание только через 5.5с.
        await RunSeconds(0.6f);

        await Server.WaitAssertion(() =>
        {
            var lazarus = SEntMan.GetComponent<LazarusComponent>(SPlayer);

            Assert.Multiple(() =>
            {
                Assert.That(lazarus.RolledThisCrit, Is.True);
                Assert.That(lazarus.NextAvailableTime, Is.GreaterThan(SGameTiming.CurTime));
                Assert.That(SEntMan.GetComponent<MobStateComponent>(SPlayer).CurrentState, Is.EqualTo(MobState.Critical),
                    "До истечения ReviveDelay персонаж должен оставаться в крите.");
                Assert.That(SEntMan.HasComponent<LazarusScarComponent>(SPlayer), Is.False);
            });
        });

        // Ждём завершения ReviveDelay (5.5с).
        await RunSeconds(5.6f);

        await Server.WaitAssertion(() =>
        {
            var lazarus = SEntMan.GetComponent<LazarusComponent>(SPlayer);
            var thresholds = SEntMan.System<MobThresholdSystem>();
            var damageable = SEntMan.System<DamageableSystem>();
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();

            var newCrit = thresholds.GetThresholdForState(SPlayer, MobState.Critical);
            var newDead = thresholds.GetThresholdForState(SPlayer, MobState.Dead);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<MobStateComponent>(SPlayer).CurrentState, Is.Not.EqualTo(MobState.Critical),
                    "После ReviveDelay персонаж должен выйти из крита.");
                Assert.That(damageable.GetTotalDamage(SPlayer), Is.LessThanOrEqualTo(origCrit * lazarus.HealToCritFraction + FixedPoint2.New(1)),
                    "Урон должен снизиться до доли порога крита.");
                Assert.That(SEntMan.HasComponent<LazarusScarComponent>(SPlayer), Is.True,
                    "После воскрешения должен вешаться LazarusScarComponent.");

                var scar = SEntMan.GetComponent<LazarusScarComponent>(SPlayer);
                Assert.That(scar.OriginalCritThreshold, Is.EqualTo(origCrit));
                Assert.That(scar.OriginalDeadThreshold, Is.EqualTo(origDead));
                Assert.That(newCrit, Is.EqualTo(origCrit * lazarus.MaxHpPenaltyFraction));
                Assert.That(newDead, Is.EqualTo(origDead * lazarus.MaxHpPenaltyFraction));

                // Проверяем инъекцию омнизина и эфедрина в кровоток.
                var bloodstream = SEntMan.GetComponent<BloodstreamComponent>(SPlayer);
                Assert.That(solutions.TryGetSolution(SPlayer, bloodstream.BloodSolutionName, out _, out var blood), Is.True);
                Assert.That(blood!.GetTotalPrototypeQuantity("Omnizine"), Is.GreaterThan(FixedPoint2.Zero));
                Assert.That(blood.GetTotalPrototypeQuantity("Ephedrine"), Is.GreaterThan(FixedPoint2.Zero));
            });
        });
    }

    /// <summary>
    /// Регрессия шрама: повторное срабатывание в той же жизни не складывает штраф порогов,
    /// а смерть (переход в Dead) снимает шрам и возвращает исходные пороги.
    /// </summary>
    [Test]
    public async Task Scar_DoesNotStackAndClearedOnDeath()
    {
        await AddAtmosphere();
        await ConfigureLazarusCVars(enabled: true, nearDeath: 0.5f, chanceMin: 1f, chanceMax: 1f, cooldownMinutes: 0f, nearbyRange: 0f);

        FixedPoint2 origCrit = default;
        FixedPoint2 origDead = default;

        // Первое срабатывание.
        await Server.WaitPost(() =>
        {
            var thresholds = SEntMan.System<MobThresholdSystem>();
            origCrit = thresholds.GetThresholdForState(SPlayer, MobState.Critical);
            origDead = thresholds.GetThresholdForState(SPlayer, MobState.Dead);
            SetNearDeathDamage(SPlayer);
        });
        await RunSeconds(6.2f);

        FixedPoint2 scarredCrit = default;
        FixedPoint2 scarredDead = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<LazarusScarComponent>(SPlayer), Is.True);
            var thresholds = SEntMan.System<MobThresholdSystem>();
            scarredCrit = thresholds.GetThresholdForState(SPlayer, MobState.Critical);
            scarredDead = thresholds.GetThresholdForState(SPlayer, MobState.Dead);
        });

        // Второе срабатывание в той же жизни (кулдаун = 0).
        await Server.WaitPost(() => SetBluntDamage(SPlayer, (scarredCrit + scarredDead) / 2 + FixedPoint2.New(10)));
        await RunSeconds(6.2f);

        await Server.WaitAssertion(() =>
        {
            var thresholds = SEntMan.System<MobThresholdSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(thresholds.GetThresholdForState(SPlayer, MobState.Critical), Is.EqualTo(scarredCrit),
                    "Повторное срабатывание Лазаруса не должно складывать штраф порога крита.");
                Assert.That(thresholds.GetThresholdForState(SPlayer, MobState.Dead), Is.EqualTo(scarredDead),
                    "Повторное срабатывание Лазаруса не должно складывать штраф порога смерти.");
            });
        });

        // Смерть снимает шрам и возвращает исходные пороги.
        await Server.WaitPost(() => SetBluntDamage(SPlayer, scarredDead));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var thresholds = SEntMan.System<MobThresholdSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<MobStateComponent>(SPlayer).CurrentState, Is.EqualTo(MobState.Dead));
                Assert.That(SEntMan.HasComponent<LazarusScarComponent>(SPlayer), Is.False,
                    "При смерти LazarusScarComponent должен сниматься.");
                Assert.That(thresholds.GetThresholdForState(SPlayer, MobState.Critical), Is.EqualTo(origCrit),
                    "При снятии шрама смертью должен восстанавливаться исходный порог крита.");
                Assert.That(thresholds.GetThresholdForState(SPlayer, MobState.Dead), Is.EqualTo(origDead),
                    "При снятии шрама смертью должен восстанавливаться исходный порог смерти.");
            });
        });
    }

    /// <summary>
    /// Неудачный бросок выставляет RolledThisCrit = true (один бросок на эпизод крита),
    /// а выход из крита сбрасывает флаг.
    /// </summary>
    [Test]
    public async Task FailedRoll_SetsRolledThisCritAndResetsOnExitCrit()
    {
        await AddAtmosphere();
        await ConfigureLazarusCVars(enabled: true, nearDeath: 0.5f, chanceMin: 0f, chanceMax: 0f, nearbyRange: 0f);

        await Server.WaitPost(() => SetNearDeathDamage(SPlayer));
        await RunSeconds(0.6f);

        await Server.WaitAssertion(() =>
        {
            var lazarus = SEntMan.GetComponent<LazarusComponent>(SPlayer);
            Assert.That(lazarus.RolledThisCrit, Is.True, "После броска флаг RolledThisCrit должен выставляться.");
            Assert.That(lazarus.NextAvailableTime, Is.EqualTo(TimeSpan.Zero), "При промахе кулдаун не должен ставиться.");
        });

        // Лечим из крита -> RolledThisCrit должен сброситься.
        await Server.WaitPost(() => SetBluntDamage(SPlayer, FixedPoint2.Zero));
        await RunSeconds(0.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<MobStateComponent>(SPlayer).CurrentState, Is.EqualTo(MobState.Alive));
            Assert.That(SEntMan.GetComponent<LazarusComponent>(SPlayer).RolledThisCrit, Is.False,
                "При выходе из крита RolledThisCrit должен сбрасываться.");
        });
    }

    /// <summary>
    /// Наличие живого игрока рядом отменяет бросок без траты попытки (RolledThisCrit остаётся false);
    /// как только соседний игрок уходит, бросок срабатывает.
    /// </summary>
    [Test]
    public async Task NearbyAlivePlayer_BlocksRollWithoutConsumingAttempt()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        var victim = SEntMan.GetEntity(Target!.Value);
        await ConfigureLazarusCVars(enabled: true, nearDeath: 0.5f, chanceMin: 1f, chanceMax: 1f, nearbyRange: 3f);

        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<LazarusComponent>(victim);
            SetNearDeathDamage(victim);
        });

        await RunSeconds(0.7f);

        await Server.WaitAssertion(() =>
        {
            var lazarus = SEntMan.GetComponent<LazarusComponent>(victim);
            Assert.That(lazarus.RolledThisCrit, Is.False,
                "Пока рядом живой игрок, бросок не должен тратиться.");
            Assert.That(lazarus.NextAvailableTime, Is.EqualTo(TimeSpan.Zero));
        });

        // Уводим SPlayer далеко за пределы радиуса (на 10 тайлов).
        await Server.WaitPost(() =>
        {
            var xform = SEntMan.GetComponent<TransformComponent>(SPlayer);
            xform.Coordinates = new EntityCoordinates(xform.ParentUid, xform.Coordinates.Position + new System.Numerics.Vector2(10f, 0f));
        });

        await RunSeconds(0.6f);

        await Server.WaitAssertion(() =>
        {
            var lazarus = SEntMan.GetComponent<LazarusComponent>(victim);
            Assert.That(lazarus.RolledThisCrit, Is.True,
                "После ухода союзника бросок должен сработать.");
            Assert.That(lazarus.NextAvailableTime, Is.GreaterThan(TimeSpan.Zero));
        });
    }

    /// <summary>
    /// Если во время ReviveDelay цель добивают в Dead или лечат до Alive,
    /// отложенное вставание отменяется, шрам не вешается и кулдаун возвращается.
    /// </summary>
    [Test]
    public async Task CancelRevive_RestoresCooldownWhenKilledDuringDelay()
    {
        await AddAtmosphere();
        await ConfigureLazarusCVars(enabled: true, nearDeath: 0.5f, chanceMin: 1f, chanceMax: 1f, nearbyRange: 0f);

        await Server.WaitPost(() => SetNearDeathDamage(SPlayer));
        await RunSeconds(0.6f);

        await Server.WaitAssertion(() =>
        {
            var lazarus = SEntMan.GetComponent<LazarusComponent>(SPlayer);
            Assert.That(lazarus.NextAvailableTime, Is.GreaterThan(TimeSpan.Zero));
        });

        // Добиваем в Dead до истечения ReviveDelay.
        FixedPoint2 deadThreshold = default;
        await Server.WaitPost(() => deadThreshold = SetDeadDamage(SPlayer));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var lazarus = SEntMan.GetComponent<LazarusComponent>(SPlayer);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<MobStateComponent>(SPlayer).CurrentState, Is.EqualTo(MobState.Dead));
                Assert.That(lazarus.NextAvailableTime, Is.EqualTo(TimeSpan.Zero),
                    "При обрыве кинематики смертью кулдаун должен возвращаться к предыдущему значению.");
                Assert.That(SEntMan.HasComponent<LazarusScarComponent>(SPlayer), Is.False);
            });
        });

        // Дожидаемся времени исходного ReviveDelay и убеждаемся, что труп не подлечился.
        await RunSeconds(6f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(SPlayer), Is.GreaterThanOrEqualTo(deadThreshold),
                "Оборванный Лазарус не должен сбрасывать урон трупу задним числом.");
        });
    }

    /// <summary>
    /// При выключенном CVar или вне состояния Critical (Alive / Dead) Лазарус не срабатывает.
    /// </summary>
    [Test]
    public async Task DisabledOrNonCrit_HasNoSideEffects()
    {
        await AddAtmosphere();
        await ConfigureLazarusCVars(enabled: false, nearDeath: 0.5f, chanceMin: 1f, chanceMax: 1f, nearbyRange: 0f);

        await Server.WaitPost(() => SetNearDeathDamage(SPlayer));
        await RunSeconds(0.7f);

        await Server.WaitAssertion(() =>
        {
            var lazarus = SEntMan.GetComponent<LazarusComponent>(SPlayer);
            Assert.That(lazarus.RolledThisCrit, Is.False);
            Assert.That(lazarus.NextAvailableTime, Is.EqualTo(TimeSpan.Zero));
        });

        // Включаем, но цель в Alive (урон 50) и затем сразу в Dead.
        await ConfigureLazarusCVars(enabled: true, nearDeath: 0.5f, chanceMin: 1f, chanceMax: 1f, nearbyRange: 0f);
        await Server.WaitPost(() => SetBluntDamage(SPlayer, FixedPoint2.New(50)));
        await RunSeconds(0.6f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<LazarusComponent>(SPlayer).NextAvailableTime, Is.EqualTo(TimeSpan.Zero));
        });

        await Server.WaitPost(() => SetDeadDamage(SPlayer));
        await RunSeconds(0.6f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<MobStateComponent>(SPlayer).CurrentState, Is.EqualTo(MobState.Dead));
            Assert.That(SEntMan.GetComponent<LazarusComponent>(SPlayer).NextAvailableTime, Is.EqualTo(TimeSpan.Zero));
        });
    }

    /// <summary>
    /// Админское исцеление (<see cref="RejuvenateSystem.PerformRejuvenate"/>) в глубоком крите
    /// не даёт эффекту Лазаруса сработать.
    /// </summary>
    [Test]
    public async Task RejuvenateDuringCrit_DoesNotTriggerLazarus()
    {
        await AddAtmosphere();
        await ConfigureLazarusCVars(enabled: true, nearDeath: 0.5f, chanceMin: 1f, chanceMax: 1f, nearbyRange: 0f);

        await Server.WaitPost(() =>
        {
            SetNearDeathDamage(SPlayer);
            SEntMan.System<RejuvenateSystem>().PerformRejuvenate(SPlayer);
        });

        await RunSeconds(0.6f);

        await Server.WaitAssertion(() =>
        {
            var lazarus = SEntMan.GetComponent<LazarusComponent>(SPlayer);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<MobStateComponent>(SPlayer).CurrentState, Is.EqualTo(MobState.Alive));
                Assert.That(lazarus.RolledThisCrit, Is.False);
                Assert.That(lazarus.NextAvailableTime, Is.EqualTo(TimeSpan.Zero));
                Assert.That(SEntMan.HasComponent<LazarusScarComponent>(SPlayer), Is.False);
            });
        });
    }

    /// <summary>
    /// Если Лазарус уже запустился и идёт задержка до вставания, админский Rejuvenate
    /// немедленно обрывает сцену, возвращает кулдаун и не даёт сработать отложенному Revive.
    /// </summary>
    [Test]
    public async Task RejuvenateDuringDelay_CancelsPendingRevive()
    {
        await AddAtmosphere();
        await ConfigureLazarusCVars(enabled: true, nearDeath: 0.5f, chanceMin: 1f, chanceMax: 1f, nearbyRange: 0f);

        await Server.WaitPost(() => SetNearDeathDamage(SPlayer));
        await RunSeconds(0.6f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<LazarusComponent>(SPlayer).NextAvailableTime, Is.GreaterThan(TimeSpan.Zero));
        });

        await Server.WaitPost(() => SEntMan.System<RejuvenateSystem>().PerformRejuvenate(SPlayer));
        await RunTicks(1);

        await Server.WaitAssertion(() =>
        {
            var lazarus = SEntMan.GetComponent<LazarusComponent>(SPlayer);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<MobStateComponent>(SPlayer).CurrentState, Is.EqualTo(MobState.Alive));
                Assert.That(lazarus.RolledThisCrit, Is.False);
                Assert.That(lazarus.NextAvailableTime, Is.EqualTo(TimeSpan.Zero),
                    "При отмене через Rejuvenate кулдаун должен возвращаться сразу.");
                Assert.That(SEntMan.HasComponent<LazarusScarComponent>(SPlayer), Is.False);
            });
        });

        await RunSeconds(6f);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<LazarusScarComponent>(SPlayer), Is.False,
                    "Отменённый через Rejuvenate Лазарус не должен вешать шрам.");
                Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(SPlayer), Is.EqualTo(FixedPoint2.Zero));
            });
        });
    }
}
