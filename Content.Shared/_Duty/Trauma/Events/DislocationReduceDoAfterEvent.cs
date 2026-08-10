// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Trauma.Events;

/// <summary>
/// _Duty: DoAfter вправления вывиха (голыми руками). Шанс успеха зависит от того, вправляет ли
/// пациент себя сам или помогает другой игрок — он фиксируется на старте и несётся в событии.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class DislocationReduceDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public float SuccessChance;

    public DislocationReduceDoAfterEvent()
    {
    }

    public DislocationReduceDoAfterEvent(float successChance)
    {
        SuccessChance = successChance;
    }
}
