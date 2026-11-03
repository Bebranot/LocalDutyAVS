using Content.Shared.ADT.CartridgeLoader.Cartridges;
using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Server.ADT.CartridgeLoader.Cartridges;

[RegisterComponent, Access(typeof(NanoChatCartridgeSystem))]
public sealed partial class NanoChatCartridgeComponent : Component
{
    /// <summary>
    ///     Station entity to keep track of.
    /// </summary>
    [DataField]
    public EntityUid? Station;

    /// <summary>
    ///     The NanoChat card to keep track of.
    /// </summary>
    [DataField]
    public EntityUid? Card;

    /// <summary>
    ///     Cached station contact directory, rebuilt only when something that could
    ///     change it happens (see <see cref="NanoChatCartridgeSystem.UpdateUI"/>).
    ///     Avoids rescanning every NanoChat card + ID card on the station on every
    ///     single chat click.
    /// </summary>
    public List<NanoChatRecipient>? CachedContacts;

    /// <summary>
    ///     The <see cref="RadioChannelPrototype" /> required to send or receive messages.
    /// </summary>
    [DataField]
    public ProtoId<RadioChannelPrototype> RadioChannel = "Common";
}
