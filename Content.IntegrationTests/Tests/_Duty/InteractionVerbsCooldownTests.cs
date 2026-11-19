// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Duty.InteractionVerbs;
using Content.Shared._Duty.InteractionVerbs;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: проверка кулдаунов интеракт-вербов после перехода хранения в <see cref="OwnInteractionVerbsComponent.Cooldowns"/>
/// со словаря на список: изоляция по целям, зачистка истёкших записей и откат при провале проверки дальности.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedInteractionVerbsSystem))]
public sealed class InteractionVerbsCooldownTests : GameTest
{
    [SidedDependency(Side.Server)] private readonly InteractionVerbsSystem _verbs = null!;
    [SidedDependency(Side.Server)] private readonly SharedTransformSystem _transform = null!;

    [Test]
    public async Task PerTargetCooldownAndExpiredCleanup()
    {
        var map = await Pair.CreateTestMap();
        var user = await SpawnAtPosition("MobHuman", map.GridCoords);
        var target1 = await SpawnAtPosition("MobHuman", map.GridCoords);
        var target2 = await SpawnAtPosition("MobHuman", map.GridCoords);
        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var lookAt = SProtoMan.Index<InteractionVerbPrototype>("LookAt");
            var comp = SEntMan.EnsureComponent<OwnInteractionVerbsComponent>(user);
            var args1 = new InteractionArgs(user, target1, null, canAccess: true, canInteract: true, hasHands: true);
            var args2 = new InteractionArgs(user, target2, null, canAccess: true, canInteract: true, hasHands: true);

            Assert.Multiple(() =>
            {
                Assert.That(_verbs.StartVerb(lookAt, args1), Is.True, "Первый вызов LookAt должен пройти.");
                Assert.That(comp.Cooldowns, Has.Count.EqualTo(1));
                Assert.That(_verbs.StartVerb(lookAt, args1), Is.False, "Повторный LookAt по той же цели должен блокироваться кулдауном.");
                Assert.That(_verbs.StartVerb(lookAt, args2), Is.True, "LookAt по другой цели не должен блокироваться локальным кулдауном.");
                Assert.That(comp.Cooldowns, Has.Count.EqualTo(2));
            });
        });

        await RunSeconds(0.6f);

        await Server.WaitAssertion(() =>
        {
            var waveAt = SProtoMan.Index<InteractionVerbPrototype>("WaveAt");
            var comp = SEntMan.GetComponent<OwnInteractionVerbsComponent>(user);
            var args1 = new InteractionArgs(user, target1, null, canAccess: true, canInteract: true, hasHands: true);

            Assert.Multiple(() =>
            {
                Assert.That(_verbs.StartVerb(waveAt, args1), Is.True, "После истечения предыдущих кулдаунов новый верб должен пройти.");
                Assert.That(comp.Cooldowns, Has.Count.EqualTo(1), "Истёкшие кулдауны должны удаляться при старте нового.");
                Assert.That(comp.Cooldowns[0].Proto.Id, Is.EqualTo("WaveAt"));
                Assert.That(comp.Cooldowns[0].Target, Is.EqualTo(target1));
            });
        });
    }

    [Test]
    public async Task OutOfRangeRefundsCooldown()
    {
        var map = await Pair.CreateTestMap();
        var user = await SpawnAtPosition("MobHuman", map.GridCoords);
        var target = await SpawnAtPosition("MobHuman", map.GridCoords.Offset(new Vector2(30f, 0f)));
        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var lookAt = SProtoMan.Index<InteractionVerbPrototype>("LookAt");
            var comp = SEntMan.EnsureComponent<OwnInteractionVerbsComponent>(user);
            var args = new InteractionArgs(user, target, null, canAccess: true, canInteract: true, hasHands: true);

            Assert.That(_verbs.StartVerb(lookAt, args), Is.True);
            Assert.That(comp.Cooldowns, Is.Empty, "Провал PerformChecks по дистанции должен откатывать выставленный кулдаун.");

            _transform.SetCoordinates(target, map.GridCoords);
            Assert.That(_verbs.StartVerb(lookAt, args), Is.True, "После возврата в радиус и отката кулдауна верб должен запускаться сразу.");
            Assert.That(comp.Cooldowns, Has.Count.EqualTo(1));
        });
    }
}
