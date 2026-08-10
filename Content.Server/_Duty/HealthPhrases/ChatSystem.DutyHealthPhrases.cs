using Content.Server.ADT.Language;
using Content.Shared.Speech;
using Content.Shared._Duty.HealthPhrases;
using Content.Shared.Chat;
using Content.Shared.IdentityManagement;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Chat.Systems;

public sealed partial class ChatSystem
{
    private const string DefaultChatFont = "NotoSansDisplayItalic";
    private const int DefaultChatFontSize = 11;

    /// <summary>
    /// Whisper реплики боли: шрифт Underdog, цвет #B22222, язык говорящего для понимания слушателями.
    /// </summary>
    public void SendDutyHealthPainWhisper(EntityUid source, string originalMessage)
    {
        var message = FormattedMessage.RemoveMarkupOrThrow(originalMessage);
        if (message.Length == 0)
            return;

        var rawMessage = message;
        var language = _language.GetCurrentLanguage(source);
        message = TransformSpeech(source, message);

        var accentMessage = _language.AccentuateMessage(source, language.ID, message);

        List<string> replacement = new();
        var obfuscateSyllables = false;
        var replaceEntireMessage = false;
        if (language.LanguageType is Generic genericSpeech)
        {
            replacement = genericSpeech.Replacement;
            obfuscateSyllables = genericSpeech.ObfuscateSyllables;
            replaceEntireMessage = genericSpeech.ReplaceEntireMessage;
        }

        var languageMessage = _language.ObfuscateMessage(source, message, replacement, obfuscateSyllables, replaceEntireMessage);
        var obfuscatedMessage = ObfuscateMessageReadability(accentMessage, 0.2f);
        var obfuscatedLanguageMessage = ObfuscateMessageReadability(languageMessage, 0.2f);

        if (string.IsNullOrEmpty(accentMessage))
            return;

        var colorHex = DutyHealthPhrasesVisuals.PainColorHex;
        accentMessage = $"[color={colorHex}]{accentMessage}[/color]";
        languageMessage = $"[color={colorHex}]{languageMessage}[/color]";
        obfuscatedMessage = $"[color={colorHex}]{obfuscatedMessage}[/color]";
        obfuscatedLanguageMessage = $"[color={colorHex}]{obfuscatedLanguageMessage}[/color]";

        var nameIdentity = FormattedMessage.EscapeText(Identity.Name(source, EntityManager));
        var nameEv = new TransformSpeakerNameEvent(source, Name(source));
        RaiseLocalEvent(source, nameEv);
        var name = FormattedMessage.EscapeText(nameEv.VoiceName);

        var font = DutyHealthPhrasesVisuals.FontPrototypeId;
        var fontSize = DutyHealthPhrasesVisuals.WhisperFontSize;

        var wrappedMessage = Loc.GetString("chat-manager-entity-whisper-wrap-message",
            ("entityName", name),
            ("fontType", font),
            ("fontSize", fontSize),
            ("defaultFont", DefaultChatFont),
            ("defaultSize", DefaultChatFontSize),
            ("message", accentMessage));

        var wrappedobfuscatedMessage = Loc.GetString("chat-manager-entity-whisper-wrap-message",
            ("entityName", nameIdentity),
            ("fontType", font),
            ("fontSize", fontSize),
            ("defaultFont", DefaultChatFont),
            ("defaultSize", DefaultChatFontSize),
            ("message", obfuscatedMessage));

        var wrappedUnknownMessage = Loc.GetString("chat-manager-entity-whisper-unknown-wrap-message",
            ("fontType", font),
            ("fontSize", fontSize),
            ("defaultFont", DefaultChatFont),
            ("defaultSize", DefaultChatFontSize),
            ("message", obfuscatedMessage));

        var wrappedLanguageMessage = Loc.GetString("chat-manager-entity-whisper-wrap-message",
            ("fontType", font),
            ("fontSize", fontSize),
            ("defaultFont", DefaultChatFont),
            ("defaultSize", DefaultChatFontSize),
            ("entityName", name),
            ("message", languageMessage));

        var wrappedobfuscatedLanguageMessage = Loc.GetString("chat-manager-entity-whisper-wrap-message",
            ("fontType", font),
            ("fontSize", fontSize),
            ("defaultFont", DefaultChatFont),
            ("defaultSize", DefaultChatFontSize),
            ("entityName", nameIdentity),
            ("message", obfuscatedLanguageMessage));

        var wrappedUnknownLanguageMessage = Loc.GetString("chat-manager-entity-whisper-unknown-wrap-message",
            ("fontType", font),
            ("fontSize", fontSize),
            ("defaultFont", DefaultChatFont),
            ("defaultSize", DefaultChatFontSize),
            ("message", obfuscatedLanguageMessage));

        SendWhisper(
            source,
            language.ID,
            ChatTransmitRange.Normal,
            message,
            obfuscatedMessage,
            wrappedMessage,
            wrappedobfuscatedMessage,
            wrappedUnknownMessage,
            wrappedLanguageMessage,
            wrappedobfuscatedLanguageMessage,
            wrappedUnknownLanguageMessage);

        if (language.LanguageType.RaiseEvent)
        {
            var resultMessage = FormattedMessage.EscapeText(accentMessage);
            var resultObfMessage = FormattedMessage.EscapeText(obfuscatedMessage);
            RaiseLocalEvent(source, new EntitySpokeEvent(source, resultMessage, rawMessage, language, null, resultObfMessage), true);
        }
    }

    /// <summary>
    /// Крик боли: настоящая say-реплика (слышно всем в обычной дистанции, идёт в чат-лог) —
    /// шрифт Underdog, цвет #FF0000, речевой пузырь над головой дрожит (DutyScreamShake).
    /// В отличие от обычной речи не проходит через акценты/обфускацию языка — крик боли понятен
    /// независимо от того, на каком языке говорит персонаж.
    /// </summary>
    public void SendDutyHealthScream(EntityUid source, string originalMessage)
    {
        var message = FormattedMessage.RemoveMarkupOrThrow(originalMessage);
        if (message.Length == 0)
            return;

        var colorHex = DutyHealthPhrasesVisuals.ScreamColorHex;
        var coloredMessage = $"[color={colorHex}]{message}[/color]";

        var nameEv = new TransformSpeakerNameEvent(source, Name(source));
        RaiseLocalEvent(source, nameEv);
        var name = FormattedMessage.EscapeText(nameEv.VoiceName);

        var wrappedMessage = Loc.GetString("chat-manager-entity-say-wrap-message",
            ("entityName", name),
            ("verb", Loc.GetString("duty-health-phrases-scream-verb")),
            ("fontType", DutyHealthPhrasesVisuals.FontPrototypeId),
            ("fontSize", DutyHealthPhrasesVisuals.ScreamFontSize),
            ("defaultFont", DefaultChatFont),
            ("defaultSize", DefaultChatFontSize),
            ("message", coloredMessage));

        SendInVoiceRange(
            ChatChannel.Local,
            message,
            wrappedMessage,
            wrappedMessage,
            source,
            ChatTransmitRange.Normal,
            ignoreLanguage: true,
            dutyScreamShake: true);
    }
}
