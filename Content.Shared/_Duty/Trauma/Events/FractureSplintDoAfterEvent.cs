// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Trauma.Events;

/// <summary>
/// _Duty: DoAfter наложения шины на перелом (только другим игроком). Успех стабилизирует зону,
/// провал — боль и урон пациенту.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class FractureSplintDoAfterEvent : SimpleDoAfterEvent
{
}
