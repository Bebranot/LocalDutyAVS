// SPDX-FileCopyrightText: 2025 LocalDuty
// SPDX-License-Identifier: MIT

using Robust.Shared.Audio.Systems;

namespace Content.Shared._Duty.PocketPlayer;

public abstract class SharedPocketPlayerSystem : EntitySystem
{
    [Dependency] protected readonly SharedAudioSystem Audio = default!;

    /// <summary>
    /// Линейно переводит значение слайдера громкости (0..100 по умолчанию) в диапазон дБ аудиопотока.
    /// </summary>
    public static float MapToRange(float value, float leftMin, float leftMax, float rightMin, float rightMax)
    {
        return rightMin + (value - leftMin) * (rightMax - rightMin) / (leftMax - leftMin);
    }
}
