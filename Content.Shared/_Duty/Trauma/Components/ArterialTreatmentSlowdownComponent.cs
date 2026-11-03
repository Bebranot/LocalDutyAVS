// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Trauma.Components;

/// <summary>
/// _Duty: замедление лечащего, пока он накладывает жгут на артерию. Вынесено из серверной сессии
/// лечения в сетевой компонент: клиент пересчитывает скорость сам (спринт, ранения, снаряжение),
/// и серверный модификатор при этом терялся — клиент бежал быстрее сервера, и позицию дёргало назад.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ArterialTreatmentSlowdownComponent : Component
{
    /// <summary>Множитель скорости ходьбы и бега (0.3 = −70%).</summary>
    [DataField, AutoNetworkedField]
    public float Modifier = 0.3f;
}
