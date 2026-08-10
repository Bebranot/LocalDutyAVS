// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Trauma.Events;

/// <summary>
/// _Duty: разовая вспышка боли на экране получателя — та же красная пульсирующая виньетка, что
/// и при низком HP, но короткая и не завязанная на урон.
///
/// Шлётся адресно (RaiseNetworkEvent на конкретного игрока), потому что это ощущение пациента,
/// а не состояние мира: постороннему рядом ничего не показывается.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyPainFlashEvent : EntityEventArgs
{
    /// <summary>Сила вспышки, 0..1 — как <c>PainLevel</c> у ванильного оверлея урона.</summary>
    public float Intensity;

    /// <summary>Сколько секунд гаснет.</summary>
    public float Duration;

    public DutyPainFlashEvent(float intensity = 0.7f, float duration = 1.2f)
    {
        Intensity = intensity;
        Duration = duration;
    }
}
