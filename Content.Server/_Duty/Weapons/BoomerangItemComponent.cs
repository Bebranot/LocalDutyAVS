using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Weapons;

[RegisterComponent]
public sealed partial class BoomerangItemComponent : Component
{
    // ───────────────── Настраиваемые параметры (только YAML) ───────────────

    /// <summary>Скорость возврата, тайлов/сек.</summary>
    [DataField("returnSpeed")]
    public float ReturnSpeed = 10f;

    /// <summary>Задержка перед возвратом, сек.</summary>
    [DataField("returnDelay")]
    public float ReturnDelay = 1.2f;

    /// <summary>Угловая скорость вращения в полёте, градусов/сек.</summary>
    [DataField("spinStrength")]
    public float SpinStrength = 720f;

    /// <summary>Зацикленный звук во время полёта.</summary>
    [DataField("flightSound")]
    public SoundSpecifier? FlightSound = null;

    // ───────────────── Runtime-состояние (не трогать в YAML) ───────────────

    public EntityUid? Thrower = null;
    public TimeSpan ReturnAt = TimeSpan.Zero;
    public bool WaitingForReturn = false;
    public bool IsReturning = false;
    public EntityUid? FlightSoundEntity = null;
}
