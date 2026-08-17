using System.Linq;
using Content.Server.AlertLevel;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Station.Systems;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Duty.ERT;

/// <summary>
/// _Duty: вызов отряда ОБР (ERT) консольными командами <c>ertcallnow</c> / <c>ertcall5min</c>.
/// Делает уведомление ЦК со звуком/цветом по варианту, через длительность звука +2с ставит код
/// угрозы gamma (чтобы klaxon не накладывался на звук вызова), и грузит шаттл ОБР на новую карту
/// (на неё нельзя попасть БСА — это уже готовая механика карт).
/// Шаттлы лежат в <see cref="GridDir"/>, на каждом заранее размещены 5 гост-роль спавнеров
/// (2 офицера + медик + инженер + лидер) и варп-маяк для призраков — загрузка грида создаёт всё сама.
/// <c>ertcall5min</c> делает уведомление и код угрозы сразу, а сам шаттл прибывает через 5 минут.
/// </summary>
public sealed class DutyErtSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IResourceManager _resource = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly AlertLevelSystem _alertLevel = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private const string GridDir = "/Maps/ADTMaps/Shuttles/ERT";
    private const string AlertCode = "gamma";

    /// <summary>Задержка прибытия шаттла для <c>ertcall5min</c>.</summary>
    public static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    /// <summary>Звук уведомления о вызове ОБР (тот же, что у команды <c>sendert</c>).</summary>
    private const string CallSound = "/Audio/Corvax/Adminbuse/yesert.ogg";

    /// <summary>
    /// Цвет уведомления + звук для каждого варианта (имя = имя файла шаттла без .yml).
    /// Звук пока у всех общий (<see cref="CallSound"/>) — поле на каждый вариант, чтобы потом легко развести по шаттлам.
    /// </summary>
    private static readonly Dictionary<string, (string Color, string Sound)> Variants = new()
    {
        ["cbun"]        = ("#F0E68C", CallSound),
        ["deathsquad"]  = ("#8B008B", "/Audio/_Duty/Announcement/FieldOfficer.ogg"),
        ["default"]     = ("#F08080", CallSound),
        ["default-rev"] = ("#DC143C", CallSound),
        ["engineer"]    = ("#DAA520", CallSound),
        ["janitor"]     = ("#FF00FF", CallSound),
        ["medical"]     = ("#00BFFF", CallSound),
        ["security"]    = ("#8B0000", CallSound),
    };

    public static IEnumerable<string> VariantNames => Variants.Keys;

    /// <summary>
    /// Отложенные действия: код угрозы gamma (чтобы его звук не накладывался на звук вызова) и
    /// прибытие шаттла для ertcall5min. Когда наступает срок — выполняем.
    /// </summary>
    private readonly List<(TimeSpan DueTime, Action Action)> _pending = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    /// <summary>
    /// Раунд перезапустился — отложенные действия из прошлого раунда (код угрозы на удалённой
    /// станции, отложенный шаттл ertcall5min) больше не актуальны. IGameTiming.CurTime не
    /// сбрасывается при рестарте раунда, поэтому без явной очистки эти замыкания рано или поздно
    /// сработали бы уже в новом раунде — с устаревшим stationUid/вариантом.
    /// </summary>
    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _pending.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pending.Count == 0)
            return;

        var now = _timing.CurTime;
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            if (_pending[i].DueTime > now)
                continue;

            var action = _pending[i].Action;
            _pending.RemoveAt(i);
            action();
        }
    }

    private void Schedule(TimeSpan delay, Action action) => _pending.Add((_timing.CurTime + delay, action));

    /// <summary>
    /// Основная точка входа из команд. Проверяет игрока/станцию/вариант, шлёт уведомление и код угрозы
    /// сразу, а шаттл грузит сейчас (delay = null) либо ставит в очередь на <paramref name="delay"/>.
    /// </summary>
    public void TryCallErt(IConsoleShell shell, ICommonSession? player, string variant, TimeSpan? delay)
    {
        variant = variant.ToLowerInvariant();

        if (player?.AttachedEntity is not { } playerEntity)
        {
            shell.WriteError(Loc.GetString("shell-only-players-can-run-this-command"));
            return;
        }

        if (!Variants.TryGetValue(variant, out var data))
        {
            shell.WriteError(Loc.GetString("duty-ert-error-type", ("types", string.Join(", ", VariantNames))));
            return;
        }

        var gridPath = $"{GridDir}/{variant}.yml";
        if (!_resource.TryContentFileRead(gridPath, out _))
        {
            shell.WriteError(Loc.GetString("duty-ert-error-path", ("path", gridPath)));
            return;
        }

        var station = _station.GetOwningStation(playerEntity);
        if (station == null)
        {
            shell.WriteError(Loc.GetString("duty-ert-error-no-station"));
            return;
        }

        // 1. Уведомление ЦК с цветом варианта. Звук играем сами (см. ниже) и ставим playSound:false:
        //    иначе Corvax-патч в DispatchGlobalAnnouncement затирает кастомный звук на дефолтный ЦК-звук
        //    (sender == «Центральное командование» совпадает с admin-announce-announcer-default).
        _chatSystem.DispatchGlobalAnnouncement(
            Loc.GetString($"duty-ert-{variant}-announcement"),
            Loc.GetString("duty-ert-announcer"),
            playSound: false,
            colorOverride: Color.FromHex(data.Color));

        _audio.PlayGlobal(new SoundPathSpecifier(data.Sound), Filter.Broadcast(), true,
            AudioParams.Default.WithVolume(-2f));

        // 2. Код угрозы — с задержкой на длительность звука вызова + 2с, чтобы klaxon кода
        //    не накладывался на звук вызова ОБР. Дед-сквад поднимает epsilon, остальные — gamma.
        var stationUid = station.Value;
        var alertCode = variant == "deathsquad" ? "epsilon" : AlertCode;
        var alertDelay = _audio.GetAudioLength(data.Sound) + TimeSpan.FromSeconds(2);
        Schedule(alertDelay,
            () => _alertLevel.SetLevel(stationUid, alertCode, playSound: true, announce: true, force: true, locked: true));

        // 3. Шаттл: сейчас либо отложенно (ertcall5min).
        if (delay is { } d && d > TimeSpan.Zero)
        {
            Schedule(d, () => SpawnShuttle(variant));
            shell.WriteLine(Loc.GetString("duty-ert-queued",
                ("variant", variant), ("minutes", (int) Math.Round(d.TotalMinutes))));
        }
        else
        {
            SpawnShuttle(variant);
            shell.WriteLine(Loc.GetString("duty-ert-spawned", ("variant", variant)));
        }
    }

    /// <summary>Создаёт новую карту, грузит на неё шаттл и снимает паузу («размораживает»).</summary>
    private void SpawnShuttle(string variant)
    {
        var gridPath = $"{GridDir}/{variant}.yml";

        var mapId = _mapManager.CreateMap();
        _metaData.SetEntityName(_mapManager.GetMapEntityId(mapId), Loc.GetString("duty-ert-map-name"));

        // InitializeMaps=true инициализирует карту (= снимает «заморозку»), при этом отрабатывает
        // MapInit на гост-роль спавнерах — 5 ОБРовцев становятся доступны как роли для призраков.
        var opts = new DeserializationOptions { StoreYamlUids = true, InitializeMaps = true };
        _mapLoader.TryLoadGrid(mapId, new ResPath(gridPath), out _, opts);
        _mapManager.SetMapPaused(mapId, false);

        _chat.SendAdminAlert(Loc.GetString("duty-ert-admin-alert", ("variant", variant), ("map", mapId)));
    }
}
