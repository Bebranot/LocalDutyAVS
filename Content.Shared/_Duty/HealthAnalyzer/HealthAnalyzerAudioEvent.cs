using Content.Shared.Mobs;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.HealthAnalyzer;

/// <summary>
/// _Duty: команда клиенту проиграть зацикленный звук состояния сканируемой цели
/// (сердцебиение / софткрит / крит) в анализаторе здоровья. Порт из Lost Paradise (#297).
/// </summary>
[Serializable, NetSerializable]
public sealed class HealthAnalyzerAudioEvent : EntityEventArgs
{
    public MobState State;
    public bool ForceRestart;

    public HealthAnalyzerAudioEvent(MobState state, bool forceRestart = false)
    {
        State = state;
        ForceRestart = forceRestart;
    }
}

[Serializable, NetSerializable]
public sealed class HealthAnalyzerStopAudioEvent : EntityEventArgs
{
}
