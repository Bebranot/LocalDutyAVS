namespace Content.Shared._Duty.AmbientMusic;

/// <summary>
/// Уровни динамической (Duty) фоновой музыки — соответствуют плейлистам в <c>DutyAmbientMusic</c>.
/// </summary>
/// <remarks>
/// Значения обязаны оставаться сплошными от нуля: клиентская система индексирует кэш громкостей
/// массивом по <c>(int) level</c>. Явные номера здесь задавать нельзя.
/// </remarks>
public enum DutyAmbientMusicLevel
{
    VeryGood,
    Good,
    Medium,
    BelowMedium,
    Awful,
    HpCritical,
    MobCritical,
    Combat,
    CombatLow,
    Death,
    CritEnter,
}
