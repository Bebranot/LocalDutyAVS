// SPDX-FileCopyrightText: 2025 LocalDuty
// SPDX-License-Identifier: MIT

using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.PocketPlayer;

/// <summary>
/// Трек для плеера Duty.
/// Чтобы добавить новую категорию — просто укажи нужное название в поле category.
/// Чтобы добавить трек — создай новый блок с type: dutyTrack и укажи category.
///
/// Пример добавления трека в новую категорию:
/// - type: dutyTrack
///   id: my_cool_song
///   name: Моя крутая песня
///   category: Моя категория   # <-- название папки в UI
///   path:
///     path: /Audio/_Duty/PlayerDK/МояПесня.ogg
/// </summary>
[Prototype("dutyTrack")]
public sealed partial class DutyTrackPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    /// <summary>
    /// Название трека — отображается в списке.
    /// </summary>
    [DataField(required: true)]
    public string Name = string.Empty;

    /// <summary>
    /// Категория (папка) в которой будет показан трек.
    /// Придумай любое название — например "Сталкер", "Рок", "Атмосфера" и т.д.
    /// Треки с одинаковым category автоматически попадут в одну папку.
    /// </summary>
    [DataField(required: true)]
    public string Category = string.Empty;

    /// <summary>
    /// Путь к .ogg файлу.
    /// </summary>
    [DataField(required: true)]
    public SoundPathSpecifier Path = default!;
}
