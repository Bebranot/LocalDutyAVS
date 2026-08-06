using Content.Shared._Duty.Parry;

namespace Content.Server._Duty.Parry;

/// <summary>
/// Серверное состояние одной QTE-дуэли. Висит на отдельной сущности-координаторе в нигде:
/// так барьеры и общий тайминг принадлежат одному владельцу, и удаление координатора
/// гарантированно демонтирует всю сцену, кто бы из участников ни исчез первым.
/// </summary>
[RegisterComponent]
public sealed partial class QteDuelComponent : Component
{
    /// <summary>Тот, кто изначально заблокировал удар и пошёл в контр-атаку.</summary>
    public QteDuelSide Blocker = new();

    /// <summary>Тот, кто спарировал контр-атаку.</summary>
    public QteDuelSide Parrier = new();

    public QteStage Stage = QteStage.Directions;

    /// <summary>Заспавненные клетки барьера — удаляются при любом завершении сцены.</summary>
    public List<EntityUid> Barriers = new();

    /// <summary>
    /// Жёсткий предохранитель: если сцена по какой-то причине не завершилась сама
    /// (баг в переходах, потеря игрока и т.п.) — принудительно демонтируем её.
    /// </summary>
    public TimeSpan Watchdog;
}

/// <summary>Состояние одной стороны дуэли.</summary>
public sealed class QteDuelSide
{
    public EntityUid Entity;

    /// <summary>Своя случайная последовательность подсказок текущего этапа (соперник её не видит).</summary>
    public List<QtePromptKey> Sequence = new();

    /// <summary>Индекс текущей подсказки в <see cref="Sequence"/>.</summary>
    public int Index;

    /// <summary>Сколько подсказок засчитано суммарно за этапы 1-2 — тай-брейкер этапа 3.</summary>
    public int Hits;

    /// <summary>Игрок уже кликнул на этапе 3 (повторные клики игнорируются).</summary>
    public bool FinalAnswered;

    /// <summary>Попал ли клик в идеальную зону.</summary>
    public bool FinalHit;

    /// <summary>Отклонение клика от идеального момента, секунды. Меньше — лучше.</summary>
    public float FinalError = float.MaxValue;
}
