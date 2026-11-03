// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Duty.Movement;
using Content.Shared._Duty.Slide;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Input;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: подкат «Мастера подкатов» через настоящий ввод игрока. Путь «клиент поглощает R и шлёт
/// предсказуемый запрос» серверными вызовами не проверить — движок не отправляет на сервер
/// клавишу, поглощённую локальным хендлером, и ошибка тут означает, что подката нет вовсе.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedDutySlideSystem))]
public sealed class DutySlideTests : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    /// <summary>
    /// Не больше 5 плиток сверх стартовой: с 7-й грид получает собственную атмосферу (AutomaticAtmosSystem),
    /// его тайлы оказываются вакуумом, и баротравма портит проверки урона.
    /// </summary>
    private const int FloorTiles = 5;

    /// <summary>Без трейта R на бегу — обычное падение ADT.</summary>
    [Test]
    public async Task WithoutTraitFallsNormally()
    {
        await PrepareFloor();
        await StartSprint();
        await PressKey(ContentKeyFunctions.ToggleKnockdown);
        await StopInput();
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<KnockedDownComponent>(SPlayer), Is.True, "Без трейта R должна ронять обычным нокдауном.");
                Assert.That(SEntMan.HasComponent<DutySlidingComponent>(SPlayer), Is.False, "Без трейта подката быть не должно.");
            });
        });
    }

    /// <summary>
    /// Подкат: лёг, проехал около 3 тайлов, сам встал, из рук ничего не выпало. Повтор до конца
    /// перезарядки — обычное падение, и перезарядка при этом не сбрасывается.
    /// </summary>
    [Test]
    public async Task SlideTravelsStandsUpAndRespectsCooldown()
    {
        await PrepareFloor();
        await GiveTrait();
        var item = SEntMan.GetEntity(await PlaceInHands("Crowbar"));

        await StartSprint();
        var startX = await StartSlide();
        await StopInput();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<StandingStateSystem>().IsDown(SPlayer), Is.True, "Во время подката персонаж лежит.");
        });

        await RunSeconds(1.5f);

        TimeSpan nextAllowed = default;
        await Server.WaitAssertion(() =>
        {
            var x = LocalX();
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<DutySlidingComponent>(SPlayer), Is.False, "Подкат не закончился.");
                Assert.That(SEntMan.System<StandingStateSystem>().IsDown(SPlayer), Is.False, "После подката персонаж не встал сам.");
                Assert.That(SEntMan.HasComponent<KnockedDownComponent>(SPlayer), Is.False, "Подкат не должен оставлять обычный нокдаун.");
                // Путь задаётся профилем по времени и зависит от скорости разбега — около трёх тайлов.
                Assert.That(x - startX, Is.InRange(2.2f, 4f), $"Подкат должен провезти около трёх тайлов, а провёз {x - startX}.");
                Assert.That(HandSys.GetActiveItem(SPlayer), Is.EqualTo(item), "Предмет выпал из рук во время подката.");
            });

            nextAllowed = SEntMan.GetComponent<DutySlideMasterComponent>(SPlayer).NextSlideAllowed;
        });

        // Отпущенная C завела кулдаун спринта — снимаем его, чтобы второй разгон был настоящим спринтом.
        await Server.WaitPost(() =>
        {
            var stamina = SEntMan.GetComponent<DutyStaminaComponent>(SPlayer);
            stamina.NextSprintAllowed = TimeSpan.Zero;
            SEntMan.Dirty(SPlayer, stamina);
        });
        await RunTicks(5);

        // Обратно на запад: полоса пола короткая, а за её краем невесомость снимает нокдаун.
        await StartSprint(DirectionFlag.West);
        await PressKey(ContentKeyFunctions.ToggleKnockdown);
        await StopInput(DirectionFlag.West);
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<DutySlidingComponent>(SPlayer), Is.False, "Подкат сработал во время перезарядки.");
                Assert.That(SEntMan.HasComponent<KnockedDownComponent>(SPlayer), Is.True, "На перезарядке R должна ронять обычным нокдауном.");
                Assert.That(SEntMan.GetComponent<DutySlideMasterComponent>(SPlayer).NextSlideAllowed, Is.EqualTo(nextAllowed),
                    "Обычное падение на перезарядке не должно сдвигать перезарядку подката.");
            });
        });
    }

    /// <summary>В невесомости отталкиваться не от чего — подката нет.</summary>
    [Test]
    public async Task NoSlideWhenWeightless()
    {
        await PrepareFloor(gravity: false);
        await GiveTrait();
        await StartSprint();
        await PressKey(ContentKeyFunctions.ToggleKnockdown);
        await StopInput();
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<DutySlidingComponent>(SPlayer), Is.False, "В невесомости подкат начался.");
        });
    }

    /// <summary>Тянущий что-то подкат не делает.</summary>
    [Test]
    public async Task NoSlideWhilePulling()
    {
        await PrepareFloor();
        await GiveTrait();

        var crowbar = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            crowbar = SEntMan.SpawnEntity("Crowbar", SEntMan.GetCoordinates(PlayerCoords));
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<PullingSystem>().TryStartPull(SPlayer, crowbar), Is.True, "Не получилось начать тянуть лом.");
        });

        await StartSprint();
        await PressKey(ContentKeyFunctions.ToggleKnockdown);
        await StopInput();
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<DutySlidingComponent>(SPlayer), Is.False, "Подкат начался, хотя персонаж что-то тянет.");
        });
    }

    /// <summary>
    /// Таран с гарантированным шансом: цель сбита и ранена, подкатывающий получает свои 2 урона,
    /// лежит оглушённым и потом встаёт сам. Главное — клиент предсказывает таран сам: останавливается
    /// на цели там же, где сервер, а не пролетает сквозь неё с откатом назад.
    /// </summary>
    [Test]
    public async Task TackleKnocksTargetDownAndIsPredicted()
    {
        await PrepareFloor();
        await GiveTrait();

        // Цель ставим заранее, чтобы клиент точно успел о ней узнать до подката.
        var target = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            target = SEntMan.SpawnEntity("MobHuman", MapData.GridCoords.Offset(new Vector2(3.5f, 0.5f)));
        });
        await RunTicks(5);

        var targetBefore = await TotalDamage(target);

        // Шанс не сетевой (берётся из прототипа), поэтому в тесте выставляем его обеим сторонам.
        await Server.WaitPost(() => SEntMan.GetComponent<DutySlideMasterComponent>(SPlayer).TackleChanceBase = 1f);
        await Client.WaitPost(() => CEntMan.GetComponent<DutySlideMasterComponent>(CPlayer).TackleChanceBase = 1f);

        await StartSprint();
        await StartSlide();
        var selfBefore = await TotalDamage(SPlayer);
        await StopInput();

        var tackled = false;
        for (var i = 0; i < 60 && !tackled; i++)
        {
            await RunTicks(1);
            await Server.WaitPost(() =>
            {
                tackled = SEntMan.TryGetComponent<DutySlidingComponent>(SPlayer, out var slide)
                          && slide.Phase == DutySlidePhase.TackleStun;
            });
        }

        Assert.That(tackled, Is.True, "Сервер не засчитал таран при шансе 100%.");
        await RunTicks(2);

        var serverX = 0f;
        await Server.WaitPost(() => serverX = LocalX());
        await Client.WaitAssertion(() =>
        {
            var clientX = CEntMan.GetComponent<TransformComponent>(CPlayer).LocalPosition.X;
            Assert.Multiple(() =>
            {
                Assert.That(CEntMan.TryGetComponent<DutySlidingComponent>(CPlayer, out var slide)
                            && slide.Phase == DutySlidePhase.TackleStun, Is.True,
                    "Клиент не предсказал таран — у игрока будет пролёт сквозь цель и откат назад.");
                Assert.That(clientX, Is.EqualTo(serverX).Within(0.25f),
                    "Клиент остановился не там, где сервер — будет рывок назад.");
            });
        });

        await RunSeconds(0.3f);

        await Server.WaitAssertion(() =>
        {
            var damageable = SEntMan.System<DamageableSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(damageable.GetTotalDamage(target).Float() - targetBefore, Is.GreaterThanOrEqualTo(9.8f), "Цель тарана не получила урон.");
                Assert.That(SEntMan.System<StandingStateSystem>().IsDown(target), Is.True, "Цель тарана осталась на ногах.");
                Assert.That(damageable.GetTotalDamage(SPlayer).Float() - selfBefore, Is.EqualTo(2f).Within(0.2f), $"Подкатывающий должен получить ровно 2 урона: {DescribeDamage(SPlayer)}.");
            });
        });

        await RunSeconds(2f);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<DutySlidingComponent>(SPlayer), Is.False, "Подкат после тарана не закончился.");
                Assert.That(SEntMan.System<StandingStateSystem>().IsDown(SPlayer), Is.False, "После тарана подкатывающий не встал сам.");
            });
        });
    }

    /// <summary>Стена по курсу на скорости — обычный нокдаун ADT и урон.</summary>
    [Test]
    public async Task WallKnocksDown()
    {
        await PrepareFloor();
        await GiveTrait();

        await StartSprint();
        var startX = await StartSlide();
        var damageBefore = await TotalDamage(SPlayer);
        await StopInput();

        await Server.WaitPost(() =>
        {
            var wallTile = MathF.Floor(startX) + 2f;
            SEntMan.SpawnEntity("WallSolid", MapData.GridCoords.Offset(new Vector2(wallTile + 0.5f, 0.5f)));
        });

        await RunSeconds(0.5f);

        await Server.WaitAssertion(() =>
        {
            var damage = SEntMan.System<DamageableSystem>().GetTotalDamage(SPlayer).Float() - damageBefore;
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<DutySlidingComponent>(SPlayer), Is.False, "Подкат не оборвался о стену.");
                Assert.That(SEntMan.HasComponent<KnockedDownComponent>(SPlayer), Is.True, "Удар о стену должен давать обычный нокдаун.");
                // У подопытного нет брони — ровно 5 (допуск на естественное заживление стартовых царапин).
                Assert.That(damage, Is.EqualTo(5f).Within(0.2f), $"Урон от стены должен быть 5, а не {damage} ({DescribeDamage(SPlayer)}).");
            });
        });
    }

    // ── Хелперы ───────────────────────────────────────────────────────────────

    private async Task PrepareFloor(bool gravity = true)
    {
        for (var x = 1; x <= FloorTiles; x++)
            await SetTile(Plating, SEntMan.GetNetCoordinates(MapData.GridCoords.Offset(new Vector2(x + 0.5f, 0.5f))), MapData.Grid);

        if (gravity)
            await AddGravity();

        // Без атмосферы вакуум наносит урон и портит проверки урона.
        await AddAtmosphere();
        await RunTicks(5);
    }

    private async Task GiveTrait()
    {
        await Server.WaitPost(() => SEntMan.EnsureComponent<DutySlideMasterComponent>(SPlayer));
        await RunTicks(3);
    }

    /// <summary>Зажать C и бежать, пока не разгонимся.</summary>
    private async Task StartSprint(DirectionFlag direction = DirectionFlag.East)
    {
        await SetKey(ContentKeyFunctions.Sprint, BoundKeyState.Down);
        await SetMovementKey(direction, BoundKeyState.Down);
        await RunTicks(10);
    }

    private async Task StopInput(DirectionFlag direction = DirectionFlag.East)
    {
        await SetMovementKey(direction, BoundKeyState.Up);
        await SetKey(ContentKeyFunctions.Sprint, BoundKeyState.Up);
    }

    /// <summary>
    /// Жмёт R и ждёт, пока сервер начнёт подкат. Возвращает X в момент старта — меряем путь от
    /// него, а не от нажатия: запрос доезжает до сервера не в тот же тик.
    /// </summary>
    private async Task<float> StartSlide()
    {
        await SetKey(ContentKeyFunctions.ToggleKnockdown, BoundKeyState.Down);

        var startX = float.NaN;
        for (var i = 0; i < 10 && float.IsNaN(startX); i++)
        {
            await RunTicks(1);
            await Server.WaitPost(() =>
            {
                if (SEntMan.HasComponent<DutySlidingComponent>(SPlayer))
                    startX = LocalX();
            });
        }

        await SetKey(ContentKeyFunctions.ToggleKnockdown, BoundKeyState.Up);
        Assert.That(float.IsNaN(startX), Is.False, "Спринт + R с трейтом не начали подкат на сервере.");
        return startX;
    }

    /// <summary>
    /// Урон на момент замера. Проверяем разницу, а не абсолют: MobHuman бывает заспавнен уже
    /// с парой единиц урона.
    /// </summary>
    private async Task<float> TotalDamage(EntityUid uid)
    {
        var total = 0f;
        await Server.WaitPost(() => total = SEntMan.System<DamageableSystem>().GetTotalDamage(uid).Float());
        return total;
    }

    private string DescribeDamage(EntityUid uid)
    {
        var damage = SEntMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict;
        return string.Join(", ", damage.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private float LocalX()
    {
        return SEntMan.GetComponent<TransformComponent>(SPlayer).LocalPosition.X;
    }
}
