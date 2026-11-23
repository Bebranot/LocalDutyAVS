// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Video;

/// <summary>
/// «Видео» — анимированный RSI-слой со звуком (звук и удаление — EmitSoundOnSpawn + TimedDespawn в прототипе).
/// Время старта сетевое, чтобы клиент, увидевший сущность позже (PVS), перемотал анимацию
/// туда же, где сейчас звук, а не начинал ролик с первого кадра.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class DutyVideoComponent : Component
{
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan StartTime;

    /// <summary>Ключ слоя спрайта с роликом.</summary>
    [DataField]
    public string Layer = "video";
}
