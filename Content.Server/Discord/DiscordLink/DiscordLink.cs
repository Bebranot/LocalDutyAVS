using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;

namespace Content.Server.Discord.DiscordLink;

/// <summary>
/// Represents the arguments for the <see cref="DiscordLink.OnCommandReceived"/> event.
/// </summary>
public sealed class CommandReceivedEventArgs
{
    /// <summary>
    /// The command that was received. This is the first word in the message, after the bot prefix.
    /// </summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>
    /// The raw arguments to the command. This is everything after the command
    /// </summary>
    public string RawArguments { get; init; } = string.Empty;

    /// <summary>
    /// A list of arguments to the command.
    /// This uses <see cref="CommandParsing.ParseArguments"/> mostly for maintainability.
    /// </summary>
    public List<string> Arguments { get; init; } = [];

    /// <summary>
    /// Information about the message that the command was received from. This includes the message content, author, etc.
    /// Use this to reply to the message, delete it, etc.
    /// </summary>
    public Message Message { get; init; } = default!;
}

/// <summary>
/// Handles the connection to Discord and provides methods to interact with it.
/// </summary>
public sealed class DiscordLink : IPostInjectInit
{
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IConfigurationManager _configuration = default!;

    /// <summary>
    ///    The Discord client. This is null if the bot is not connected.
    /// </summary>
    /// <remarks>
    ///     This should not be used directly outside of DiscordLink. So please do not make it public. Use the methods in this class instead.
    /// </remarks>
    private GatewayClient? _client;
    private ISawmill _sawmill = default!;
    private ISawmill _sawmillLog = default!;

    private ulong _guildId;
    private string _botToken = string.Empty;

    public string BotPrefix = default!;
    /// <summary>
    /// If the bot is currently connected to Discord.
    /// </summary>
    public bool IsConnected => _client != null;

    #region Events

    /// <summary>
    ///     Event that is raised when a command is received from Discord.
    /// </summary>
    public event Action<CommandReceivedEventArgs>? OnCommandReceived;
    /// <summary>
    ///     Event that is raised when a message is received from Discord. This is raised for every message, including commands.
    /// </summary>
    public event Action<Message>? OnMessageReceived;

    // TODO: consider implementing this in a way where we can unregister it in a similar way
    public void RegisterCommandCallback(Action<CommandReceivedEventArgs> callback, string command)
    {
        OnCommandReceived += args =>
        {
            if (args.Command == command)
                callback(args);
        };
    }

    #endregion

    public void Initialize()
    {
        _configuration.OnValueChanged(CCVars.DiscordGuildId, OnGuildIdChanged, true);
        _configuration.OnValueChanged(CCVars.DiscordPrefix, OnPrefixChanged, true);

        if (_configuration.GetCVar(CCVars.DiscordToken) is not { } token || token == string.Empty)
        {
            _sawmill.Info("No Discord token specified, not connecting.");
            return;
        }

        // If the Guild ID is empty OR the prefix is empty, we don't want to connect to Discord.
        if (_guildId == 0 || BotPrefix == string.Empty)
        {
            // This is a warning, not info, because it's a configuration error.
            // It is valid to not have a Discord token set which is why the above check is an info.
            // But if you have a token set, you should also have a guild ID and prefix set.
            _sawmill.Warning("No Discord guild ID or prefix specified, not connecting.");
            return;
        }

        _client = new GatewayClient(new BotToken(token), new GatewayClientConfiguration()
        {
            Intents = GatewayIntents.Guilds
                             | GatewayIntents.GuildUsers
                             | GatewayIntents.GuildMessages
                             | GatewayIntents.MessageContent
                             | GatewayIntents.DirectMessages,
            Logger = new DiscordSawmillLogger(_sawmillLog),
        });
        _client.MessageCreate += OnCommandReceivedInternal;
        _client.MessageCreate += OnMessageReceivedInternal;

        _botToken = token;
        // Since you cannot change the token while the server is running / the DiscordLink is initialized,
        // we can just set the token without updating it every time the cvar changes.

        _client.Ready += _ =>
        {
            _sawmill.Info("Discord client ready.");
            return default;
        };

        Task.Run(async () =>
        {
            try
            {
                await _client.StartAsync();
                _sawmill.Info("Connected to Discord.");
            }
            catch (Exception e)
            {
                _sawmill.Error("Failed to connect to Discord!", e);
            }
        });
    }

