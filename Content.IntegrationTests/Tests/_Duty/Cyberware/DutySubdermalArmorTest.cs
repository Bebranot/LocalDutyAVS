// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Armor;
using Content.Shared._Duty.Cyberware.Surgery;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Implants;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Duty.Cyberware;

/// <summary>
/// _Duty: бронепластины «Панцирь» / «Чешуя» — резист и замедление от числа пластин (1/2/3), общий лимит,
/// смесь типов, исключённые типы урона, откат после извлечения, строка HUD и синхронизация на клиент.
/// Отражение пуль считается предсказуемым рандомом и здесь не проверяется (результат зависит от тика).
/// </summary>
[TestFixture]
public sealed class DutySubdermalArmorTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;

    private const string Plate = "DutySubdermalArmorPlate";
    private const string Elite = "DutySubdermalArmorPlateElite";

    private EntityUid AddPlate(string proto)
    {
        var implant = _implants.AddImplant(SPlayer, proto);
        Assert.That(implant, Is.Not.Null, $"Не удалось вживить {proto}.");
        return implant!.Value;
    }

    /// <summary>Множитель урона Blunt (100 → x) и Asphyxiation (10 → y) через настоящее событие DamageModifyEvent.</summary>
    private (float Blunt, float Asphyxiation) MeasureDamage()
    {
        var spec = new DamageSpecifier();
        spec.DamageDict["Blunt"] = FixedPoint2.New(100);
        spec.DamageDict["Asphyxiation"] = FixedPoint2.New(10);
        var ev = new DamageModifyEvent(spec);
        SEntMan.EventBus.RaiseLocalEvent(SPlayer, ev);
        return (ev.Damage.DamageDict["Blunt"].Float() / 100f, ev.Damage.DamageDict["Asphyxiation"].Float() / 10f);
    }

    private float MeasureSpeed()
    {
        var ev = new RefreshMovementSpeedModifiersEvent();
        SEntMan.EventBus.RaiseLocalEvent(SPlayer, ev);
        return ev.WalkSpeedModifier;
    }

    private async Task AssertState(float damage, float speed, int stacks)
    {
        await RunTicks(3);
        await Server.WaitAssertion(() =>
        {
            var (blunt, asphyxiation) = MeasureDamage();
            Assert.That(blunt, Is.EqualTo(damage).Within(0.01f), "Множитель урона.");
            Assert.That(asphyxiation, Is.EqualTo(1f).Within(0.001f), "Удушье броня не режет.");
            Assert.That(MeasureSpeed(), Is.EqualTo(speed).Within(0.001f), "Множитель скорости.");
            Assert.That(SEntMan.GetComponent<DutySubdermalArmorComponent>(SPlayer).Stacks, Is.EqualTo(stacks));
        });
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.TryGetComponent<DutySubdermalArmorComponent>(CPlayer, out var comp), Is.True,
                "Компонент пластин должен доехать до клиента (скорость предиктится).");
            Assert.That(comp!.Stacks, Is.EqualTo(stacks));
        });
    }

    [Test]
    public async Task StackScalesResistAndSpeed()
    {
        await Server.WaitPost(() => AddPlate(Plate));
        await AssertState(0.85f, 0.90f, 1);

        await Server.WaitPost(() => AddPlate(Plate));
        await AssertState(0.72f, 0.78f, 2);

        await Server.WaitPost(() => AddPlate(Plate));
        await AssertState(0.60f, 0.63f, 3);
    }

    [Test]
    public async Task EliteSlowsHalfAsMuch()
    {
        await Server.WaitPost(() =>
        {
            AddPlate(Elite);
            AddPlate(Elite);
            AddPlate(Elite);
        });
        await AssertState(0.60f, 0.815f, 3);
    }

    /// <summary>Порядок вживления не важен: 2 «Панциря» + 1 «Чешуя» = 0.10 + 0.12 + 0.075.</summary>
    [Test]
    public async Task MixedStackIsOrderIndependent()
    {
        await Server.WaitPost(() =>
        {
            AddPlate(Plate);
            AddPlate(Elite);
            AddPlate(Plate);
        });
        await AssertState(0.60f, 0.705f, 3);
    }

    [Test]
    public async Task FourthPlateIsRefused()
    {
        await Server.WaitPost(() =>
        {
            AddPlate(Plate);
            AddPlate(Elite);
            AddPlate(Plate);
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            var fourth = SEntMan.SpawnEntity(Elite, SEntMan.GetCoordinates(PlayerCoords));
            var ev = new DutyCyberImplantAttemptEvent(SPlayer, SPlayer);
            SEntMan.EventBus.RaiseLocalEvent(fourth, ref ev);
            Assert.That(ev.Cancelled, Is.True, "Четвёртая пластина должна получить отказ.");
            Assert.That(ev.Reason, Is.Not.Null);
        });
    }

    [Test]
    public async Task ThirdPlateAllowedWhenTwoInstalled()
    {
        await Server.WaitPost(() =>
        {
            AddPlate(Plate);
            AddPlate(Plate);
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            var third = SEntMan.SpawnEntity(Plate, SEntMan.GetCoordinates(PlayerCoords));
            var ev = new DutyCyberImplantAttemptEvent(SPlayer, SPlayer);
            SEntMan.EventBus.RaiseLocalEvent(third, ref ev);
            Assert.That(ev.Cancelled, Is.False, "На третью пластину место ещё есть.");
        });
    }

    [Test]
    public async Task RemovalRollsBackAndClearsComponent()
    {
        var plates = new List<EntityUid>();
        await Server.WaitPost(() =>
        {
            plates.Add(AddPlate(Plate));
            plates.Add(AddPlate(Plate));
            plates.Add(AddPlate(Plate));
        });
        await AssertState(0.60f, 0.63f, 3);

        await Server.WaitPost(() => _implants.ForceRemove(SPlayer, plates[2]));
        await AssertState(0.72f, 0.78f, 2);

        await Server.WaitPost(() =>
        {
            _implants.ForceRemove(SPlayer, plates[1]);
            _implants.ForceRemove(SPlayer, plates[0]);
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<DutySubdermalArmorComponent>(SPlayer), Is.False,
                "Без пластин компонент носителя должен исчезнуть.");
            var (blunt, _) = MeasureDamage();
            Assert.That(blunt, Is.EqualTo(1f).Within(0.001f));
            Assert.That(MeasureSpeed(), Is.EqualTo(1f).Within(0.001f));
        });
    }

    /// <summary>Строка HUD на каждой пластине показывает общий резист, а три пластины сворачиваются в одну строку.</summary>
    [Test]
    public async Task HudDetailShowsTotalResist()
    {
        var plates = new List<EntityUid>();
        await Server.WaitPost(() =>
        {
            plates.Add(AddPlate(Plate));
            plates.Add(AddPlate(Plate));
            plates.Add(AddPlate(Plate));
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            foreach (var plate in plates)
            {
                Assert.That(SEntMan.GetComponent<DutyCyberwareComponent>(plate).Detail, Is.EqualTo("40%"));
            }

            var host = SEntMan.GetComponent<DutyCyberHostComponent>(SPlayer);
            Assert.That(host.Load, Is.EqualTo(3));
            Assert.That(host.Modules, Has.Count.EqualTo(1), "Три пластины сворачиваются в одну строку.");
            Assert.That(host.Modules[0].Count, Is.EqualTo(3));
        });
    }
}
