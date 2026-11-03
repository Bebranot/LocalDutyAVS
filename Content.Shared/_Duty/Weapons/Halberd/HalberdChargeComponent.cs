using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Weapons.Halberd;

/// <summary>
/// Компонент рывка алебарды. Вешается на алебарду. Здесь только параметры способности и серверная
/// служебка — состояние идущего рывка живёт на самом бегущем (<see cref="HalberdChargingComponent"/>),
/// потому что предсказывается именно его тело.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class HalberdChargeComponent : Component
{
    // ── Action ────────────────────────────────────────────────

    [DataField]
    public EntProtoId ChargeActionId = "ActionDutyHalberdCharge";

    [DataField]
    public EntityUid? ChargeActionEntity;

    /// <summary>Сохранённое время окончания кулдауна рывка — переживает unwield/wield (баг с пересозданием Action).</summary>
    [DataField]
    public TimeSpan ChargeCooldownEnd = TimeSpan.Zero;

    // ── Параметры способности ─────────────────────────────────

    /// <summary>Максимальная дистанция рывка в тайлах.</summary>
    [DataField]
    public float ChargeDistance = 12f;

    /// <summary>Скорость рывка (метров/сек).</summary>
    [DataField]
    public float ChargeSpeed = 18f;

    /// <summary>Урон при попадании в сущность (Slash).</summary>
    [DataField]
    public float ChargeDamage = 125f;

    /// <summary>Резист ко всем типам урона во время рывка (0.0 - 1.0).</summary>
    [DataField]
    public float ChargeResistance = 0.65f;

    /// <summary>Стан при попадании в стену (секунды).</summary>
    [DataField]
    public float KnockdownOnHitWall = 8f;

    /// <summary>Стан при промахе (секунды).</summary>
    [DataField]
    public float KnockdownOnMiss = 2f;

    /// <summary>Замедление при попадании в моба — длительность (секунды). Персонаж не падает, но замедляется.</summary>
    [DataField]
    public float HitSlowdownDuration = 3f;

    /// <summary>Множитель скорости при попадании в моба (0.5 = в два раза медленнее).</summary>
    [DataField]
    public float HitSlowdownSpeedModifier = 0.5f;

    /// <summary>Радиус, в котором рывок цепляет моба перед собой (тайлы).</summary>
    [DataField]
    public float HitRadius = 0.65f;

    /// <summary>
    /// Упор в препятствие: за тик тело сместилось меньше этой доли от заданного два тика подряд.
    /// Скольжение вдоль стены теряет лишь долю скорости и упором не считается — только лобовой удар.
    /// </summary>
    [DataField]
    public float StallFraction = 0.35f;

    /// <summary>Звук рывка — зацикленный, играет всё время чарджа, останавливается вручную. Заглушка — звук шагов поставит пользователь позже.</summary>
    [DataField]
    public SoundSpecifier ChargeLoopSound = new SoundPathSpecifier("/Audio/_Duty/Weapons/Halberd/HalberdCharge.ogg", AudioParams.Default.WithLoop(true));

    [DataField]
    public SoundSpecifier HitSound = new SoundPathSpecifier("/Audio/Weapons/slash.ogg");

    [DataField]
    public SoundSpecifier WallSound = new SoundPathSpecifier("/Audio/Effects/metal_slam1.ogg");

    // ── Серверная служебка ────────────────────────────────────

    /// <summary>Активный зацикленный звук рывка — нужно остановить вручную по окончании.</summary>
    [ViewVariables]
    public EntityUid? ChargeAudioStream;

    /// <summary>
    /// Кто держит алебарду wielded прямо сейчас. Нужно на ComponentShutdown: алебарду могли
    /// удалить или уничтожить, минуя обычный unwield, — тогда рывок и маркер снимаются отсюда.
    /// </summary>
    [ViewVariables]
    public EntityUid? WieldedBy;
}

/// <summary>
/// Идущий рывок — висит на бегущем от старта до конца рывка. Пока он есть, ввод движения
/// заблокирован, а скорость тела ведёт <see cref="HalberdChargeController"/>. Сетевой, с дельтами:
/// клиент предсказывает свой рывок и должен откатываться к тем же значениям, что у сервера.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true), AutoGenerateComponentPause]
public sealed partial class HalberdChargingComponent : Component
{
    [AutoNetworkedField]
    public EntityUid Halberd;

    /// <summary>Единичный вектор в мировых координатах, фиксируется на старте.</summary>
    [AutoNetworkedField]
    public Vector2 Direction;

    /// <summary>Мировая точка старта — от неё меряется пройденное вдоль направления.</summary>
    [AutoNetworkedField]
    public Vector2 Origin;

    [AutoNetworkedField]
    public float Distance;

    [AutoNetworkedField]
    public float Speed;

    /// <summary>Доля урона, которую рывок гасит.</summary>
    [AutoNetworkedField]
    public float Resistance;

    [AutoNetworkedField]
    public Vector2 LastPosition;

    /// <summary>Скорость, заданная в прошлом тике: по ней ждём смещения и ловим упор.</summary>
    [AutoNetworkedField]
    public float LastSpeed;

    [AutoNetworkedField]
    public int StallTicks;

    /// <summary>Страховка: позже этого рывок заканчивается в любом случае.</summary>
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan Deadline;
}

/// <summary>
/// Маркер на пользователе, пока он держит wielded-алебарду (не только во время рывка). Даёт
/// directed-подписку на события дропа/стана/дизарма НА ЮЗЕРЕ — эти события движок всегда шлёт на
/// того, с кем это происходит, а не на предмет в его руках. Сетевой: дроп при нокдауне клиент
/// предсказывает и должен знать, что алебарду ронять нельзя.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HalberdWieldedComponent : Component
{
    /// <summary>Какую именно алебарду держит юзер — нужно для TryDrop при дизарме.</summary>
    [AutoNetworkedField]
    public EntityUid Halberd;
}

public enum HalberdChargeEndReason : byte
{
    HitEntity,
    Wall,
    Miss,
    KnockedDown,

    /// <summary>Рывок сорвался без последствий: смерть/крит, потеря алебарды, невесомость.</summary>
    Aborted,
}
