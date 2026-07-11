using Robust.Shared.Serialization;

namespace Content.Shared._Duty.FuryStimulator;

/// <summary>
/// _Duty: ключ визуализации инъектора Fury-16. <c>Used=true</c> → пустой (использованный) спрайт,
/// <c>false</c> → полный. Переключается через <c>Appearance</c>+<c>GenericVisualizer</c> в прототипе.
/// </summary>
[Serializable, NetSerializable]
public enum FuryInjectorVisuals : byte
{
    Used,
}
