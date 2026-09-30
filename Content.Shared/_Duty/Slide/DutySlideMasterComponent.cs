// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Duty.Slide;

/// <summary>
/// _Duty: трейт «Мастер подкатов». Во время спринта клавиша «лечь» вместо обычного падения
/// запускает подкат (<see cref="DutySlidingComponent"/>). Здесь лежат кулдаун и все числа
/// механики, чтобы их можно было подкрутить прототипом, не трогая код.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class DutySlideMasterComponent : Component
{
    /// <summary>
    /// Когда разрешён следующий подкат. Сетевое: клиент по нему сам решает, предсказывать подкат
    /// или отдать R ванильному падению.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextSlideAllowed;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Длительность профиля скорости v = v0·(1 − t/T)^p. Путь выходит ≈ v0·T/(p+1): при обычном
    /// спринте около трёх тайлов, быстрее бежал — дальше проехал.
    /// </summary>
    [DataField]
    public TimeSpan SlideDuration = TimeSpan.FromSeconds(1.2);

    /// <summary>
    /// Степень p в профиле. Чем больше, тем резче рывок на старте и длиннее медленный «доезд» —
    /// именно этот контраст и читается глазом как подкат, а не как бег лёжа.
    /// </summary>
    [DataField]
    public float SlideExponent = 2.5f;

    /// <summary>Ниже этой скорости (тайл/с) скольжение считается законченным — хвост профиля почти стоит на месте.</summary>
    [DataField]
    public float EndSpeed = 0.5f;

    /// <summary>Дистанция от точки старта, если по пути попалась слизь/лужа (приблизительная).</summary>
    [DataField]
    public float SlipperyDistance = 5f;

    /// <summary>Стартовая скорость — текущая, умноженная на это.</summary>
    [DataField]
    public float SpeedMultiplier = 1.5f;

    /// <summary>Нижняя граница стартовой скорости (тайл/с): иначе медленный спринт давал бы вялый подкат.</summary>
    [DataField]
    public float MinStartSpeed = 8.5f;

    /// <summary>Верхняя граница стартовой скорости: спринт с баффами не должен запускать через полкомнаты.</summary>
    [DataField]
    public float MaxStartSpeed = 11f;

    /// <summary>Страховка: скольжение не дольше этого, даже если профиль растянула слизь.</summary>
    [DataField]
    public TimeSpan Timeout = TimeSpan.FromSeconds(2.5);

    [DataField]
    public TimeSpan StandUpDelay = TimeSpan.FromSeconds(0.2);

    /// <summary>Доля от стартовой скорости, начиная с которой касание препятствия — удар с нокдауном.</summary>
    [DataField]
    public float ImpactSpeedFraction = 0.25f;

    /// <summary>
    /// Застревание: фактическое смещение за тик меньше этой доли от ожидаемого два тика подряд —
    /// тоже препятствие (на случай, если контакт не дал StartCollideEvent).
    /// </summary>
    [DataField]
    public float StallFraction = 0.2f;

    [DataField]
    public TimeSpan WallKnockdown = TimeSpan.FromSeconds(2);

    [DataField]
    public DamageSpecifier WallDamage = new()
    {
        DamageDict = new() { { "Blunt", 5 } },
    };

    /// <summary>Шанс тарана у полностью измотанного подкатывающего.</summary>
    [DataField]
    public float TackleChanceBase = 0.05f;

    /// <summary>Прибавка к шансу у здорового и свежего (множится на среднее долей ХП и стамины).</summary>
    [DataField]
    public float TackleChanceBonus = 0.10f;

    [DataField]
    public float TackleRadius = 0.4f;

    [DataField]
    public TimeSpan TackleTargetParalyze = TimeSpan.FromSeconds(3);

    [DataField]
    public DamageSpecifier TackleTargetDamage = new()
    {
        DamageDict = new() { { "Blunt", 10 } },
    };

    /// <summary>На сколько тайлов отлетает предмет из активной руки сбитого.</summary>
    [DataField]
    public float TackleItemFlingDistance = 1f;

    [DataField]
    public TimeSpan TackleSelfStun = TimeSpan.FromSeconds(1);

    [DataField]
    public DamageSpecifier TackleSelfDamage = new()
    {
        DamageDict = new() { { "Blunt", 2 } },
    };
}
