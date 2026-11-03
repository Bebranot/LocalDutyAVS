// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Recoil;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// Калибр должен реально различаться в отдаче: патроны берут мощность из таблицы по тегу,
/// и на клиенте (где считается камера), и на сервере (где считается разброс).
/// </summary>
[TestFixture]
public sealed class DutyCaliberRecoilTests
{
    private static readonly (string Proto, float Power)[] Expected =
    {
        ("STCartridge918PGJ", 0.42f),
        ("STCartridge739FMJ", 1.0f),
        ("STCartridge754FMJ", 1.35f),
        ("STCartridge13x108FMJ", 2.2f),
        ("CartridgePistol", 0.5f),
    };

    [Test]
    public async Task CaliberPowerFromTagTable()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });

        foreach (var instance in new RobustIntegrationTest.IntegrationInstance[] { pair.Server, pair.Client })
        {
            var entMan = instance.ResolveDependency<IEntityManager>();
            var recoil = entMan.System<SharedDutyRecoilSystem>();

            await instance.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    foreach (var (proto, power) in Expected)
                    {
                        var uid = entMan.SpawnEntity(proto, MapCoordinates.Nullspace);
                        var shootable = entMan.GetComponent<CartridgeAmmoComponent>(uid);

                        Assert.That(recoil.GetCaliberPower(uid, shootable), Is.EqualTo(power).Within(0.001f),
                            $"{proto} на {instance.GetType().Name}");

                        entMan.DeleteEntity(uid);
                    }
                });
            });
        }

        await pair.CleanReturnAsync();
    }
}
