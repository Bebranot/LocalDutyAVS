using Content.Shared._Duty.Heartbeat;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.HealthAnalyzer;

/// <summary>
/// _Duty: команда клиенту проигрывать сердцебиение сканируемой цели в анализаторе здоровья.
/// Звук штучными сэмплами по <see cref="Level"/> (ниже HP — чаще и тяжелее). В крите
/// (<see cref="InCrit"/>) тяжёлое сердцебиение чередуется с писком монитора и играет быстро.
/// На грани смерти (<see cref="NearDeath"/>, HP &lt; ~10%) добавляется тревога панели.
/// </summary>
[Serializable, NetSerializable]
public sealed class HealthAnalyzerAudioEvent : EntityEventArgs
{
    public HeartbeatLevel Level;
    public bool InCrit;
    public bool NearDeath;
    public bool Flatline; // цель мертва — пульса нет (тишина в анализаторе)
    public bool PlayFlatline; // одноразовый импульс: проиграть ровную линию прямо сейчас (переход жив→мёртв)
    public bool ForceRestart;

    public HealthAnalyzerAudioEvent(HeartbeatLevel level, bool inCrit, bool nearDeath, bool flatline, bool playFlatline = false, bool forceRestart = false)
    {
        Level = level;
        InCrit = inCrit;
        NearDeath = nearDeath;
        Flatline = flatline;
        PlayFlatline = playFlatline;
        ForceRestart = forceRestart;
    }
}

[Serializable, NetSerializable]
public sealed class HealthAnalyzerStopAudioEvent : EntityEventArgs
{
}
