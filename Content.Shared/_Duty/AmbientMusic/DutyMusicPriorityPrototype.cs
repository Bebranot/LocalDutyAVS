// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.AmbientMusic;

/// <summary>
/// _Duty: правило приоритета звука для арбитра музыки.
///
/// Пока играет глобальный звук, чей путь начинается с одного из <see cref="PathPrefixes"/>,
/// динамическая музыка молчит. Список задаётся в YAML, чтобы добавить новый «глушащий» звук
/// можно было без правок C#.
/// </summary>
[Prototype("dutyMusicPriority")]
public sealed partial class DutyMusicPriorityPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Префиксы путей к звукам. Сравнение по началу строки, с учётом регистра — пути в
    /// прототипах и в <c>AudioComponent.FileName</c> пишутся одинаково.
    /// </summary>
    [DataField(required: true)]
    public string[] PathPrefixes = [];

    /// <summary>
    /// Чем больше, тем важнее. У динамической музыки приоритет 0, поэтому любое правило с
    /// положительным приоритетом её глушит.
    /// </summary>
    [DataField]
    public int Priority = 1;

    /// <summary>
    /// Сколько ещё держать тишину после того, как звук закончился. Нужно, чтобы музыка не
    /// врывалась в последнюю секунду затухающего объявления.
    /// </summary>
    [DataField]
    public TimeSpan HoldAfter = TimeSpan.FromSeconds(2);
}
