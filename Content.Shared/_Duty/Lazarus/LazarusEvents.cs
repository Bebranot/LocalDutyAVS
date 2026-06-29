using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Lazarus;

/// <summary>
/// Серверное событие, отправляемое конкретному клиенту, у которого сработал
/// эффект Лазаруса. Запускает клиентскую кинематику: затемнение → музыка →
/// рукописная фраза → виньетка. Тайминги передаются с сервера, чтобы их можно
/// было тюнить через <see cref="LazarusComponent"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class LazarusTriggeredEvent : EntityEventArgs
{
    public float BlackoutFadeIn;
    public float BlackoutHold;
    public float BlackoutFadeOut;
    public float VignetteDuration;
    public float VignetteFadeOut;

    /// <summary>Сердцебиение — звук-подводка, играет первым.</summary>
    public SoundSpecifier? Heartbeat;

    /// <summary>Громкость сердцебиения, дБ.</summary>
    public float HeartbeatVolume;

    /// <summary>Основной звук "Last Standing", вступает с задержкой.</summary>
    public SoundSpecifier? LastStand;

    /// <summary>Громкость основного звука, дБ.</summary>
    public float LastStandVolume;

    /// <summary>Задержка вступления основного звука относительно сердцебиения, сек.</summary>
    public float LastStandDelay;
}
