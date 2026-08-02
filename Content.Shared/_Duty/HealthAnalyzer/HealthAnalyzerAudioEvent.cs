using Robust.Shared.Serialization;

namespace Content.Shared._Duty.HealthAnalyzer;

/// <summary>
/// _Duty: команда клиенту проигрывать звук сканируемой цели в анализаторе здоровья.
/// Вне крита анализатор МОЛЧИТ — стук кардиомонитора убран совсем. В крите
/// (<see cref="InCrit"/>) играет зацикленный критический тон, на грани смерти
/// (<see cref="NearDeath"/>, HP &lt; ~10%) добавляется тревога панели, при смерти —
/// одноразовая ровная линия.
/// </summary>
[Serializable, NetSerializable]
public sealed class HealthAnalyzerAudioEvent : EntityEventArgs
{
    public bool InCrit;
    public bool NearDeath;
    public bool Flatline; // цель мертва — пульса нет (тишина в анализаторе)
    public bool PlayFlatline; // одноразовый импульс: проиграть ровную линию прямо сейчас (переход жив→мёртв)

    public HealthAnalyzerAudioEvent(bool inCrit, bool nearDeath, bool flatline, bool playFlatline = false)
    {
        InCrit = inCrit;
        NearDeath = nearDeath;
        Flatline = flatline;
        PlayFlatline = playFlatline;
    }
}

[Serializable, NetSerializable]
public sealed class HealthAnalyzerStopAudioEvent : EntityEventArgs
{
}
