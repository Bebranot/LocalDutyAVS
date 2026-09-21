using Content.Shared.EntityEffects;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Xenobiology.Extracts;

/// <summary>
/// Spawns an area-effect entity (e.g. a smoke cloud) a while after a slime extract is used.
/// Ported from Space Onyx.
/// </summary>
public sealed partial class DelayedExtractAreaReaction : EntityEffectBase<DelayedExtractAreaReaction>
{
    [DataField(required: true)]
    public EntProtoId PrototypeId;

    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    [DataField]
    public TimeSpan Delay;

    [DataField]
    public float Duration = 10f;

    [DataField]
    public int SpreadAmount = 1;
}
