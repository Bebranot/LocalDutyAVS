using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Parry;

/// <summary>Этап QTE-дуэли.</summary>
[Serializable, NetSerializable]
public enum QteStage : byte
{
    /// <summary>Этап 1 — 6 подсказок из A/W/S/D.</summary>
    Directions,

    /// <summary>Этап 2 — 4-6 подсказок из Q/T/E/R/G/F/H.</summary>
    Letters,

    /// <summary>Этап 3 (решающий) — сжимающаяся шкала, клик ПКМ.</summary>
    Final,

    /// <summary>Дуэль завершена, идёт демонтаж катсцены.</summary>
    Finished,
}

/// <summary>
/// Клавиша-подсказка QTE. Отдельный enum, а не сырой символ — чтобы состояние сериализовалось
/// компактно и клиент рисовал подпись через локаль, а не хардкод символа.
/// </summary>
[Serializable, NetSerializable]
public enum QtePromptKey : byte
{
    None,

    // Этап 1
    W,
    A,
    S,
    D,

    // Этап 2
    Q,
    T,
    E,
    R,
    G,
    F,
    H,
}

/// <summary>
/// Клиент сообщает о нажатии клавиши-подсказки на этапах 1-2.
/// Сервер сам сверяет, та ли это клавиша и уложился ли игрок в окно — присланному
/// клиентом «результату» не доверяем, только факту нажатия.
/// </summary>
[Serializable, NetSerializable]
public sealed class QtePromptInputEvent(QtePromptKey key) : EntityEventArgs
{
    public QtePromptKey Key = key;
}

/// <summary>
/// Клиент сообщает о клике ПКМ на решающем этапе 3.
/// Момент нажатия определяет сервер по времени получения, с поправкой на половину
/// собственного замера пинга игрока (клиент тайминг не присылает и подделать его не может).
/// </summary>
[Serializable, NetSerializable]
public sealed class QteFinalInputEvent : EntityEventArgs;

/// <summary>
/// Запрос на запуск QTE-дуэли. Поднимается из общей <see cref="TwoHandedBlockSystem"/>,
/// обрабатывается только серверной QteDuelSystem — так общий код может инициировать
/// катсцену, не зная о серверной машине состояний.
/// </summary>
[ByRefEvent]
public readonly record struct QteDuelStartRequestEvent(EntityUid Blocker, EntityUid Parrier);
