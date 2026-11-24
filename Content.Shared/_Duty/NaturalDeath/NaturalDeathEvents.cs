using Content.Shared.Mobs;
using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.NaturalDeath;

/// <summary>
/// _Duty: сетевое событие клиенту о начале естественной смерти (плавное угасание зрения и слуха).
/// </summary>
[Serializable, NetSerializable]
public sealed class NaturalDeathStartedEvent : EntityEventArgs
{
    public NetEntity Target { get; }
    public float FadeDuration { get; }
    public SoundSpecifier TinnitusSound { get; }
    public SoundSpecifier FinalBeatSound { get; }

    public NaturalDeathStartedEvent(
        NetEntity target,
        float fadeDuration,
        SoundSpecifier tinnitusSound,
        SoundSpecifier finalBeatSound)
    {
        Target = target;
        FadeDuration = fadeDuration;
        TinnitusSound = tinnitusSound;
        FinalBeatSound = finalBeatSound;
    }
}

/// <summary>
/// _Duty: сетевое событие клиенту об отмене/сбросе сцены естественной смерти (реанимация, гиб, рейджув).
/// </summary>
[Serializable, NetSerializable]
public sealed class NaturalDeathCancelledEvent : EntityEventArgs
{
    public bool FadeOut { get; }

    public NaturalDeathCancelledEvent(bool fadeOut)
    {
        FadeOut = fadeOut;
    }
}

/// <summary>
/// _Duty: локальное серверное событие перед штатным Deathgasp при переходе в Dead,
/// позволяющее NaturalDeathSystem заменить стандартный эмоут на естественную смерть без дубля в чат.
/// </summary>
[ByRefEvent]
public record struct BeforeNaturalDeathgaspEvent(EntityUid Target, MobState OldMobState, bool Cancelled = false);
