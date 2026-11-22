// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Cyberware.Gorlex;

/// <summary>_Duty: переключатель термовизора (действие импланта, поднимается на имплант).</summary>
public sealed partial class DutyGorlexThermalToggleEvent : InstantActionEvent;

/// <summary>_Duty: «допрос» чёрного ящика мультитулом завершён.</summary>
[Serializable, NetSerializable]
public sealed partial class DutyBlackBoxInterrogateDoAfterEvent : SimpleDoAfterEvent;
