// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Cyberware.Mercenary;

/// <summary>_Duty: действие «Подтверждения» — эвакуация (самоустранение) после закрытого контракта.</summary>
public sealed partial class DutyMercEvacuateActionEvent : InstantActionEvent;

/// <summary>_Duty: действие «Мимикрии» — начать скан лица цели.</summary>
public sealed partial class DutyMercMimicryActionEvent : EntityTargetActionEvent;

/// <summary>_Duty: действие «Протокола стирания».</summary>
public sealed partial class DutyMercErasureActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class DutyMercMimicryDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// _Duty: адресно игроку-наёмнику — показать экран «КОНТРАКТ ЗАКРЫТ» (или «РАСТОРГНУТ»).
/// Ощущение одного человека, а не состояние мира, поэтому не компонентом.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyMercContractEndedEvent : EntityEventArgs
{
    public bool Terminated;
    public string TargetName;

    public DutyMercContractEndedEvent(bool terminated, string targetName)
    {
        Terminated = terminated;
        TargetName = targetName;
    }
}
