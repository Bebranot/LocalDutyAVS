using Content.Shared.Alert;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.ShieldBash;

/// <summary>
/// _Duty: вешается на BaseShield — даёт всем щитам в игре способность «Удар по щиту».
/// Пока щит в одной руке, а в другой — оружие (см. <see cref="SharedShieldBashSystem.IsQualifyingWeapon"/>),
/// владельцу выдаётся Action <see cref="ActionId"/>. Активация вешает на владельца
/// <see cref="ShieldBashBuffComponent"/> на случайное время и запускает личный (не привязанный
/// к предмету) кулдаун — см. <see cref="ShieldBasherComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ShieldBashComponent : Component
{
    // ── Action ────────────────────────────────────────────────

    [DataField]
    public EntProtoId ActionId = "ActionDutyShieldBash";

    [DataField]
    public EntityUid? ActionEntity;

    // ── Гейт «оружие в другой руке» ─────────────────────────────

    /// <summary>
    /// Минимальный суммарный базовый урон MeleeWeaponComponent, чтобы предмет в другой руке
    /// считался «настоящим оружием» (безоружный удар — 5, кухонный нож — 10, ровно проходит).
    /// Любой GunComponent считается оружием без проверки урона.
    /// </summary>
    [DataField]
    public int MinMeleeDamage = 10;

    // ── Кулдаун ──────────────────────────────────────────────────

    /// <summary>Личный кулдаун способности (хранится на владельце, не на предмете).</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(90);

    // ── Параметры баффа ───────────────────────────────────────

    [DataField]
    public TimeSpan MinBuffDuration = TimeSpan.FromSeconds(10);

    [DataField]
    public TimeSpan MaxBuffDuration = TimeSpan.FromSeconds(20);

    /// <summary>Резист ко всему входящему урону (0.15 = -15%).</summary>
    [DataField]
    public float DamageResist = 0.15f;

    /// <summary>Множитель скорости передвижения (1.05 = +5%).</summary>
    [DataField]
    public float SpeedModifier = 1.05f;

    /// <summary>Множитель урона оружия ближнего боя в свободной руке (1.2 = +20%).</summary>
    [DataField]
    public float MeleeDamageMultiplier = 1.2f;

    /// <summary>Множитель скорости атаки оружия ближнего боя в свободной руке (1.2 = +20%).</summary>
    [DataField]
    public float MeleeAttackRateMultiplier = 1.2f;

    [DataField]
    public ProtoId<AlertPrototype> Alert = "DutyShieldBash";

    /// <summary>Звук удара по щиту при активации.</summary>
    [DataField]
    public SoundSpecifier Sound = new SoundPathSpecifier("/Audio/_Duty/Weapons/Melee/shield_bash.ogg");
}
