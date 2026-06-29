using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Duty.Weapons.UnknownSubstanceFlacon;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class UnknownSubstanceFlaconComponent : Component
{
    [DataField(required: true)]
    public List<UnknownSubstanceFlaconManifestation> Manifestations = new();

    [DataField, AutoNetworkedField]
    public string? CurrentManifestationId;

    [DataField, AutoNetworkedField]
    public string CurrentSpriteState = "flacon";

    [DataField, AutoNetworkedField]
    public int HermesCharge;

    [DataField, AutoNetworkedField]
    public List<string> StudiedManifestations = new();

    [DataField, AutoNetworkedField]
    public int GuaranteedScytheHitsRemaining;

    [DataField, AutoNetworkedField]
    public bool AwaitingScytheSpeech;

    [DataField]
    public List<string> StudySpeech = new()
    {
        "Раз.",
        "Два.",
        "Труа.",
        "Четыре.",
        "Пять.",
        "Шесть.",
        "Семь. Осталось два.",
        "Восемь. Остался один.",
        "Исполнено.",
    };

    [DataField]
    public string SeventhStudyDelayedSpeech = "Тю э бель а форс д'этре.";

    [DataField]
    public float SeventhStudyDelayedSpeechDelay = 1.5f;

    [DataField]
    public string ScytheSpeech = "Рассечён и разорван, пока даже форма не исчезнет.";

    [DataField]
    public int MaxHermesCharge = 9;

    [DataField]
    public int GuaranteedScytheHits = 3;

    [DataField]
    public string ScytheManifestationId = "scythe";

    [DataField]
    public string OpenSpriteState = "flacon-open";

    [DataField]
    public string ClosedSpriteState = "flacon";

    [DataField]
    public float ClosedAttackRate = 2f;

    [DataField]
    public float ClosedRange = 0.5f;

    [DataField]
    public float ClosedAngle = 60f;

    [DataField]
    public EntProtoId ClosedAnimation = "WeaponArcPunch";

    [DataField]
    public EntProtoId ClosedWideAnimation = "WeaponArcPunch";

    [DataField]
    public float ClosedWideAnimationRotation;

    [DataField]
    public DamageSpecifier ClosedDamage = new()
    {
        DamageDict = new()
        {
            { "Blunt", 1 },
        },
    };

    [DataField]
    public string? BaseDescription;
}

[DataDefinition]
public sealed partial class UnknownSubstanceFlaconManifestation
{
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField(required: true)]
    public string Description = string.Empty;

    [DataField(required: true)]
    public DamageSpecifier Damage = default!;

    [DataField(required: true)]
    public float Range;

    [DataField(required: true)]
    public float Angle;

    [DataField(required: true)]
    public EntProtoId Animation;

    [DataField(required: true)]
    public EntProtoId WideAnimation;

    [DataField(required: true)]
    public float WideAnimationRotation;

    [DataField(required: true)]
    public string SpriteState = string.Empty;

    [DataField]
    public float AttackRate = 1f;
}

[NetSerializable]
public enum UnknownSubstanceFlaconVisuals : byte
{
    State,
}
