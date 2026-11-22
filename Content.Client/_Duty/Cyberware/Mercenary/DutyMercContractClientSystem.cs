// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Cyberware.Mercenary;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Cyberware.Mercenary;

/// <summary>
/// _Duty: клиент наёмника — мировая метка «Контракта» и экран «КОНТРАКТ ЗАКРЫТ / РАСТОРГНУТ».
/// Оверлеи висят всегда, но рисуют себя только при наличии данных: дешевле, чем добавлять/снимать по событиям тела.
/// </summary>
public sealed partial class DutyMercContractClientSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Сколько держится экран завершения контракта.</summary>
    public const float EndedDuration = 4.8f;

    private DutyMercContractMarkerOverlay _marker = default!;
    private DutyMercContractEndedOverlay _ended = default!;

    /// <summary>Момент (RealTime) получения события завершения; null — экрана нет.</summary>
    private TimeSpan? _endedAt;

    public bool EndedTerminated { get; private set; }
    public string EndedTargetName { get; private set; } = string.Empty;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<DutyMercContractEndedEvent>(OnContractEnded);

        _marker = new DutyMercContractMarkerOverlay();
        _ended = new DutyMercContractEndedOverlay();
        _overlays.AddOverlay(_marker);
        _overlays.AddOverlay(_ended);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlays.RemoveOverlay(_marker);
        _overlays.RemoveOverlay(_ended);
    }

    private void OnContractEnded(DutyMercContractEndedEvent ev)
    {
        _endedAt = _timing.RealTime;
        EndedTerminated = ev.Terminated;
        EndedTargetName = ev.TargetName;
    }

    /// <summary>Сколько секунд прошло с завершения контракта; отрицательное — экрана нет.</summary>
    public float EndedElapsed
    {
        get
        {
            if (_endedAt is not { } at)
                return -1f;

            var elapsed = (float) (_timing.RealTime - at).TotalSeconds;
            if (elapsed > EndedDuration)
            {
                _endedAt = null;
                return -1f;
            }

            return elapsed;
        }
    }

    public bool TryGetLocalTracker(out EntityUid uid, out DutyMercContractTrackerComponent tracker)
    {
        if (_player.LocalEntity is { } local && TryComp(local, out DutyMercContractTrackerComponent? comp))
        {
            uid = local;
            tracker = comp;
            return true;
        }

        uid = default;
        tracker = default!;
        return false;
    }
}
