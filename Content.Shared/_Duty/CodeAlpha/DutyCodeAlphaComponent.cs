// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Duty.CodeAlpha;

/// <summary>
/// _Duty: маркер активного кода «Альфа» на сущности станции.
///
/// Живёт ОТДЕЛЬНО от <c>AlertLevelComponent.CurrentLevel</c>: у станции всего один слот уровня
/// тревоги, и взведённая бомба принудительно ставит туда <c>delta</c>, затирая <c>alpha</c>.
/// Если бы состояние Альфы читалось из уровня, доступы и таймер отваливались бы ровно в тот
/// момент, когда экипажу надо бежать к бомбе через пол-станции. Поэтому уровень тревоги нужен
/// только ради объявления и атмосферы, а жизнь протокола определяет этот компонент.
///
/// Снимается только зелёным кодом на этой же станции либо командой <c>dutycodealpha off</c>.
/// См. <see cref="Content.Server._Duty.CodeAlpha.DutyCodeAlphaSystem"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class DutyCodeAlphaComponent : Component
{
    /// <summary>
    /// Конец отсчёта: момент активации плюс пятнадцать минут.
    ///
    /// Раньше здесь лежал ванильный <c>WarDeclaredTime + WarNukieArriveDelay</c>. Это имело смысл,
    /// пока код включался автоматически по объявлению войны и хост подтверждал его отдельным окном:
    /// пауза на подтверждение (до минуты) увела бы собственный отсчёт от активации в расхождение с
    /// прилётом оперативников. Ни автотриггера, ни окна больше нет — код объявляет админ, и сам
    /// момент объявления и есть точка отсчёта.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan Deadline;

    /// <summary>
    /// Момент объявления кода. Якорь для первого музыкального трека (стартует на +20 секунд).
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan ActivatedAt;
}
