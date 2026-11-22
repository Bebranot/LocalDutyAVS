// SPDX-FileCopyrightText: 2026 LocalDuty
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Sandevistan;

[Serializable, NetSerializable]
public enum SandevistanAudioPhase : byte
{
    /// <summary>Немедленный старт sande_activation.ogg и запуск цикла sande_idle.ogg через 3.07s.</summary>
    Activate,

    /// <summary>Удержание sande_idle.ogg ещё 0.4s, затем плавный fade_out и запуск sande_exit.ogg.</summary>
    Deactivate,

    /// <summary>Немедленная остановка всех звуков Sandevistan (ЭМИ, смерть, удаление).</summary>
    StopImmediate,
}

/// <summary>
/// _Duty: сетевое событие управления 3-фазным аудио Sandevistan на клиенте.
/// </summary>
[Serializable, NetSerializable]
public sealed class SandevistanAudioEvent : EntityEventArgs
{
    public NetEntity User;
    public SandevistanAudioPhase Phase;

    public SandevistanAudioEvent(NetEntity user, SandevistanAudioPhase phase)
    {
        User = user;
        Phase = phase;
    }
}
