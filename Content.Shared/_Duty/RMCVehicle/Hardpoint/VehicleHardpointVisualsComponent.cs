using System;
using System.Collections.Generic;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Vehicle;

[RegisterComponent, NetworkedComponent]
public sealed partial class VehicleHardpointVisualsComponent : Component
{
    public List<VehicleHardpointLayerState> Layers = new();
}

[Serializable, NetSerializable, DataDefinition]
public partial record struct VehicleHardpointLayerState
{
    [DataField(required: true)]
    public string Layer { get; set; } = string.Empty;

    [DataField]
    public string State { get; set; } = string.Empty;

    public VehicleHardpointLayerState(string layer, string state)
    {
        Layer = layer;
        State = state;
    }
}

// _Duty: голый List<> в AppearanceData не регистрируется в NetSerializer — сервер не мог отправить
// состояние ни одному игроку рядом с техникой. Регистрируется только как поле NetSerializable-типа.
[Serializable, NetSerializable]
public sealed class VehicleHardpointLayersData : ICloneable
{
    public List<VehicleHardpointLayerState> Layers;

    public VehicleHardpointLayersData(List<VehicleHardpointLayerState> layers)
    {
        Layers = layers;
    }

    public object Clone()
    {
        return new VehicleHardpointLayersData(new List<VehicleHardpointLayerState>(Layers));
    }
}

[Serializable, NetSerializable]
public enum VehicleHardpointVisualsVisuals : byte
{
    Layers,
}
