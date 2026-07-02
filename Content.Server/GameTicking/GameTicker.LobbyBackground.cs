using Content.Shared.GameTicking.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    [ViewVariables]
    public ProtoId<LobbyBackgroundPrototype>? LobbyBackground { get; private set; }

    private void InitializeLobbyBackground()
    {
        // LocalDuty: фон лобби — параллакс на клиенте (ParallaxDuty), картинки из lobbyBackground не используются.
        LobbyBackground = null;
    }

    private void RandomizeLobbyBackground()
    {
        LobbyBackground = null;
    }
}
