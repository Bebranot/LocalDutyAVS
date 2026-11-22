// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Cyberware.Surgery;

/// <summary>
/// _Duty: завершился один этап операции. Цепочка идёт по одному doafter-у на этап, чтобы бросивший
/// операцию мог остановиться посреди (с открытой раной), а не получить всё-или-ничего.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class DutyImplantSurgeryDoAfterEvent : DoAfterEvent
{
    /// <summary>Индекс завершённого этапа в <c>InsertStages</c> / <c>RemoveStages</c>.</summary>
    [DataField]
    public int Stage;

    /// <summary>true — извлечение, false — вживление.</summary>
    [DataField]
    public bool Remove;

    /// <summary>При извлечении — какой именно имплант достаём.</summary>
    [DataField]
    public NetEntity? Implant;

    public override DoAfterEvent Clone() => this;
}
