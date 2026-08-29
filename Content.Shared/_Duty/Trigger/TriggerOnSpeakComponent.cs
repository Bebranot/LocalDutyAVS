using Content.Shared.Trigger.Components.Triggers;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Trigger;

/// <summary>
/// Срабатывает, когда говорит сама сущность или тот, кто её носит.
/// В отличие от <c>TriggerOnVoiceComponent</c> реагирует на любую речь, а не на ключевую фразу.
/// Порт из Goob-Station (Content.Goobstation.Server/Explosion) под наш Trigger API.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TriggerOnSpeakComponent : BaseTriggerOnXComponent
{
    /// <summary>
    /// Радиус, в котором сущность слышит речь.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ListenRange = 4f;
}
