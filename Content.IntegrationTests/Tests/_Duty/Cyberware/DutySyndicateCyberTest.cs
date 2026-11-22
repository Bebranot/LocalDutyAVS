// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Duty.Cyberware.Syndicate;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Implants;
using Content.Shared.Lock;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Duty.Cyberware;

/// <summary>
/// _Duty: «Пальцы-отмычки» — действие по шлюзу и по запертому шкафчику доводит doAfter до конца и открывает их.
/// </summary>
[TestFixture]
public sealed class DutySyndicateCyberTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [SidedDependency(Side.Server)] private readonly SharedSubdermalImplantSystem _implants = default!;

    private async Task<EntityUid> Implant(string proto)
    {
        EntityUid? implant = null;
        await Server.WaitPost(() => implant = _implants.AddImplant(SPlayer, proto));
        Assert.That(implant, Is.Not.Null, $"Не удалось вживить {proto}.");
        return implant!.Value;
    }

    private async Task UseLockpick(EntityUid implant, float seconds)
    {
        await Server.WaitPost(() =>
        {
            var ev = new DutyLockpickActionEvent { Performer = SPlayer, Target = STarget!.Value };
            SEntMan.EventBus.RaiseLocalEvent(implant, (object) ev);
            Assert.That(ev.Handled, Is.True, "Действие должно обработаться.");
        });
        await RunTicks((int) (seconds * 60f) + 60);
    }

    [Test]
    public async Task LockpickOpensAirlock()
    {
        var implant = await Implant("DutyLockpickFingersImplant");
        await SpawnTarget("Airlock");
        await RunTicks(5);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<DoorComponent>(STarget!.Value).State, Is.EqualTo(DoorState.Closed)));

        await UseLockpick(implant, 8f);

        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<DoorComponent>(STarget!.Value).State;
            Assert.That(state, Is.AnyOf(DoorState.Opening, DoorState.Open), "Шлюз должен открыться после взлома.");
        });
    }

    [Test]
    public async Task LockpickUnlocksLocker()
    {
        var implant = await Implant("DutyLockpickFingersImplant");
        await SpawnTarget("LockerSecurity");
        await RunTicks(5);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<LockComponent>(STarget!.Value).Locked, Is.True));

        await UseLockpick(implant, 4f);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<LockComponent>(STarget!.Value).Locked, Is.False, "Замок шкафчика должен открыться."));
    }
}
