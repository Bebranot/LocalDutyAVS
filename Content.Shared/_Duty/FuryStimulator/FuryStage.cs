using Robust.Shared.Serialization;

namespace Content.Shared._Duty.FuryStimulator;

/// <summary>
/// _Duty: стадия действия стимулятора Fury-16, вычисляется из текущего уровня вещества.
/// Порядок важен: чем выше значение, тем «раньше» стадия по ходу действия (Intro — самый пик уровня).
/// </summary>
[Serializable, NetSerializable]
public enum FuryStage : byte
{
    /// <summary>Вещества нет — эффект неактивен.</summary>
    None = 0,

    /// <summary>Выход из организма (0–5). Тревожная атмосфера, музыка «разогрева», баффов нет.</summary>
    Washout = 1,

    /// <summary>Спад (5–25). Баффы/дебаффы вдвое слабее пика.</summary>
    Decline = 2,

    /// <summary>Пик действия (25–45). Полные баффы/дебаффы.</summary>
    Peak = 3,

    /// <summary>Ввод (45–50). Разогрев, дебаффы восприятия, баффов нет.</summary>
    Intro = 4,
}
