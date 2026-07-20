// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma.Components;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Trauma;

/// <summary>Вид травмы для строки в анализаторе здоровья.</summary>
[Serializable, NetSerializable]
public enum TraumaAnalyzerKind : byte
{
    Fracture,
    Dislocation,
    DislocationResidual,
    ArterialBleed,
    HeadTrauma,
}

/// <summary>
/// _Duty: одна строка травмы для анализатора здоровья. Передаётся структурой (а не готовым
/// текстом), чтобы локализация выполнялась на клиенте.
/// </summary>
[Serializable, NetSerializable]
public struct TraumaAnalyzerEntry
{
    public TraumaAnalyzerKind Kind;

    /// <summary>Зона тела; null для беззонных травм (артериальное кровотечение).</summary>
    public BodyZone? Zone;

    /// <summary>Тир — только для <see cref="TraumaAnalyzerKind.Fracture"/>.</summary>
    public FractureTier Tier;

    /// <summary>Наложена ли шина — только для <see cref="TraumaAnalyzerKind.Fracture"/>.</summary>
    public bool Splinted;

    /// <summary>Тяжесть — только для <see cref="TraumaAnalyzerKind.HeadTrauma"/>.</summary>
    public HeadTraumaTier HeadTier;

    public TraumaAnalyzerEntry(
        TraumaAnalyzerKind kind,
        BodyZone? zone = null,
        FractureTier tier = FractureTier.Crack,
        bool splinted = false,
        HeadTraumaTier headTier = HeadTraumaTier.Light)
    {
        Kind = kind;
        Zone = zone;
        Tier = tier;
        Splinted = splinted;
        HeadTier = headTier;
    }
}
