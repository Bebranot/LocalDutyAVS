using Content.Shared.FixedPoint; // ADT-Tweak
using Content.Shared.Mobs; // _Duty
using Robust.Shared.Serialization;

namespace Content.Shared.MedicalScanner;

/// <summary>
/// On interacting with an entity retrieves the entity UID for use with getting the current damage of the mob.
/// </summary>
[Serializable, NetSerializable]
public sealed class HealthAnalyzerScannedUserMessage : BoundUserInterfaceMessage
{
    public HealthAnalyzerUiState State;

    public HealthAnalyzerScannedUserMessage(HealthAnalyzerUiState state)
    {
        State = state;
    }
}

/// <summary>
/// Contains the current state of a health analyzer control. Used for the health analyzer and cryo pod.
/// </summary>
[Serializable, NetSerializable]
public struct HealthAnalyzerUiState
{
    public readonly NetEntity? TargetEntity;
    public float Temperature;
    public float BloodLevel;
    public bool? ScanMode;
    public bool? Bleeding;
    public bool? Unrevivable;
    public List<(string ReagentId, FixedPoint2 Quantity)>? MetabolizingReagents; // ADT-Tweak - list of metabolizing reagents inside scanned user
    public MobState? MobState; // _Duty - для эмбиент-звука состояния в анализаторе
    public float? HealthFraction; // _Duty - «живучесть» (1 = полное HP, ≤0 = крит); для мигающей крит-таблички

    public HealthAnalyzerUiState(NetEntity? targetEntity, float temperature, float bloodLevel, bool? scanMode, bool? bleeding, bool? unrevivable, List<(string ReagentId, FixedPoint2 Quantity)>? metabolizingReagents = null, MobState? mobState = null, float? healthFraction = null) // Starlight - added metabolizingReagents parameter
    {
        TargetEntity = targetEntity;
        Temperature = temperature;
        BloodLevel = bloodLevel;
        ScanMode = scanMode;
        Bleeding = bleeding;
        Unrevivable = unrevivable;
        MetabolizingReagents = metabolizingReagents; // ADT-Tweak
        MobState = mobState; // _Duty
        HealthFraction = healthFraction; // _Duty
    }
}
