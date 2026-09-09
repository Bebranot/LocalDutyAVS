// SPDX-FileCopyrightText: 2026 LocalDuty
// SPDX-License-Identifier: MIT

using System.Linq;
using Content.Shared._Duty.SpawnMenu;
using Robust.Client.Placement;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client._Duty.SpawnMenu;

/// <summary>
/// _Duty: клиентская половина меню выдачи предметов. Окно и режим размещения — здесь,
/// все проверки — на сервере (см. Content.Server/_Duty/SpawnMenu).
/// </summary>
public sealed class DutySpawnMenuSystem : EntitySystem
{
    [Dependency] private readonly IPlacementManager _placement = default!;

    /// <summary>
    /// Режим размещения из движка: свободная точка, но с проверкой дистанции до игрока
    /// (<c>RangeRequired</c>), плюс движок сам рисует круг радиуса.
    /// </summary>
    private const string PlacementMode = "PlaceNearby";

    private DutySpawnMenuWindow? _window;

    /// <summary>Дистанция выдачи в клетках — приходит с сервера вместе со списком.</summary>
    private float _range = 3f;

    /// <summary>Прототип, который сейчас в руке курсора; null — режим размещения выключен.</summary>
    private string? _placing;

    public override void Initialize()
    {
        SubscribeNetworkEvent<DutySpawnMenuStateEvent>(OnState);
    }

    public override void Shutdown()
    {
        StopPlacing();
        _window?.Dispose();
        _window = null;
    }

    /// <summary>
    /// Открывает окно (запросив у сервера актуальный список) или закрывает уже открытое.
    /// </summary>
    public void ToggleWindow()
    {
        if (_window is { IsOpen: true })
        {
            _window.Close();
            StopPlacing();
            return;
        }

        RaiseNetworkEvent(new DutySpawnMenuRequestEvent());
    }

    /// <summary>Вызывается хиджаком размещения по клику в мире.</summary>
    public void RequestSpawn(string proto, EntityCoordinates coordinates)
    {
        RaiseNetworkEvent(new DutySpawnMenuSpawnEvent(proto, GetNetCoordinates(coordinates)));
    }

    private void OnState(DutySpawnMenuStateEvent ev)
    {
        if (_window == null)
        {
            _window = new DutySpawnMenuWindow();
            _window.OnItemSelected += StartPlacing;
            _window.OnClose += StopPlacing;
        }

        _range = ev.Range;
        _window.Populate(ev.Entries, ev.Range);

        if (!_window.IsOpen)
            _window.OpenCentered();

        // Лимит выбранного предмета мог закончиться этой же выдачей — тогда выходим
        // из режима размещения, чтобы следующий клик не улетел в отказ.
        if (_placing != null)
        {
            var entry = ev.Entries.FirstOrDefault(e => e.Proto == _placing);
            if (entry.Proto != _placing || entry.Remaining == 0)
                StopPlacing();
        }
    }

    private void StartPlacing(string proto)
    {
        _placing = proto;

        _placement.BeginPlacing(new PlacementInformation
        {
            EntityType = proto,
            IsTile = false,
            PlacementOption = PlacementMode,
            Range = (int) MathF.Ceiling(_range),
            UseEditorContext = true,
        }, new DutySpawnPlacementHijack(this, proto));
    }

    private void StopPlacing()
    {
        if (_placing == null)
            return;

        _placing = null;
        _placement.Clear();
    }
}
