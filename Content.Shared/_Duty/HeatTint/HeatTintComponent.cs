// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx, изначально взято оттуда из Goob-Station (AGPL-3.0-or-later).

namespace Content.Shared._Duty.HeatTint;

/// <summary>
/// _Duty: перекрашивает спрайт (или отдельные его слои) по температуре сущности —
/// интерполяция по градиенту цветов в пространстве OkLab (см. <see cref="SharedHeatTintSystem"/>).
/// </summary>
[RegisterComponent]
public sealed partial class HeatTintComponent : Component
{
    /// <summary>
    /// Точки градиента, отсортированные по температуре (Кельвины).
    /// Система интерполирует между соседними точками в пространстве OkLab.
    /// Минимум 2 точки.
    /// </summary>
    [DataField(required: true)]
    public List<HeatTintPoint> ColorGradient = new();

    /// <summary>
    /// Ключи слоёв спрайта, которые нужно красить. Если null/пусто — красится весь спрайт.
    /// </summary>
    [DataField]
    public List<string>? AffectedLayers;

    public Dictionary<int, Color> BaseColors = new();

    public Dictionary<int, Color> LastAppliedColors = new();
}

[DataDefinition]
public sealed partial class HeatTintPoint
{
    [DataField(required: true)]
    public float Temperature;

    [DataField(required: true)]
    public Color Color = Color.White;
}
