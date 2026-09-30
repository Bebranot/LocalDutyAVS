using System.Linq;
using Content.Server.ADT.NanoChat;
using Content.Server.Administration.Logs;
using Content.Server.CartridgeLoader;
using Content.Server.Chat.Managers;
using Content.Server.Power.Components;
using Content.Server.Radio;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.ADT.CartridgeLoader.Cartridges;
using Content.Shared.ADT.NanoChat;
using Content.Shared.PDA;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Server.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.ADT.CartridgeLoader.Cartridges;

public sealed class NanoChatCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridge = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedNanoChatSystem _nanoChat = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly AudioSystem _audio = default!;

    // Messages in notifications get cut off after this point
    // no point in storing it on the comp
    private const int NotificationMaxLength = 64;

    // How long a "typing..." indicator stays up after the last keystroke ping before we
    // treat it as stale and clear it - the client only pings while actively typing, it
    // never explicitly says "I stopped".
    private static readonly TimeSpan TypingIndicatorDuration = TimeSpan.FromSeconds(4);

    // Card entity -> (who's typing to it, when that stops being valid). Purely transient
    // server-side state, not worth persisting or networking on the card itself.
    private readonly Dictionary<EntityUid, (uint From, TimeSpan Expires)> _typingIndicators = new();

    // Group chats aren't real ID cards, so they don't fit the "number identifies one
    // NanoChatCardComponent" model the rest of this file assumes. Instead a group gets an
    // id from a reserved range that individual contact numbers (1000-9999, see the
    // NanoChat nameIdentifierGroup prototype) never reach, and its membership list lives
    // here rather than on any single player's card.
    private const uint GroupIdBase = 100_000;
    private const int MaxGroupMembers = 20;
    private const int GroupNameMaxLength = 32;
    private uint _nextGroupId = GroupIdBase;
    private readonly Dictionary<uint, List<uint>> _groupMembers = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NanoChatCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<NanoChatCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Update card references for any cartridges that need it
        var query = EntityQueryEnumerator<NanoChatCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out var nanoChat, out var cartridge))
        {
            if (cartridge.LoaderUid == null)
                continue;

            // Check if we need to update our card reference
            if (!TryComp<PdaComponent>(cartridge.LoaderUid, out var pda))
                continue;

            var newCard = pda.ContainedId;
            var currentCard = nanoChat.Card;

            // If the cards match, nothing to do
            if (newCard == currentCard)
                continue;

            // Update card reference
            nanoChat.Card = newCard;

            // Update UI state since card reference changed
            UpdateUI((uid, nanoChat), cartridge.LoaderUid.Value);
        }

        ExpireTypingIndicators();
    }

    /// <summary>
    ///     Clears out "typing..." indicators once they've timed out and pushes a UI update
    ///     to the affected card so it actually disappears client-side instead of sticking
    ///     around forever. Bounded by how many people are typing at once, not by station size.
    /// </summary>
    private void ExpireTypingIndicators()
    {
        if (_typingIndicators.Count == 0)
            return;

        List<EntityUid>? expired = null;
        foreach (var (cardUid, typing) in _typingIndicators)
        {
            if (typing.Expires > _timing.CurTime)
                continue;

            expired ??= new List<EntityUid>();
            expired.Add(cardUid);
        }

        if (expired == null)
            return;

        foreach (var cardUid in expired)
        {
            _typingIndicators.Remove(cardUid);
            UpdateUIForCard(cardUid, refreshContacts: false);
        }
    }

    /// <summary>
    ///     Handles incoming UI messages from the NanoChat cartridge.
    /// </summary>
    private void OnMessage(Entity<NanoChatCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not NanoChatUiMessageEvent msg)
            return;

        if (!GetCardEntity(GetEntity(args.LoaderUid), out var card))
            return;

        switch (msg.Type)
        {
            // These already push their own UI update (and some, like NewChat, need the
            // full station contact rescan) - don't redundantly recompute and resend
            // the whole state a second time right after.
            case NanoChatUiMessageType.NewChat:
                NewChat(card, msg.RecipientNumber, msg.Content, msg.RecipientJob, msg.Actor);
                return;
            case NanoChatUiMessageType.NewGroupChat:
                NewGroupChat(card, msg.Content, msg.GroupMembers, msg.Actor);
                return;
            case NanoChatUiMessageType.ToggleMute:
                ToggleMute(card);
                return;
            case NanoChatUiMessageType.DeleteChat:
                DeleteChat(card, msg.RecipientNumber, msg.Actor);
                return;
            case NanoChatUiMessageType.ToggleListNumber:
                ToggleListNumber(card);
                return;
            case NanoChatUiMessageType.Typing:
                // Only pushes to the recipient, never back to the sender - no trailing
                // update needed here.
                Typing(card, msg.RecipientNumber);
                return;

            // These only touch chat content/selection, never the station contact
            // directory, so the trailing update below can skip rebuilding it.
            case NanoChatUiMessageType.SelectChat:
                SelectChat(card, msg.RecipientNumber);
                break;
            case NanoChatUiMessageType.CloseChat:
                CloseChat(card);
                break;
            case NanoChatUiMessageType.SendMessage:
                SendMessage(ent, card, msg.RecipientNumber, msg.Content, ent.Comp.RadioChannel);
                break;
        }

        UpdateUI(ent, GetEntity(args.LoaderUid), refreshContacts: false);
    }

    /// <summary>
    ///     Gets the ID card entity associated with a PDA.
    /// </summary>
    /// <param name="loaderUid">The PDA entity ID</param>
    /// <param name="card">Output parameter containing the found card entity and component</param>
    /// <returns>True if a valid NanoChat card was found</returns>
    private bool GetCardEntity(
        EntityUid loaderUid,
        out Entity<NanoChatCardComponent> card)
    {
        card = default;

        // Get the PDA and check if it has an ID card
        if (!TryComp<PdaComponent>(loaderUid, out var pda) ||
            pda.ContainedId == null ||
            !TryComp<NanoChatCardComponent>(pda.ContainedId, out var idCard))
            return false;

        card = (pda.ContainedId.Value, idCard);
        return true;
    }

    public void NewChat(Entity<NanoChatCardComponent> card, uint? recipientNumber, string? content, string? recipientJob, EntityUid actor)
    {
        if (recipientNumber == null || content == null || recipientNumber == card.Comp.Number)
            return;

        var name = content;
        if (!string.IsNullOrWhiteSpace(name))
        {
            name = name.Trim();
        }

        var jobTitle = recipientJob;
        if (!string.IsNullOrWhiteSpace(jobTitle))
        {
            jobTitle = jobTitle.Trim();
        }

        // Add new recipient
        var recipient = new NanoChatRecipient(recipientNumber.Value,
            name,
            jobTitle);

        // Initialize or update recipient
        _nanoChat.SetRecipient((card, card.Comp), recipientNumber.Value, recipient);

        _adminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(actor):user} created new NanoChat conversation with #{recipientNumber:D4} ({name})");

        var recipientEv = new NanoChatRecipientUpdatedEvent(card);
        RaiseLocalEvent(ref recipientEv);
        UpdateUIForCard(card);
    }

    public void SelectChat(Entity<NanoChatCardComponent> card, uint? recipientNumber)
    {
        if (recipientNumber == null)
            return;

        _nanoChat.SetCurrentChat((card, card.Comp), recipientNumber);

        // Clear unread flag when selecting chat
        if (_nanoChat.GetRecipient((card, card.Comp), recipientNumber.Value) is { } recipient)
        {
            _nanoChat.SetRecipient((card, card.Comp),
                recipientNumber.Value,
                recipient with { HasUnread = false });
        }
    }

    public void CloseChat(Entity<NanoChatCardComponent> card)
    {
        _nanoChat.SetCurrentChat((card, card.Comp), null);
    }

    public void DeleteChat(Entity<NanoChatCardComponent> card, uint? recipientNumber, EntityUid actor)
    {
        if (recipientNumber == null || card.Comp.Number == null)
            return;

        // Delete chat but keep the messages
        var deleted = _nanoChat.TryDeleteChat((card, card.Comp), recipientNumber.Value, true);

        if (!deleted)
            return;

        _adminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(actor):user} deleted NanoChat conversation with #{recipientNumber:D4}");

        UpdateUIForCard(card);
    }

    public void ToggleMute(Entity<NanoChatCardComponent> card)
    {
        _nanoChat.SetNotificationsMuted((card, card.Comp), !_nanoChat.GetNotificationsMuted((card, card.Comp)));
        UpdateUIForCard(card);
    }

    public void ToggleListNumber(Entity<NanoChatCardComponent> card)
    {
        _nanoChat.SetListNumber((card, card.Comp), !_nanoChat.GetListNumber((card, card.Comp)));

        // Only this card's own UI needs to reflect the toggle immediately - it used to
        // rebuild and push the full state (including entire message history) to every
        // single PDA with NanoChat open on the server, which caused a visible stall.
        // Other players' contact directories pick up the change next time their own
        // UI refreshes.
        UpdateUIForCard(card);
    }

    /// <summary>
    ///     Handles a "typing..." ping from the client, relaying it to the recipient's card
    ///     if one is currently reachable by number.
    /// </summary>
    public void Typing(Entity<NanoChatCardComponent> card, uint? recipientNumber)
    {
        if (recipientNumber == null || card.Comp.Number == null)
            return;

        // Typing pings to a group aren't implemented - showing "N is typing" for a group
        // would need per-member state instead of the current single typer slot.
        if (_groupMembers.ContainsKey(recipientNumber.Value))
            return;

        if (!TryFindCardByNumber(recipientNumber.Value, out var recipientUid))
            return;

        _typingIndicators[recipientUid] = ((uint)card.Comp.Number, _timing.CurTime + TypingIndicatorDuration);
        UpdateUIForCard(recipientUid, refreshContacts: false);
    }

    /// <summary>
    ///     Finds the NanoChat card entity with the given number, if any.
    /// </summary>
    private bool TryFindCardByNumber(uint number, out EntityUid card)
    {
        var query = EntityQueryEnumerator<NanoChatCardComponent>();
        while (query.MoveNext(out var uid, out var nanoChatCard))
        {
            if (nanoChatCard.Number != number)
                continue;

            card = uid;
            return true;
        }

        card = default;
        return false;
    }

    /// <summary>
    ///     Handles creation of a new group chat, adding it to every valid member's card.
    /// </summary>
    public void NewGroupChat(Entity<NanoChatCardComponent> card, string? content, List<uint>? groupMembers, EntityUid actor)
    {
        if (content == null || groupMembers == null || card.Comp.Number == null)
            return;

        var name = content.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (name.Length > GroupNameMaxLength)
            name = name[..GroupNameMaxLength];

        // Only invite numbers that actually resolve to a real card, and always include
        // the creator. Silently drop unknown/self numbers rather than failing the whole
        // group - typos in one number shouldn't block the rest.
        var members = new List<uint> { card.Comp.Number.Value };
        foreach (var number in groupMembers.Distinct())
        {
            if (number == card.Comp.Number || members.Count >= MaxGroupMembers)
                continue;

            if (TryFindCardByNumber(number, out _))
                members.Add(number);
        }

        // Need at least one other real member for this to be a group at all.
        if (members.Count < 2)
            return;

        var groupId = _nextGroupId++;
        _groupMembers[groupId] = members;

        var groupRecipient = new NanoChatRecipient(groupId, name, null, isGroup: true, memberCount: members.Count);

        foreach (var memberNumber in members)
        {
            if (!TryFindCardByNumber(memberNumber, out var memberUid) ||
                !TryComp<NanoChatCardComponent>(memberUid, out var memberCard))
                continue;

            _nanoChat.SetRecipient((memberUid, memberCard), groupId, groupRecipient);
            _nanoChat.EnsureRecipientExists((memberUid, memberCard), groupId);
            UpdateUIForCard(memberUid);
        }

        _adminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(actor):user} created NanoChat group '{name}' (#{groupId}) with {members.Count} members");
    }

    /// <summary>
    ///     Sends a NanoChat message from a card, attempting delivery through the radio channel.
    /// </summary>
    /// <param name="sender">The sending entity (cartridge or station AI)</param>
    public void SendMessage(EntityUid sender,
        Entity<NanoChatCardComponent> card,
        uint? recipientNumber,
        string? content,
        ProtoId<RadioChannelPrototype> channelId)
    {
        if (recipientNumber == null || content == null || card.Comp.Number == null)
            return;

        var conversationNumber = recipientNumber.Value;

        if (!EnsureRecipientExists(card, conversationNumber))
            return;

        if (!string.IsNullOrWhiteSpace(content))
        {
            content = FormattedMessage.EscapeText(content.Trim());
            if (content.Length > NanoChatMessage.MaxContentLength)
                content = content[..NanoChatMessage.MaxContentLength];
        }

        var senderName = TryComp<IdCardComponent>(card, out var senderIdCard) ? senderIdCard.FullName : null;

        // Create and store message for sender
        var message = new NanoChatMessage(
            _timing.CurTime,
            content,
            (uint)card.Comp.Number,
            senderName: senderName
        );

        // Attempt delivery - to every member for a group, to the one contact otherwise
        bool deliveryFailed;
        List<Entity<NanoChatCardComponent>> recipients;
        if (_groupMembers.TryGetValue(conversationNumber, out var members))
        {
            recipients = new List<Entity<NanoChatCardComponent>>();
            foreach (var memberNumber in members)
            {
                if (memberNumber == card.Comp.Number)
                    continue;

                var (memberFailed, memberRecipients) = AttemptMessageDelivery(sender, memberNumber, channelId);
                if (!memberFailed)
                    recipients.AddRange(memberRecipients);
            }

            deliveryFailed = recipients.Count == 0;
        }
        else
        {
            (deliveryFailed, recipients) = AttemptMessageDelivery(sender, conversationNumber, channelId);
        }

        // Update delivery status
        message = message with { DeliveryFailed = deliveryFailed };

        // Store message in sender's outbox under the conversation's key
        _nanoChat.AddMessage((card, card.Comp), conversationNumber, message);

        // Log message attempt
        var recipientsText = recipients.Count > 0
            ? string.Join(", ", recipients.Select(r => ToPrettyString(r)))
            : $"#{conversationNumber:D4}";

        _adminLogger.Add(LogType.Chat,
            LogImpact.Low,
            $"{ToPrettyString(card):user} sent NanoChat message to {recipientsText}: {content}{(deliveryFailed ? " [DELIVERY FAILED]" : "")}");

        var msgEv = new NanoChatMessageReceivedEvent(card);
        RaiseLocalEvent(ref msgEv);

        if (deliveryFailed)
            return;

        var groupId = members != null ? conversationNumber : (uint?)null;
        foreach (var recipient in recipients)
        {
            DeliverMessageToRecipient(card, recipient, message, groupId);
        }
    }

    /// <summary>
    ///     Ensures a recipient exists in the sender's contacts.
    /// </summary>
    /// <param name="card">The card to check contacts for</param>
    /// <param name="recipientNumber">The recipient's number to check</param>
    /// <returns>True if the recipient exists or was created successfully</returns>
    private bool EnsureRecipientExists(Entity<NanoChatCardComponent> card, uint recipientNumber)
    {
        return _nanoChat.EnsureRecipientExists((card, card.Comp), recipientNumber, GetCardInfo(recipientNumber));
    }

    /// <summary>
    ///     Attempts to deliver a message to recipients, including cards held by a station AI.
    /// </summary>
    /// <param name="sender">The sending entity (cartridge or station AI)</param>
    /// <param name="recipientNumber">The recipient's number</param>
    /// <param name="channelId">The radio channel used for delivery</param>
    /// <returns>Tuple containing delivery status and recipients if found.</returns>
    private (bool failed, List<Entity<NanoChatCardComponent>> recipient) AttemptMessageDelivery(
        EntityUid sender,
        uint recipientNumber,
        ProtoId<RadioChannelPrototype> channelId)
    {
        // First verify we can send from this device
        var channel = _prototype.Index(channelId);
        var sendAttemptEvent = new RadioSendAttemptEvent(channel, sender);
        RaiseLocalEvent(ref sendAttemptEvent);
        if (sendAttemptEvent.Cancelled)
            return (true, new List<Entity<NanoChatCardComponent>>());

        var foundRecipients = new List<Entity<NanoChatCardComponent>>();

        // Find all cards with matching number
        var cardQuery = EntityQueryEnumerator<NanoChatCardComponent>();
        while (cardQuery.MoveNext(out var cardUid, out var card))
        {
            if (card.Number != recipientNumber)
                continue;

            foundRecipients.Add((cardUid, card));
        }

        if (foundRecipients.Count == 0)
            return (true, foundRecipients);

        var senderStation = _station.GetOwningStation(sender);

        // Now check if any of these cards can receive
        var deliverableRecipients = new List<Entity<NanoChatCardComponent>>();
        foreach (var recipient in foundRecipients)
        {
            // Cards held by a station AI (e.g. in a core) can receive without a cartridge
            if (HasComp<StationAiNanoChatComponent>(recipient.Owner))
            {
                if (CanReceive(sender, senderStation, recipient.Owner, channel))
                    deliverableRecipients.Add(recipient);

                continue;
            }

            // Find any cartridges that have this card
            var cartridgeQuery = EntityQueryEnumerator<NanoChatCartridgeComponent, ActiveRadioComponent>();
            while (cartridgeQuery.MoveNext(out var receiverUid, out var receiverCart, out _))
            {
                if (receiverCart.Card != recipient.Owner)
                    continue;

                if (!CanReceive(sender, senderStation, receiverUid, channel))
                    continue;

                deliverableRecipients.Add(recipient);
                break; // Only need one valid cartridge per card
            }
        }

        return (deliverableRecipients.Count == 0, deliverableRecipients);
    }

    private bool CanReceive(EntityUid sender, EntityUid? senderStation, EntityUid receiver, RadioChannelPrototype channel)
    {
        var recipientStation = _station.GetOwningStation(receiver);

        // Both entities must be on a station
        if (recipientStation == null || senderStation == null)
            return false;

        // Must be on same map/station unless long range allowed
        if (!channel.LongRange && recipientStation != senderStation)
            return false;

        // Needs telecomms
        if (!HasActiveServer(senderStation.Value) || !HasActiveServer(recipientStation.Value))
            return false;

        var receiveAttemptEv = new RadioReceiveAttemptEvent(channel, sender, receiver);
        RaiseLocalEvent(ref receiveAttemptEv);
        return !receiveAttemptEv.Cancelled;
    }

    /// <summary>
    ///     Checks if there are any active telecomms servers on the given station
    /// </summary>
    private bool HasActiveServer(EntityUid station)
    {
        // I have no idea why this isn't public in the RadioSystem
        var query =
            EntityQueryEnumerator<TelecomServerComponent, EncryptionKeyHolderComponent, ApcPowerReceiverComponent>();

        while (query.MoveNext(out var uid, out _, out _, out var power))
        {
            if (_station.GetOwningStation(uid) == station && power.Powered)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Delivers a message to the recipient and handles associated notifications.
    /// </summary>
    /// <param name="sender">The sender's card entity</param>
    /// <param name="recipient">The recipient's card entity</param>
    /// <param name="message">The <see cref="NanoChatMessage" /> to deliver</param>
    /// <param name="groupId">
    ///     If this is a group message, the group's id - it's stored under that key on the
    ///     recipient's card instead of the sender's number, and never adds the sender as a
    ///     new 1:1 contact (the recipient already knows about the group from its creation).
    /// </param>
    private void DeliverMessageToRecipient(Entity<NanoChatCardComponent> sender,
        Entity<NanoChatCardComponent> recipient,
        NanoChatMessage message,
        uint? groupId = null)
    {
        var senderNumber = sender.Comp.Number;
        if (senderNumber == null)
            return;

        uint conversationKey;
        if (groupId is { } group)
        {
            // The recipient must already know about the group - never resurrect it as a
            // stray 1:1 conversation with whoever happened to send this message.
            if (_nanoChat.GetRecipient((recipient, recipient.Comp), group) == null)
                return;

            conversationKey = group;
        }
        else
        {
            if (!EnsureRecipientExists(recipient, senderNumber.Value))
                return;

            conversationKey = senderNumber.Value;
        }

        _nanoChat.AddMessage((recipient, recipient.Comp), conversationKey, message with { DeliveryFailed = false });

        // The message itself is a stronger signal than the typing ping - don't leave
        // "typing..." showing for a few more seconds after it already arrived.
        if (_typingIndicators.TryGetValue(recipient.Owner, out var typing) && typing.From == senderNumber.Value)
            _typingIndicators.Remove(recipient.Owner);

        HandleUnreadNotification(recipient, message, conversationKey);

        var msgEv = new NanoChatMessageReceivedEvent(recipient);
        RaiseLocalEvent(ref msgEv);
        UpdateUIForCard(recipient, refreshContacts: false);
    }

    /// <summary>
    ///     Handles unread message notifications and updates unread status.
    /// </summary>
    /// <param name="recipient">The card receiving the message</param>
    /// <param name="message">The message received - <see cref="NanoChatMessage.SenderId"/> names who actually wrote it</param>
    /// <param name="conversationNumber">
    ///     The key this message is filed under in the recipient's own Recipients/Messages -
    ///     the sender's number for a 1:1 chat, or the group's id for a group chat.
    /// </param>
    private void HandleUnreadNotification(Entity<NanoChatCardComponent> recipient,
        NanoChatMessage message,
        uint conversationNumber)
    {
        // Get sender name from contacts or fall back to number
        var recipients = _nanoChat.GetRecipients((recipient, recipient.Comp));
        var senderName = recipients.TryGetValue(message.SenderId, out var senderRecipient)
            ? senderRecipient.Name
            : $"#{message.SenderId:D4}";
        var hasSelectedCurrentChat = _nanoChat.GetCurrentChat((recipient, recipient.Comp)) == conversationNumber;

        // Update unread status on the conversation itself (the group, or the 1:1 contact)
        if (!hasSelectedCurrentChat && recipients.TryGetValue(conversationNumber, out var conversationRecipient))
            _nanoChat.SetRecipient((recipient, recipient.Comp),
                conversationNumber,
                conversationRecipient with { HasUnread = true });

        if (recipient.Comp.NotificationsMuted)
            return;

        var header = Loc.GetString("nano-chat-new-message-title", ("sender", senderName));
        var body = Loc.GetString("nano-chat-new-message-body", ("message", TruncateMessage(message.Content)));

        if (TryComp<StationAiNanoChatComponent>(recipient.Owner, out var stationAi))
        {
            if (hasSelectedCurrentChat && _ui.IsUiOpen(recipient.Owner, StationAiNanoChatUiKey.Key))
                return;

            NotifyStationAi((recipient, stationAi), header, body);
            return;
        }

        if (recipient.Comp.PdaUid is not {} pdaUid ||
            !TryComp<CartridgeLoaderComponent>(pdaUid, out var loader) ||
            // Don't notify if the recipient has the NanoChat program open with this chat selected.
            (hasSelectedCurrentChat &&
                _ui.IsUiOpen(pdaUid, PdaUiKey.Key) &&
                HasComp<NanoChatCartridgeComponent>(loader.ActiveProgram)))
            return;

        _cartridge.SendNotification(pdaUid, header, body, loader);
    }

    private void NotifyStationAi(Entity<StationAiNanoChatComponent> ent, string header, string body)
    {
        if (!TryComp<ActorComponent>(ent.Owner, out var actor))
            return;

        var message = FormattedMessage.EscapeText(body);
        var wrappedMessage = Loc.GetString("pda-notification-message",
            ("header", header),
            ("message", message));

        _chatManager.ChatMessageToOne(ChatChannel.Notifications,
            message,
            wrappedMessage,
            EntityUid.Invalid,
            false,
            actor.PlayerSession.Channel);

        _audio.PlayGlobal(ent.Comp.NotificationSound, actor.PlayerSession);
    }

    /// <summary>
    ///     Updates the UI for any PDAs containing the specified card.
    /// </summary>
    private void UpdateUIForCard(EntityUid cardUid, bool refreshContacts = true)
    {
        if (TryComp<StationAiNanoChatComponent>(cardUid, out var stationAi))
            UpdateStationAiUi((cardUid, stationAi));

        // Find any PDA containing this card and update its UI
        var query = EntityQueryEnumerator<NanoChatCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out var comp, out var cartridge))
        {
            if (comp.Card != cardUid || cartridge.LoaderUid == null)
                continue;

            UpdateUI((uid, comp), cartridge.LoaderUid.Value, refreshContacts);
        }

        var aiQuery = EntityQueryEnumerator<StationAiNanoChatComponent>();
        while (aiQuery.MoveNext(out var uid, out var comp))
        {
            UpdateStationAiUi((uid, comp));
        }
    }

    /// <summary>
    ///     Gets the <see cref="NanoChatRecipient" /> for a given NanoChat number.
    /// </summary>
    private NanoChatRecipient? GetCardInfo(uint number)
    {
        if (!TryFindCardByNumber(number, out var uid))
            return null;

        // Try to get job title from ID card if possible
        string? jobTitle = null;
        var name = "Unknown";
        if (TryComp<IdCardComponent>(uid, out var idCard))
        {
            jobTitle = idCard.LocalizedJobTitle;
            name = idCard.FullName ?? name;
        }
        else if (HasComp<StationAiNanoChatComponent>(uid))
        {
            name = Name(uid);
            jobTitle = Loc.GetString("job-name-station-ai");
        }

        return new NanoChatRecipient(number, name, jobTitle);
    }

    /// <summary>
    ///     Truncates a message to the notification maximum length.
    /// </summary>
    private static string TruncateMessage(string message)
    {
        return message.Length <= NotificationMaxLength
            ? message
            : message[..(NotificationMaxLength - 4)] + " [...]";
    }

    private void OnUiReady(Entity<NanoChatCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        _cartridge.RegisterBackgroundProgram(args.Loader, ent);
        UpdateUI(ent, args.Loader);
    }

    private List<NanoChatRecipient>? GetContacts(EntityUid? station)
    {
        if (station == null)
            return null;

        var contacts = new List<NanoChatRecipient>();

        var query = AllEntityQuery<NanoChatCardComponent, IdCardComponent>();
        while (query.MoveNext(out var entityId, out var nanoChatCard, out var idCardComponent))
        {
            if (nanoChatCard.ListNumber && nanoChatCard.Number is uint nanoChatNumber && idCardComponent.FullName is string fullName && _station.GetOwningStation(entityId) == station)
            {
                contacts.Add(new NanoChatRecipient(nanoChatNumber, fullName, idCardComponent.LocalizedJobTitle));
            }
        }

        // Cards held by a station AI are visible in the directory as well
        var aiJob = Loc.GetString("job-name-station-ai");
        var aiQuery = AllEntityQuery<NanoChatCardComponent, StationAiNanoChatComponent>();
        while (aiQuery.MoveNext(out var entityId, out var nanoChatCard, out _))
        {
            if (nanoChatCard.ListNumber && nanoChatCard.Number is uint nanoChatNumber && _station.GetOwningStation(entityId) == station)
            {
                contacts.Add(new NanoChatRecipient(nanoChatNumber, Name(entityId), aiJob));
            }
        }

        contacts.Sort((contactA, contactB) => string.CompareOrdinal(contactA.Name, contactB.Name));
        return contacts;
    }

    public void UpdateStationAiUi(Entity<StationAiNanoChatComponent> ent)
    {
        if (!TryComp<NanoChatCardComponent>(ent.Owner, out var card))
            return;

        var state = new NanoChatUiState(card.Recipients,
            card.Messages,
            GetContacts(_station.GetOwningStation(ent.Owner)),
            card.CurrentChat,
            card.Number ?? 0,
            card.MaxRecipients,
            card.NotificationsMuted,
            card.ListNumber);

        _ui.SetUiState(ent.Owner, StationAiNanoChatUiKey.Key, state);
    }

    /// <summary>
    ///     Pushes the cartridge's UI state to the client.
    /// </summary>
    /// <param name="refreshContacts">
    ///     Whether to rescan every NanoChat/ID card on the station to rebuild the contact
    ///     directory. This scan is the expensive part of an update, so callers that only
    ///     changed chat content (selecting a chat, sending a message) should pass false
    ///     and reuse the last known directory - it can't have changed from those actions.
    /// </param>
    private void UpdateUI(Entity<NanoChatCartridgeComponent> ent, EntityUid loader, bool refreshContacts = true)
    {
        List<NanoChatRecipient>? contacts;
        if (_station.GetOwningStation(loader) is { } station)
        {
            var stationChanged = ent.Comp.Station != station;
            ent.Comp.Station = station;

            if (refreshContacts || stationChanged || ent.Comp.CachedContacts == null)
                ent.Comp.CachedContacts = GetContacts(station);

            contacts = ent.Comp.CachedContacts;
        }
        else
        {
            contacts = null;
            ent.Comp.CachedContacts = null;
        }

        var recipients = new Dictionary<uint, NanoChatRecipient>();
        var messages = new Dictionary<uint, List<NanoChatMessage>>();
        uint? currentChat = null;
        uint ownNumber = 0;
        var maxRecipients = 50;
        var notificationsMuted = false;
        var listNumber = false;
        uint? typingFrom = null;

        if (ent.Comp.Card != null && TryComp<NanoChatCardComponent>(ent.Comp.Card, out var card))
        {
            recipients = card.Recipients;
            messages = card.Messages;
            currentChat = card.CurrentChat;
            ownNumber = card.Number ?? 0;
            maxRecipients = card.MaxRecipients;
            notificationsMuted = card.NotificationsMuted;
            listNumber = card.ListNumber;

            if (_typingIndicators.TryGetValue(ent.Comp.Card.Value, out var typing) && typing.Expires > _timing.CurTime)
                typingFrom = typing.From;
        }

        var state = new NanoChatUiState(recipients,
            messages,
            contacts,
            currentChat,
            ownNumber,
            maxRecipients,
            notificationsMuted,
            listNumber,
            typingFrom);
        _cartridge.UpdateCartridgeUiState(loader, state);
    }
}
