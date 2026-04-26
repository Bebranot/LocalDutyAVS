using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.Weapons.Melee.ComboStrike;

[RegisterComponent]
public sealed partial class ComboStrikeComponent : Component
{
    /// <summary>
    /// Сколько успешных ударов по цели нужно для активации комбо.
    /// </summary>
    [DataField(required: true)]
    public int HitsRequired = 3;

    [DataField]
    public int CurrentHits = 0;

    [DataField]
    public EntityUid? LastTarget = null;

    // ── Бонусный урон ────────────────────────────────────────────────────────

    /// <summary>
    /// Тип и количество бонусного урона при комбо.
    /// Пример в YAML:
    ///   bonusDamage:
    ///     types:
    ///       Blunt: 15
    /// </summary>
    [DataField]
    public DamageSpecifier? BonusDamage = null;

    // ── Урон выносливости ────────────────────────────────────────────────────

    /// <summary>
    /// Сколько единиц выносливости (stamina) снимается при комбо.
    /// </summary>
    [DataField]
    public float StaminaDamage = 30f;

    // ── Звук ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Звук, воспроизводимый при активации комбо.
    /// Пример в YAML:
    ///   comboSound:
    ///     path: /Audio/Weapons/smash.ogg
    /// </summary>
    [DataField]
    public SoundSpecifier? ComboSound = null;

    // ── Визуальный эффект ────────────────────────────────────────────────────

    /// <summary>
    /// ID прототипа эффекта, который спавнится на цели при комбо.
    /// Пример в YAML:
    ///   comboEffectPrototype: ComboHitEffect1
    /// </summary>
    [DataField]
    public string? ComboEffectPrototype = null;
}
