using Robust.Shared.Serialization;

namespace Content.Shared._Duty.FuryStimulator;

/// <summary>
/// _Duty: фаза действия стимулятора Fury-16. Модель — таймерная: фазы сменяют друг друга
/// по фиксированным длительностям (см. <c>FuryStimulatorComponent</c>), а не по уровню вещества.
/// Порядок значений совпадает с порядком прохождения.
/// </summary>
[Serializable, NetSerializable]
public enum FuryStage : byte
{
    /// <summary>Эффект неактивен.</summary>
    None = 0,

    /// <summary>Ввод. Баффов нет, дезориентация. По умолчанию 15 c.</summary>
    Intro = 1,

    /// <summary>Разгон (между вводом и пиком). ⅓ баффов, без дебаффа огнестрела и без иммунитета к боли. 29 c.</summary>
    RampUp = 2,

    /// <summary>Пик. Полные баффы + дебафф огнестрела + неуязвимость к боли. 30 c.</summary>
    Peak = 3,

    /// <summary>Спад. ½ пика (включая огнестрел и иммунитет к боли). 29 c.</summary>
    Decline = 4,
}
