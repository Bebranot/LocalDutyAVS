using Content.Shared.Anomaly.Effects;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Content.Shared.FixedPoint;

namespace Content.Shared.ADT.Implants;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MantisDaggersComponent : Component
{
    [AutoNetworkedField]
    public EntityUid? InnateWeapon;

    /// <summary>
    /// _Duty: прототип оружия, которое спавнится в руки при инициализации импланта.
    /// Параметризовано, чтобы Duty-клоны (см. Content.Shared._Duty.SecurityImplants)
    /// могли выдавать собственную (более слабую) сущность, не дублируя весь InitDaggers.
    /// </summary>
    [DataField]
    public EntProtoId WeaponProto = "ADTMantisDaggers";

    /// <summary>
    /// _Duty: имя контейнера для InnateWeapon — вынесено в DataField, чтобы два разных
    /// импланта на базе MantisDaggersComponent (синдикатский и Duty-клон) не делили
    /// один и тот же контейнер, если оба почему-то оказались на одном теле одновременно.
    /// </summary>
    [DataField]
    public string ContainerId = "MantisDaggersContainer";

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/ADT/Weapons/Melee/blade.ogg");

    /// <summary>
    /// The fallback sprite to be added on the original entity.
    /// </summary>
    [DataField]
    public SpriteSpecifier? FallbackSprite = new SpriteSpecifier.Rsi(new("ADT/Implants/mantis_daggers.rsi"), "hands");

    /// <summary>
    /// The key of the entity layer into which the sprite will be inserted
    /// </summary>
    [DataField]
    public string LayerMap = "mantis_daggers_layer";

    [DataField]
    [AutoNetworkedField]
    public bool Active = false;

    [AutoNetworkedField]
    public EntityUid? ActionEntity;

    public Container Container = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan EmpLastPulse = TimeSpan.Zero;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan EmpCooldown = TimeSpan.FromSeconds(10f);

    [DataField]
    public DamageSpecifier EmpDamage = new()
    {
        DamageDict = new Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2>
        {
            { "Shock", 20 },
        },
    };
}
