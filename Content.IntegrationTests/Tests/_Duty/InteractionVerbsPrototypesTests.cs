// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.InteractionVerbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: портировано из Goob-Station (InteractionPrototypesTest) вместе с самой системой
/// интеракт-вербов. Ловит забытый Action на непустых (не-abstract) прототипах — без него верб
/// либо не выполняется, либо валится на десериализации.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.SingleInstance)]
[TestOf(typeof(InteractionVerbPrototype))]
public sealed class InteractionVerbsPrototypesTests
{
    [Test]
    public async Task ValidatePrototypeContents()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        await server.WaitIdleAsync();

        var protoMan = server.ResolveDependency<IPrototypeManager>();

        foreach (var proto in protoMan.EnumeratePrototypes<InteractionVerbPrototype>())
        {
            Assert.That(proto.Abstract || proto.Action is not null, $"Non-abstract prototype {proto.ID} lacks an action!");
        }

        await pair.CleanReturnAsync();
    }
}
