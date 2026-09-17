using System.IO;
using System.Text;
using System.Text.Json;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Server.Station.Components;
using Content.Shared.CCVar;
using NetCord.Rest;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using DiscordColor = NetCord.Color;
using DiscordLinkService = Content.Server.Discord.DiscordLink.DiscordLink;
using GameRunLevel = Content.Server.GameTicking.GameRunLevel;

namespace Content.Server._Duty.DiscordStatus;

/// <summary>
/// _Duty: держит одно и то же сообщение в Discord-канале <see cref="DutyCCVars.DiscordStatusChannelId"/>
/// в актуальном состоянии (карта, пресет, число игроков онлайн, лобби/раунд), редактируя его вместо
/// того, чтобы спамить новыми.
///
/// Обновление триггерится СОБЫТИЯМИ (подключение/отключение игрока, смена <see cref="GameRunLevel"/>),
/// а не только периодическим <see cref="Update"/> — это принципиально: движок ставит всю симуляцию
/// на паузу через встроенный <c>game.auto_pause_empty</c> (<see cref="Robust.Server.BaseServer"/>),
/// когда на сервере не остаётся ни одного игрока, и пока пауза активна, <see cref="Update"/> ни у одной
/// EntitySystem не вызывается вообще. Событие отключения последнего игрока летит по сетевой шине
/// до постановки на паузу, так что реакция на него всё ещё успевает уйти в Discord — а периодический
/// Update() в этот момент уже не сработал бы. <see cref="Update"/> остаётся как доп. подстраховка на
/// то время, пока сервер не пуст (раз в <see cref="DutyCCVars.DiscordStatusUpdateInterval"/> секунд).
///
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
    private const string StateFileName = "discord_status_message.json";

    private TimeSpan _nextUpdate;
    private ulong? _messageId;
    private ulong _messageChannelId;
    private bool _updateInFlight;

    private static string StateFilePath =>
        Path.Combine(AppContext.BaseDirectory, "data", StateFileName);

    private sealed record PersistedMessage(ulong ChannelId, ulong MessageId);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;

        Log.Info($"DiscordStatusSystem initialized. enabled={_cfg.GetCVar(DutyCCVars.DiscordStatusEnabled)} " +
                 $"channel='{_cfg.GetCVar(DutyCCVars.DiscordStatusChannelId)}' " +
                 $"interval={_cfg.GetCVar(DutyCCVars.DiscordStatusUpdateInterval)}");

        // Переживает рестарт процесса: без этого каждый рестарт сервиса (билд/деплой/краш) терял
        // _messageId и слал НОВОЕ сообщение вместо правки старого — отсюда дубликаты в канале.
        LoadPersistedMessage();

        // Сразу постим начальное состояние при старте сервера — не ждём Update(), т.к. при 0 игроков
        // симуляция может быть на паузе с самого запуска (game.auto_pause_empty) и Update() не тикнет
        // вообще, пока кто-то не зайдёт.
        TriggerUpdateNow();
    }

    private void LoadPersistedMessage()
    {
        try
        {
            if (!File.Exists(StateFilePath))
                return;

            var json = File.ReadAllText(StateFilePath);
            var state = JsonSerializer.Deserialize<PersistedMessage>(json);
            if (state == null)
                return;

            _messageChannelId = state.ChannelId;
            _messageId = state.MessageId;
        }
        catch (Exception e)
        {
            Log.Warning($"DiscordStatusSystem: failed to load persisted message state: {e}");
        }
    }

    private void SavePersistedMessage(ulong channelId, ulong messageId)
    {
        try
        {
            var dir = Path.GetDirectoryName(StateFilePath);
            if (dir != null)
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(new PersistedMessage(channelId, messageId));
            File.WriteAllText(StateFilePath, json);
        }
        catch (Exception e)
        {
            Log.Warning($"DiscordStatusSystem: failed to persist message state: {e}");
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent args)
    {
        // Лобби → раунд и раунд → лобби (в т.ч. после рестарта) не должны ждать до конца интервала.
        TriggerUpdateNow();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        // Только Connected/Disconnected меняют то, что показывает эмбед (число игроков онлайн).
        // Критично реагировать именно здесь, а не только в Update() — см. class-doc про auto-pause:
        // отключение последнего игрока фактически ставит сервер на паузу сразу вслед за этим событием,
        // так что это последний шанс отправить актуальное "0 игроков" в Discord.
        if (args.NewStatus is SessionStatus.Connected or SessionStatus.Disconnected)
            TriggerUpdateNow();
    }

    public override void Update(float frameTime)
    {
        try
        {
            if (_timing.CurTime >= _nextUpdate)
                TriggerUpdateNow();
        }
        catch (Exception e)
        {
            Log.Error($"DiscordStatusSystem.Update threw: {e}");
        }
    }

    private void TriggerUpdateNow()
    {
        if (_updateInFlight)
            return;

        if (!_cfg.GetCVar(DutyCCVars.DiscordStatusEnabled))
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
                    SavePersistedMessage(channelId, messageId);
                    return;
                }

                // The message is gone (deleted, channel purged, etc.) — send a fresh one below.
                _messageId = null;
            }

            _messageId = await _discord.SendEmbedAsync(channelId, embed);
            if (_messageId is { } sentId)
            {
                Log.Info($"DiscordStatusSystem: sent new status embed (message {sentId}).");
                SavePersistedMessage(channelId, sentId);
            }
            else
            {
                Log.Info("DiscordStatusSystem: SendEmbedAsync returned null (bot not connected or channel not found).");
            }
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
