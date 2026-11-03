// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Drone;

public abstract class SharedDroneSystem : EntitySystem
{
    [Serializable, NetSerializable]
    public enum DroneVisuals : byte
    {
        Status,
    }

    [Serializable, NetSerializable]
    public enum DroneStatus : byte
    {
        Off,
        On,
    }
}
