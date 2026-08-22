// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Trauma.Events;

/// <summary>
/// _Duty: DoAfter быстрой остановки артерии фабричным жгутом на себе. Одноэтапный — в отличие от
/// пошагового <c>ArterialTreatmentDoAfterEvent</c>, которым лечат руками и самоделкой.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class ArterialTourniquetDoAfterEvent : SimpleDoAfterEvent
{
}
