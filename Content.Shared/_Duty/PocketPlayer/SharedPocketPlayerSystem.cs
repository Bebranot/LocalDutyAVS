// SPDX-FileCopyrightText: 2025 LocalDuty
// SPDX-License-Identifier: MIT

using Robust.Shared.Audio.Systems;

namespace Content.Shared._Duty.PocketPlayer;

public abstract class SharedPocketPlayerSystem : EntitySystem
{
    [Dependency] protected readonly SharedAudioSystem Audio = default!;

    /// <summary>
    /// Громкость, которую считаем практически неразличимой на слух — форсируется,
    /// когда слайдер выкручен ровно в 0 (MinVolume сам по себе — не мут, а «тихо»).
    /// </summary>
    public const float MuteVolumeDb = -100f;

    /// <summary>
    /// Линейно переводит значение слайдера громкости (0..100 по умолчанию) в диапазон дБ аудиопотока.
    /// </summary>
    public static float MapToRange(float value, float leftMin, float leftMax, float rightMin, float rightMax)
    {
        return rightMin + (value - leftMin) * (rightMax - rightMin) / (leftMax - leftMin);
    }

    /// <summary>
    /// Переводит значение слайдера громкости в дБ для конкретного плеера.
    /// На минимуме слайдера (0) форсирует <see cref="MuteVolumeDb"/> вместо MinVolume,
    /// чтобы «громкость на 0» реально означала тишину, а не просто «тихо».
    /// </summary>
    public static float GetVolumeDb(float volume, PocketPlayerComponent comp)
    {
        if (volume <= comp.MinSlider)
            return MuteVolumeDb;

        return MapToRange(volume, comp.MinSlider, comp.MaxSlider, comp.MinVolume, comp.MaxVolume);
    }
}
