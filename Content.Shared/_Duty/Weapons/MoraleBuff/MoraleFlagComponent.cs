using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Weapons.MoraleBuff;

/// <summary>
/// _Duty: вешается на алебарду ОСЩ. Пока алебарда взята в две руки (wielded),
/// владельцу выдаётся активная способность «Поднять и помахать флагом»
/// (<see cref="MoraleFlagActionEvent"/>), которая накладывает «бафф морали»
/// (<see cref="MoraleBuffComponent"/>) на кастера и живых гуманоидов рядом.
/// Логика выдачи/активации — в серверном <c>MoraleFlagSystem</c>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MoraleFlagComponent : Component
{
    // ── Action ────────────────────────────────────────────────

    [DataField]
    public EntProtoId ActionId = "ActionDutyMoraleFlag";

    [DataField]
    public EntityUid? ActionEntity;

    /// <summary>
    /// Сохранённое время окончания кулдауна — переживает unwield/wield,
    /// иначе кулдаун теряется при пересоздании Action (см. алебарду-рывок).
    /// </summary>
    [DataField]
    public TimeSpan CooldownEnd = TimeSpan.Zero;

    /// <summary>Кулдаун способности. Должен совпадать с useDelay на прототипе Action.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(120);

    // ── Параметры баффа ───────────────────────────────────────

    /// <summary>Длительность накладываемого баффа.</summary>
    [DataField]
    public TimeSpan BuffDuration = TimeSpan.FromSeconds(25);

    /// <summary>Радиус (тайлы), в котором живые гуманоиды получают бафф. Кастер баффается всегда.</summary>
    [DataField]
    public float Radius = 5f;

    /// <summary>Визуальный эффект над головой цели (восклицательный знак, ~3с).</summary>
    [DataField]
    public EntProtoId VisualEffect = "DutyMoraleBuffVisual";

    /// <summary>
    /// Звук активации. ЗАГЛУШКА — заменить на финальный ассет, когда будет путь/файл.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Items/airhorn.ogg");
}
