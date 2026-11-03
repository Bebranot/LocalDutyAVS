using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared.ADT.CartridgeLoader.Cartridges;

[Serializable, NetSerializable]
public sealed class NanoChatUiMessageEvent : CartridgeMessageEvent
{
    /// <summary>
    ///     The type of UI message being sent.
    /// </summary>
    public readonly NanoChatUiMessageType Type;

    /// <summary>
    ///     The recipient's NanoChat number, if applicable.
    /// </summary>
    public readonly uint? RecipientNumber;

    /// <summary>
    ///     The content of the message or name for new chats.
    /// </summary>
    public readonly string? Content;

    /// <summary>
    ///     The recipient's job title when creating a new chat.
    /// </summary>
    public readonly string? RecipientJob;

    /// <summary>
    ///     The NanoChat numbers to invite when creating a group chat. Only used for
    ///     <see cref="NanoChatUiMessageType.NewGroupChat" />, where <see cref="Content" />
    ///     carries the group's name instead of a message.
    /// </summary>
    public readonly List<uint>? GroupMembers;

    /// <summary>
    ///     Creates a new NanoChat UI message event.
    /// </summary>
    /// <param name="type">The type of message being sent</param>
    /// <param name="recipientNumber">Optional recipient number for the message</param>
    /// <param name="content">Optional content of the message</param>
    /// <param name="recipientJob">Optional job title for new chat creation</param>
    /// <param name="groupMembers">Optional member numbers when creating a group chat</param>
    public NanoChatUiMessageEvent(NanoChatUiMessageType type,
        uint? recipientNumber = null,
        string? content = null,
        string? recipientJob = null,
        List<uint>? groupMembers = null)
    {
        Type = type;
        RecipientNumber = recipientNumber;
        Content = content;
        RecipientJob = recipientJob;
        GroupMembers = groupMembers;
    }
}

[Serializable, NetSerializable]
public enum NanoChatUiMessageType : byte
{
    NewChat,
    SelectChat,
    CloseChat,
    SendMessage,
    DeleteChat,
    ToggleMute,
    ToggleListNumber,
    Typing,
    NewGroupChat,
}

// putting this here because i can
[Serializable, NetSerializable, DataRecord]
public partial struct NanoChatRecipient
{
    /// <summary>
    ///     The recipient's unique NanoChat number.
    /// </summary>
    public uint Number;

    /// <summary>
    ///     The recipient's display name, typically from their ID card.
    /// </summary>
    public string Name;

    /// <summary>
    ///     The recipient's job title, if available.
    /// </summary>
    public string? JobTitle;

    /// <summary>
    ///     Whether this recipient has unread messages.
    /// </summary>
    public bool HasUnread;

    /// <summary>
    ///     Whether this is a group chat rather than a single contact. <see cref="Number" />
    ///     is then a group id, not a real NanoChat card's number.
    /// </summary>
    public bool IsGroup;

    /// <summary>
    ///     Number of members in the group, if <see cref="IsGroup" />. Shown as a subtitle
    ///     before the group has any message history to preview instead.
    /// </summary>
    public int MemberCount;

    /// <summary>
    ///     Creates a new NanoChat recipient.
    /// </summary>
    /// <param name="number">The recipient's NanoChat number</param>
    /// <param name="name">The recipient's display name</param>
    /// <param name="jobTitle">Optional job title for the recipient</param>
    /// <param name="hasUnread">Whether there are unread messages from this recipient</param>
    /// <param name="isGroup">Whether this is a group chat</param>
    /// <param name="memberCount">Number of members, if a group chat</param>
    public NanoChatRecipient(uint number,
        string name,
        string? jobTitle = null,
        bool hasUnread = false,
        bool isGroup = false,
        int memberCount = 0)
    {
        Number = number;
        Name = name;
        JobTitle = jobTitle;
        HasUnread = hasUnread;
        IsGroup = isGroup;
        MemberCount = memberCount;
    }
}

[Serializable, NetSerializable, DataRecord]
public partial struct NanoChatMessage
{
    public const int MaxContentLength = 256;

    /// <summary>
    ///     When the message was sent.
    /// </summary>
    public TimeSpan Timestamp;

    /// <summary>
    ///     The content of the message.
    /// </summary>
    public string Content;

    /// <summary>
    ///     The NanoChat number of the sender.
    /// </summary>
    public uint SenderId;

    /// <summary>
    ///     The sender's display name at the time of sending. Only actually shown in group
    ///     chats (1:1 chats already know the other side's name from the conversation
    ///     itself) - kept on the message rather than looked up live so a group's history
    ///     stays readable even if a member's card later changes hands or is destroyed.
    /// </summary>
    public string? SenderName;

    /// <summary>
    ///     Whether the message failed to deliver to the recipient.
    ///     This can happen if the recipient is out of range or if there's no active telecomms server.
    /// </summary>
    public bool DeliveryFailed;

    /// <summary>
    ///     Creates a new NanoChat message.
    /// </summary>
    /// <param name="timestamp">When the message was sent</param>
    /// <param name="content">The content of the message</param>
    /// <param name="senderId">The sender's NanoChat number</param>
    /// <param name="deliveryFailed">Whether delivery to the recipient failed</param>
    /// <param name="senderName">The sender's display name, for group chats</param>
    public NanoChatMessage(TimeSpan timestamp, string content, uint senderId, bool deliveryFailed = false, string? senderName = null)
    {
        Timestamp = timestamp;
        Content = content;
        SenderId = senderId;
        DeliveryFailed = deliveryFailed;
        SenderName = senderName;
    }
}

/// <summary>
///     NanoChat log data struct
/// </summary>
/// <remarks>Used by the LogProbe</remarks>
[Serializable, NetSerializable, DataRecord]
public readonly partial struct NanoChatData(
    Dictionary<uint, NanoChatRecipient> recipients,
    Dictionary<uint, List<NanoChatMessage>> messages,
    uint? cardNumber,
    NetEntity card)
{
    public Dictionary<uint, NanoChatRecipient> Recipients { get; } = recipients;
    public Dictionary<uint, List<NanoChatMessage>> Messages { get; } = messages;
    public uint? CardNumber { get; } = cardNumber;
    public NetEntity Card { get; } = card;
}

/// <summary>
///     Raised on the NanoChat card whenever a recipient gets added
/// </summary>
[ByRefEvent]
public readonly record struct NanoChatRecipientUpdatedEvent(EntityUid CardUid);

/// <summary>
///     Raised on the NanoChat card whenever it receives or tries sending a messsage
/// </summary>
[ByRefEvent]
public readonly record struct NanoChatMessageReceivedEvent(EntityUid CardUid);
