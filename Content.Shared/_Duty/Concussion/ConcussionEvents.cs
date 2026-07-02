using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Concussion;

public enum ConcussionImpulseType : byte
{
    /// <summary>Короткое моргание экрана от выстрела рядом.</summary>
    Shot,

    /// <summary>Резкое затемнение с долгим fade-out от взрыва.</summary>
    Blast,
}

/// <summary>
/// _Duty: разовый визуально-звуковой «удар» по конкретному игроку. Косметика —
/// шлётся направленно на клиент жертвы, не предсказывается и не хранится в стейте.
/// </summary>
[Serializable, NetSerializable]
public sealed class ConcussionImpulseEvent : EntityEventArgs
{
    public ConcussionImpulseType Type;

    /// <summary>Нормированная сила (0..1) от текущей шкалы — масштабирует яркость/громкость.</summary>
    public float Intensity;

    public ConcussionImpulseEvent(ConcussionImpulseType type, float intensity)
    {
        Type = type;
        Intensity = intensity;
    }
}
