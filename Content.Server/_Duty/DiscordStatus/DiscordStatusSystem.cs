using System.Text;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Server.Station.Components;
using Content.Shared.CCVar;
using NetCord.Rest;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using DiscordColor = NetCord.Color;
using DiscordLinkService = Content.Server.Discord.DiscordLink.DiscordLink;
using GameRunLevel = Content.Server.GameTicking.GameRunLevel;

namespace Content.Server._Duty.DiscordStatus;

/// <summary>
/// _Duty: раз в <see cref="DutyCCVars.DiscordStatusUpdateInterval"/> секунд редактирует одно и то же
/// сообщение в Discord-канале <see cref="DutyCCVars.DiscordStatusChannelId"/> со статусом сервера
/// (карта, пресет, число игроков онлайн, лобби/раунд), вместо того чтобы спамить новыми сообщениями.
/// Дополнительно форсирует немедленное обновление при смене <see cref="GameRunLevel"/> (лобби → раунд
/// и обратно, в том числе после рестарта сервера), не дожидаясь конца интервала.
/// ID отправленного сообщения хранится только в памяти системы: рестарт сервера или смена канала
/// на лету теряет его, и следующее обновление просто отправляет новое сообщение.
/// </summary>
public sealed class DiscordStatusSystem : EntitySystem
{
    [Dependency] private readonly DiscordLinkService _discord = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IGameMapManager _gameMapManager = default!;
    [Dependency] private readonly GameTicker _ticker = default!;

    private const float MinUpdateIntervalSeconds = 5f;

    private TimeSpan _nextUpdate;
    private ulong? _messageId;
    private ulong _messageChannelId;
    private bool _updateInFlight;
    private bool _loggedFirstUpdate;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);

        Log.Info($"DiscordStatusSystem initialized. enabled={_cfg.GetCVar(DutyCCVars.DiscordStatusEnabled)} " +
                 $"channel='{_cfg.GetCVar(DutyCCVars.DiscordStatusChannelId)}' " +
                 $"interval={_cfg.GetCVar(DutyCCVars.DiscordStatusUpdateInterval)}");
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent args)
    {
        // Лобби → раунд и раунд → лобби (в т.ч. после рестарта) не должны ждать до конца интервала.
        _nextUpdate = TimeSpan.Zero;
    }

    private int _diagTickCount;

    public override void Update(float frameTime)
    {
        // ВРЕМЕННО, без условий — проверяем, вызывается ли Update() вообще. Убрать после диагностики.
        _diagTickCount++;
        if (_diagTickCount <= 5)
            Log.Info($"DiscordStatusSystem: Update() called, tick #{_diagTickCount}, frameTime={frameTime}");

        try
        {
            UpdateCore();
        }
        catch (Exception e)
        {
            // Диагностика: EXCEPTION_TOLERANCE в этом форке может молча глотать исключения из
            // Update() систем на более высоком уровне, не давая нам их увидеть вообще.
            Log.Error($"DiscordStatusSystem.Update threw: {e}");
        }
    }

    private void UpdateCore()
    {
        if (!_loggedFirstUpdate)
        {
            _loggedFirstUpdate = true;
            Log.Info($"DiscordStatusSystem: first Update() tick reached. CurTime={_timing.CurTime} nextUpdate={_nextUpdate} " +
                     $"enabled={_cfg.GetCVar(DutyCCVars.DiscordStatusEnabled)} updateInFlight={_updateInFlight}");
        }

        if (_updateInFlight)
            return;

        if (!_cfg.GetCVar(DutyCCVars.DiscordStatusEnabled))
            return;

        if (_timing.CurTime < _nextUpdate)
            return;

        var interval = MathF.Max(MinUpdateIntervalSeconds, _cfg.GetCVar(DutyCCVars.DiscordStatusUpdateInterval));
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(interval);

        if (!ulong.TryParse(_cfg.GetCVar(DutyCCVars.DiscordStatusChannelId), out var channelId) || channelId == 0)
            return;

        if (channelId != _messageChannelId)
        {
            // Channel was changed at runtime — the old message ID belongs to a different channel.
            _messageId = null;
            _messageChannelId = channelId;
        }

        _updateInFlight = true;
        Log.Info($"DiscordStatusSystem: sending/editing status embed in channel {channelId}...");
        UpdateDiscordMessageAsync(channelId, BuildEmbed());
    }

    private EmbedProperties BuildEmbed()
    {
        var runLevel = _ticker.RunLevel;

        var statusText = runLevel switch
        {
            GameRunLevel.PreRoundLobby => "Лобби",
            GameRunLevel.InRound => "Идёт раунд",
            _ => "Раунд завершён",
        };

        var mapName = GetStationNames();
        if (mapName.Length == 0)
            mapName = _gameMapManager.GetSelectedMap()?.MapName ?? "—";

        var presetName = runLevel == GameRunLevel.PreRoundLobby
            ? "—"
            : _ticker.CurrentPreset is { } preset ? Loc.GetString(preset.ModeTitle) : "—";

        var color = runLevel == GameRunLevel.InRound
            ? new DiscordColor(0x57, 0xF2, 0x87)
            : new DiscordColor(0x58, 0x65, 0xF2);

        return new EmbedProperties()
            .WithTitle("Статус сервера")
            .WithColor(color)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .AddFields(
                new EmbedFieldProperties().WithName("Статус").WithValue(statusText).WithInline(true),
                new EmbedFieldProperties().WithName("Игроков онлайн").WithValue(_playerManager.PlayerCount.ToString()).WithInline(true),
                new EmbedFieldProperties().WithName("Карта").WithValue(mapName).WithInline(true),
                new EmbedFieldProperties().WithName("Пресет").WithValue(presetName).WithInline(true));
    }

    private string GetStationNames()
    {
        var stationNames = new StringBuilder();
        var query = EntityQueryEnumerator<StationJobsComponent, StationSpawningComponent, MetaDataComponent>();
        while (query.MoveNext(out _, out _, out var meta))
        {
            if (stationNames.Length > 0)
                stationNames.Append(", ");

            stationNames.Append(meta.EntityName);
        }

        return stationNames.ToString();
    }

    private async void UpdateDiscordMessageAsync(ulong channelId, EmbedProperties embed)
    {
        try
        {
            if (_messageId is { } messageId)
            {
                var edited = await _discord.EditEmbedAsync(channelId, messageId, embed);
                if (edited)
                {
                    Log.Info($"DiscordStatusSystem: edited status embed (message {messageId}).");
                    return;
                }

                // The message is gone (deleted, channel purged, etc.) — send a fresh one below.
                _messageId = null;
            }

            _messageId = await _discord.SendEmbedAsync(channelId, embed);
            Log.Info(_messageId is { } sentId
                ? $"DiscordStatusSystem: sent new status embed (message {sentId})."
                : "DiscordStatusSystem: SendEmbedAsync returned null (bot not connected or channel not found).");
        }
        catch (Exception e)
        {
            Log.Error($"Failed to update the Discord status embed: {e}");
        }
        finally
        {
            _updateInFlight = false;
        }
    }
}
