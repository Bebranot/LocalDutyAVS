using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.FuryStimulator;

/// <summary>
/// _Duty: авто-инъектор стимулятора Fury-16. Использование в руке колет себя, применение на моба —
/// колет цель. Каждый укол добавляет дозу вещества (<c>FuryStimulatorSystem.Inject</c>); повторный
/// укол по уже накачанному приводит к передозу. Логика — в серверном <c>FuryStimulatorSystem</c>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FuryStimulatorInjectorComponent : Component
{
    /// <summary>Осталось доз в инъекторе.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int Charges = 1;

    /// <summary>Удалять предмет, когда дозы кончились.</summary>
    [DataField]
    public bool DeleteWhenEmpty = true;

    /// <summary>Звук укола.</summary>
    [DataField]
    public SoundSpecifier InjectSound = new SoundPathSpecifier("/Audio/Items/hypospray.ogg");
}
