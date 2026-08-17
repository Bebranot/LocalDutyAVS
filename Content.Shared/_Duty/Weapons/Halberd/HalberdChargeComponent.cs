using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Weapons.Halberd;

/// <summary>
/// Компонент рывка алебарды. Вешается на алебарду.
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

    /// <summary>Кулдаун способности.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(25);

    /// <summary>Резист ко всем типам урона во время рывка (0.0 - 1.0).</summary>
    [DataField]
    public float ChargeResistance = 0.65f;

    /// <summary>Стан при попадании в сущность (секунды).</summary>
    [DataField]
    public float KnockdownOnHitEntity = 5f;

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

    /// <summary>Звук рывка — зацикленный, играет всё время чарджа, останавливается вручную. Заглушка — звук шагов поставит пользователь позже.</summary>
    [DataField]
    public SoundSpecifier ChargeLoopSound = new SoundPathSpecifier("/Audio/_Duty/Weapons/Halberd/HalberdCharge.ogg", AudioParams.Default.WithLoop(true));

    // ── Состояние рывка (рантайм) ─────────────────────────────

    public bool IsCharging = false;
    public EntityUid? ChargeUser = null;
    public System.Numerics.Vector2 ChargeDirection = System.Numerics.Vector2.Zero;
    public System.Numerics.Vector2 ChargeStartPos = System.Numerics.Vector2.Zero;

    /// <summary>Активный зацикленный звук рывка — нужно остановить вручную при StopCharge.</summary>
    public EntityUid? ChargeAudioStream = null;

    /// <summary>
    /// Кто держит алебарду wielded прямо сейчас (шире, чем <see cref="ChargeUser"/> — тот
    /// только на время самого рывка). Нужно на ComponentShutdown: снять
    /// <see cref="HalberdWieldedComponent"/> с юзера, если алебарду удалили/уничтожили, не
    /// дожидаясь обычного ItemUnwieldedEvent.
    /// </summary>
    public EntityUid? WieldedBy = null;
}

/// <summary>
/// Временный компонент-маркер, вешается на пользователя во время рывка.
/// Используется для применения резиста к урону.
/// </summary>
[RegisterComponent]
public sealed partial class HalberdChargeResistComponent : Component
{
    /// <summary>Доля урона которая блокируется (0.65 = 65% резист).</summary>
    public float Resistance = 0.65f;

    /// <summary>CanCollide до рывка (восстанавливается по окончании).</summary>
    public bool HadCanCollide;

    public bool CanCollideBefore;
}

/// <summary>
/// Компонент-маркер: вешается на пользователя, пока он держит wielded-алебарду (не только во
/// время рывка). Даёт directed-подписку на события дропа/стана/дизарма НА ЮЗЕРЕ — эти события
/// движок всегда шлёт на того, с кем это происходит, а не на предмет в его руках.
/// </summary>
[RegisterComponent]
public sealed partial class HalberdWieldedComponent : Component
{
    /// <summary>Какую именно алебарду держит юзер — нужно для TryDrop при дизарме.</summary>
    public EntityUid Halberd;
}
