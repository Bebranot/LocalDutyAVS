using Content.Shared.Actions;
using Content.Shared.ADT.CartridgeLoader.Cartridges;
using Robust.Shared.Serialization;

namespace Content.Shared.ADT.NanoChat;

[Serializable, NetSerializable]
public enum StationAiNanoChatUiKey : byte
{
    Key
}

public sealed partial class StationAiNanoChatActionEvent : InstantActionEvent
{
}

[Serializable, NetSerializable]
public sealed class StationAiNanoChatUiMessage : BoundUserInterfaceMessage
{
    public readonly NanoChatUiMessageType Type;

    public readonly uint? RecipientNumber;

    public readonly string? Content;

    public readonly string? RecipientJob;

    // _Duty: участники группового чата — окно ИИ использует тот же интерфейс, что и ПДА.
    public readonly List<uint>? GroupMembers;

    public StationAiNanoChatUiMessage(NanoChatUiMessageType type,
        uint? recipientNumber = null,
        string? content = null,
        string? recipientJob = null,
        List<uint>? groupMembers = null) // _Duty
    {
        Type = type;
        RecipientNumber = recipientNumber;
        Content = content;
        RecipientJob = recipientJob;
        GroupMembers = groupMembers; // _Duty
    }
}
