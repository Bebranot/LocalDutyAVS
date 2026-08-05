using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Parry.Components;

/// <summary>
/// Состояние участника QTE-дуэли. Висит на обоих участниках, синхронизируется — клиент по нему
/// рисует катсцену (зум, виньетка, скрытие HUD, подсказки, шкала) и играет музыку.
/// Последовательности подсказок у участников независимые: каждый видит только свою текущую.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class QteParticipantComponent : Component
{
    /// <summary>Второй участник дуэли.</summary>
    [AutoNetworkedField]
    public EntityUid Opponent;

    /// <summary>
    /// Сущность-координатор дуэли. Не сетевое — нужно только серверу, чтобы по участнику
    /// найти его дуэль при обработке входящего нажатия.
    /// </summary>
    public EntityUid Duel;

    /// <summary>Индекс трека в коллекции DutyQteSong — выбирается сервером один раз, чтобы оба слышали одно и то же.</summary>
    [AutoNetworkedField]
    public int MusicTrack;

    [AutoNetworkedField]
    public QteStage Stage;

    /// <summary>Текущая клавиша-подсказка (этапы 1-2).</summary>
    [AutoNetworkedField]
    public QtePromptKey CurrentPrompt;

    /// <summary>Когда показана текущая подсказка — для анимации таймера у клиента.</summary>
    [AutoNetworkedField]
    public TimeSpan PromptStart;

    /// <summary>Дедлайн текущей подсказки (окно рандомное, 0.5-0.8с).</summary>
    [AutoNetworkedField]
    public TimeSpan PromptEnd;

    /// <summary>Номер текущей подсказки в этапе (для индикатора прогресса).</summary>
    [AutoNetworkedField]
    public int PromptIndex;

    /// <summary>Всего подсказок в текущем этапе.</summary>
    [AutoNetworkedField]
    public int PromptTotal;

    /// <summary>Сколько подсказок засчитано за оба этапа — тай-брейкер при точной ничьей на этапе 3.</summary>
    [AutoNetworkedField]
    public int Hits;

    /// <summary>Начало сжатия шкалы на этапе 3.</summary>
    [AutoNetworkedField]
    public TimeSpan FinalStart;

    /// <summary>Момент идеального клика — кольцо полностью сошлось на контуре кнопки.</summary>
    [AutoNetworkedField]
    public TimeSpan FinalPerfect;

    /// <summary>После этого момента этап 3 считается проваленным.</summary>
    [AutoNetworkedField]
    public TimeSpan FinalDeadline;

    /// <summary>Игрок уже кликнул на этапе 3 (повторные клики игнорируются).</summary>
    [AutoNetworkedField]
    public bool FinalAnswered;

    /// <summary>Что показать на экране итога — заполняется при развязке дуэли.</summary>
    [AutoNetworkedField]
    public QteOutcome Outcome;

    /// <summary>
    /// До этого момента кнопка горит красным. Ставится сервером на каждом промахе, чтобы
    /// провал читался сразу, а не только по итогу дуэли.
    /// </summary>
    [AutoNetworkedField]
    public TimeSpan MissFlashUntil;

    /// <summary>Когда экран итога погаснет — клиенту для анимации, демонтаж ведёт сервер.</summary>
    [AutoNetworkedField]
    public TimeSpan ResultUntil;

    /// <summary>
    /// Атакующий из последнего AttackedEvent — кэш для отличения ближнего удара от выстрела
    /// в следующем за ним DamageModifyEvent. Не сетевое, нужно только серверу.
    /// </summary>
    public EntityUid? PendingMeleeAttacker;
}
