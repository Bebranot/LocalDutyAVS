// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx (Content.Shared._Onyx.Drone).

using Content.Shared.Alert;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Drone;

/// <summary>
/// _Duty: ремонтный дрон-призрак-роль. Не может использовать вещи вне вайтлиста рядом с живыми
/// не-дронами, следит за зарядом батареи, гибнет при разряде.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause, AutoGenerateComponentState]
public sealed partial class DroneComponent : Component
{
    [DataField]
    public float InteractionBlockRange = 1.5f;

    [DataField]
    public TimeSpan ProximityDelay = TimeSpan.FromSeconds(2);

    [AutoPausedField]
    public TimeSpan NextProximityAlert;

    public EntityUid NearestEntity;

    [DataField, AutoNetworkedField]
    public EntityWhitelist? Whitelist;

    [DataField, AutoNetworkedField]
    public EntityWhitelist? Blacklist;

    [DataField]
    public ProtoId<AlertPrototype> BatteryAlert = "DroneBattery";

    [DataField]
    public ProtoId<AlertPrototype> NoBatteryAlert = "BorgBatteryNone";

    public short LastChargePercent;

    [AutoPausedField]
    public TimeSpan NextBatteryUpdate;
}
