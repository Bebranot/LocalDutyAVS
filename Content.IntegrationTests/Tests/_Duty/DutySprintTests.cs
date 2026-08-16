// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._Duty.Movement;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: спринт как третья ступень движения.
///
/// Первый тест здесь — про реальную поломку: компонент <c>DutyStamina</c> не был подключён ни к
/// одному прототипу, и вся фича молча не работала при живом и корректном коде. Поиск причины занял
/// заметно больше времени, чем сама однострочная правка, поэтому крепим её тестом.
/// </summary>
[TestFixture]
[TestOf(typeof(DutySprintSystem))]
public sealed class DutySprintTests : GameTest
{
    [SidedDependency(Side.Server)] private readonly DutySprintSystem _sprint = null!;
    [SidedDependency(Side.Server)] private readonly StandingStateSystem _standing = null!;

    /// <summary>
    /// Каждая играбельная раса должна получать пул выносливости. Компонент вешается один раз на
    /// <c>BaseSpeciesMob</c>, но проверяем по факту на конечных прототипах: если кто-то заведёт
    /// расу мимо общего родителя, спринт у неё не заработает, и заметить это в игре тяжело.
    /// </summary>
    [Test]
    public async Task EveryPlayableSpeciesHasStamina()
    {
        var protoMan = Server.ResolveDependency<IPrototypeManager>();
        var compFactory = Server.ResolveDependency<IComponentFactory>();

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var species in protoMan.EnumeratePrototypes<SpeciesPrototype>())
                {
                    if (!species.RoundStart)
                        continue;

                    Assert.That(protoMan.TryIndex(species.Prototype, out var mob), Is.True,
                        $"Раса {species.ID} ссылается на несуществующий прототип {species.Prototype}.");

                    if (mob is null)
                        continue;

                    Assert.That(mob.TryGetComponent<DutyStaminaComponent>(out _, compFactory), Is.True,
                        $"У расы {species.ID} нет DutyStamina — спринт для неё не работает вообще.");
                }
            });
        });
    }

    /// <summary>
    /// Множитель скорости должен падать по мере усталости, а не быть постоянным бонусом.
    /// Проверяем три точки: полный запас, ниже порога «загнан», ноль.
    /// </summary>
    [Test]
    public async Task SprintModifierFallsWithEndurance()
    {
        var human = await Spawn("MobHuman");

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<DutyStaminaComponent>(human);
            comp.WantsSprint = true;

            comp.Current = comp.Max;
            var full = _sprint.GetSprintModifier((human, comp));

            comp.Current = comp.Max * comp.WindedFraction * 0.5f;
            var winded = _sprint.GetSprintModifier((human, comp));

            comp.Current = 0f;
            var empty = _sprint.GetSprintModifier((human, comp));

            Assert.Multiple(() =>
            {
                Assert.That(full, Is.EqualTo(comp.SprintBonus).Within(0.001f),
                    "На полном запасе спринт должен давать ровно заявленный бонус.");
                Assert.That(winded, Is.LessThan(full), "Загнанный бежит медленнее свежего.");
                Assert.That(empty, Is.LessThan(winded), "На нуле выносливости должно быть ещё медленнее.");
                Assert.That(empty, Is.LessThan(1f),
                    "На нуле спринт обязан быть медленнее обычного бега, иначе его незачем отпускать.");
            });
        });
    }

    /// <summary>
    /// Обычный бег (клавиша спринта не зажата) штрафуется на низком запасе — иначе выносливость
    /// не имела бы последствий для того, кто просто не спринтует.
    /// </summary>
    [Test]
    public async Task LowStaminaSlowsNormalRun()
    {
        var human = await Spawn("MobHuman");

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<DutyStaminaComponent>(human);
            comp.WantsSprint = false;

            comp.Current = comp.Max;
            Assert.That(_sprint.GetSprintModifier((human, comp)), Is.EqualTo(1f).Within(0.001f),
                "На полном запасе обычный бег не должен ничем штрафоваться.");

            comp.Current = comp.Max * comp.LowRunThreshold * 0.5f;
            Assert.That(_sprint.GetSprintModifier((human, comp)), Is.EqualTo(comp.LowRunPenalty).Within(0.001f),
                "Ниже порога обычный бег должен замедляться.");
        });
    }

    /// <summary>
    /// Лежачий не спринтует. Гейт проверяется каждый тик, а не только при нажатии: сбили с ног
    /// посреди забега — разгон обязан оборваться сам.
    /// </summary>
    [Test]
    public async Task LyingDownBlocksSprint()
    {
        var human = await Spawn("MobHuman");

        await Server.WaitAssertion(() =>
        {
            Assert.That(_sprint.CanSprint(human), Is.True, "Стоя спринтовать можно.");

            _standing.Down(human);
            Assert.That(_sprint.CanSprint(human), Is.False, "Лёжа спринтовать нельзя.");

            _standing.Stand(human);
            Assert.That(_sprint.CanSprint(human), Is.True, "После подъёма спринт снова разрешён.");
        });
    }
}