    public async Task Shutdown()
    {
        if (_client != null)
        {
            _sawmill.Info("Disconnecting from Discord.");

            // Unsubscribe from the events.
            _client.MessageCreate -= OnCommandReceivedInternal;
            _client.MessageCreate -= OnMessageReceivedInternal;

            try
            {
                // StartAsync() is fired off via Task.Run in Initialize() without being awaited,
                // so _client can be non-null before the gateway websocket actually connects.
                // Closing it in that window throws "Connection not started." — harmless, but if
                // nothing awaits/observes this Task the exception gets rethrown by the finalizer.
                await _client.CloseAsync();
            }
            catch (InvalidOperationException e)
            {
                _sawmill.Debug($"Discord client was never fully started; skipping graceful close: {e.Message}");
            }

            _client.Dispose();
            _client = null;
        }

        _configuration.UnsubValueChanged(CCVars.DiscordGuildId, OnGuildIdChanged);
        _configuration.UnsubValueChanged(CCVars.DiscordPrefix, OnPrefixChanged);
    }

    void IPostInjectInit.PostInject()
    {
        _sawmill = _logManager.GetSawmill("discord.link");
        _sawmillLog = _logManager.GetSawmill("discord.link.log");
    }

    private void OnGuildIdChanged(string guildId)
    {
        _guildId = ulong.TryParse(guildId, out var id) ? id : 0;
    }

    private void OnPrefixChanged(string prefix)
    {
        BotPrefix = prefix;
    }

    private ValueTask OnCommandReceivedInternal(Message message)
    {
        var content = message.Content;
        // If the message doesn't start with the bot prefix, ignore it.
        if (!content.StartsWith(BotPrefix))
            return ValueTask.CompletedTask;

        // Split the message into the command and the arguments.
        var trimmedInput = content[BotPrefix.Length..].Trim();
        var firstSpaceIndex = trimmedInput.IndexOf(' ');

        string command, rawArguments;

        if (firstSpaceIndex == -1)
        {
            command = trimmedInput;
            rawArguments = string.Empty;
        }
        else
        {
            command = trimmedInput[..firstSpaceIndex];
            rawArguments = trimmedInput[(firstSpaceIndex + 1)..].Trim();
        }

        var argumentList = new List<string>();
        CommandParsing.ParseArguments(rawArguments, argumentList);

        // Raise the event!
        OnCommandReceived?.Invoke(new CommandReceivedEventArgs
        {
            Command = command,
            Arguments = argumentList,
            RawArguments = rawArguments,
            Message = message,
        });
        return ValueTask.CompletedTask;
    }

    private ValueTask OnMessageReceivedInternal(Message message)
    {
        OnMessageReceived?.Invoke(message);
        return ValueTask.CompletedTask;
    }

    #region Proxy methods

    /// <summary>
    /// Sends a message to a Discord channel with the specified ID. Without any mentions.
    /// </summary>
    public async Task SendMessageAsync(ulong channelId, string message)
    {
        if (_client == null)
        {
            return;
        }

        var channel = await _client.Rest.GetChannelAsync(channelId) as TextChannel;
        if (channel == null)
        {
            _sawmill.Error("Tried to send a message to Discord but the channel {Channel} was not found.", channel);
            return;
        }

        await channel.SendMessageAsync(new MessageProperties()
        {
            AllowedMentions = AllowedMentionsProperties.None,
            Content = message,
        });
    }

    /// <summary>
    /// Sends an embed to a Discord channel with the specified ID.
    /// </summary>
    /// <returns>The ID of the sent message, or null if it could not be sent (bot not connected, channel not found).</returns>
    public async Task<ulong?> SendEmbedAsync(ulong channelId, EmbedProperties embed)
    {
        if (_client == null)
        {
            return null;
        }

        var channel = await _client.Rest.GetChannelAsync(channelId) as TextChannel;
        if (channel == null)
        {
            _sawmill.Error("Tried to send an embed to Discord but the channel {Channel} was not found.", channelId);
            return null;
        }

        var message = await channel.SendMessageAsync(new MessageProperties()
        {
            AllowedMentions = AllowedMentionsProperties.None,
        }.AddEmbeds(embed));

        return message.Id;
    }

    /// <summary>
    /// Edits an existing message in a Discord channel to replace its embed, instead of sending a new message.
    /// </summary>
    /// <returns>False if the edit failed (bot not connected, channel/message not found) — the caller should
    /// treat this as "message is gone" and send a fresh one via <see cref="SendEmbedAsync"/> instead.</returns>
    public async Task<bool> EditEmbedAsync(ulong channelId, ulong messageId, EmbedProperties embed)
    {
        if (_client == null)
        {
            return false;
        }

        var channel = await _client.Rest.GetChannelAsync(channelId) as TextChannel;
        if (channel == null)
        {
            _sawmill.Error("Tried to edit an embed in a Discord channel {Channel} but the channel was not found.", channelId);
            return false;
        }

        try
        {
            await channel.ModifyMessageAsync(messageId, options => options.WithEmbeds([embed]));
            return true;
        }
        catch (Exception e)
        {
            // Most commonly the message was deleted out from under us (e.g. channel history purged).
            _sawmill.Warning("Failed to edit Discord message {Message} in channel {Channel}: {Error}", messageId, channelId, e.Message);
            return false;
        }
    }

    #endregion
}
