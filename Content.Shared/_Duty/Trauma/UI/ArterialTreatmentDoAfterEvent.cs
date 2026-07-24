// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Trauma.UI;

/// <summary>
/// _Duty: DoAfter одного этапа лечения артерии. Несёт этап, чтобы сервер знал, что завершилось
/// (этапы выполняются последовательно, но событие общее).
/// </summary>
[Serializable, NetSerializable]
public sealed partial class ArterialTreatmentDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public ArterialTreatmentStep Step;

    public ArterialTreatmentDoAfterEvent()
    {
    }

    public ArterialTreatmentDoAfterEvent(ArterialTreatmentStep step)
    {
        Step = step;
    }
}
