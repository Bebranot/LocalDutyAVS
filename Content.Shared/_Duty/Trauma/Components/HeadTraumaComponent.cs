// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Trauma.Components;

/// <summary>Тяжесть сотрясения мозга.</summary>
public enum HeadTraumaTier : byte
{
    /// <summary>Лёгкое — короткая дезориентация, проходит само.</summary>
    Light = 1,

    /// <summary>Среднее — симптомы волнами, ощутимый штраф.</summary>
    Medium = 2,

    /// <summary>Тяжёлое — риск кратковременной потери сознания, долгое восстановление.</summary>
    Severe = 3,
}

/// <summary>
/// _Duty: сотрясение мозга. ВАЖНО: это НЕ аудио-контузия из <c>_Duty/Concussion</c> (звон в ушах от
/// выстрелов и взрывов) — другая механика, другой неймспейс, пересекать их нельзя.
///
/// Запускается травмой головы (переломом черепа), лечится только временем. Повторный удар по голове
/// до восстановления («second impact») поднимает тяжесть нелинейно — это и реализм, и анти-абьюз
/// против добивания уже травмированного лёгкими тычками.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HeadTraumaComponent : Component
{
    /// <summary>Текущая тяжесть. Сетевая — от неё зависят предсказанные штрафы движения.</summary>
    [AutoNetworkedField]
    public HeadTraumaTier Tier = HeadTraumaTier.Light;

    /// <summary>Серверное: когда тяжесть снизится на шаг.</summary>
    [ViewVariables]
    public TimeSpan NextRecovery;

    /// <summary>
    /// Серверное: до этого момента повторная травма головы считается «вторым ударом» и
    /// поднимает тяжесть сразу на два шага.
    /// </summary>
    [ViewVariables]
    public TimeSpan SecondImpactUntil;

    /// <summary>Серверное: следующий тик симптомов (тошнота/отключки).</summary>
    [ViewVariables]
    public TimeSpan NextEffectTick;
}
