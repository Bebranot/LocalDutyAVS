// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Neurointerface;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Duty.Neurointerface.Tablet;

/// <summary>
/// _Duty: клиентский BoundUserInterface тактического планшета управления отрядами NT-SEC.
/// Открывает <see cref="DutyNeuroTabletWindow"/>, передаёт обновления состояния и отправляет
/// серверу команды приказов, отрядов, радиосигналов, таймеров и задач.
/// </summary>
[UsedImplicitly]
public sealed class DutyNeuroTabletBoundUserInterface(EntityUid owner, Enum uiKey)
    : BoundUserInterface(owner, uiKey)
{
    private DutyNeuroTabletWindow? _window;

    public DutyNeuroTabletBuiState? CurrentState => State as DutyNeuroTabletBuiState;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<DutyNeuroTabletWindow>();

        EntityUid? initialGrid = null;
        var initialStationName = string.Empty;

        if (EntMan.TryGetComponent<DutyNeurointerfaceTabletComponent>(Owner, out var tablet))
        {
            EntMan.TryGetEntity(tablet.TargetGrid, out initialGrid);
            initialStationName = tablet.StationName;
        }

        if (initialGrid == null && EntMan.TryGetComponent<TransformComponent>(Owner, out var xform))
            initialGrid = xform.GridUid;

        if (initialGrid != null && string.IsNullOrEmpty(initialStationName) &&
            EntMan.TryGetComponent<MetaDataComponent>(initialGrid, out var gridMeta))
        {
            initialStationName = gridMeta.EntityName;
        }

        _window.SetInitialMap(initialStationName, initialGrid);

        _window.OnCreateOrder += (orderType, coords, zoneName) =>
            SendMessage(new DutyNeuroCreateOrderMessage(orderType, coords, zoneName));

        _window.OnRemoveOrder += orderId =>
            SendMessage(new DutyNeuroRemoveOrderMessage(orderId));

        _window.OnClearOrders += () =>
            SendMessage(new DutyNeuroClearOrdersMessage());

        _window.OnSelectSquad += squadId =>
            SendMessage(new DutyNeuroSelectSquadMessage(squadId));

        _window.OnCreateSquad += callsign =>
            SendMessage(new DutyNeuroCreateSquadMessage(callsign));

        _window.OnRenameSquad += (squadId, newCallsign) =>
            SendMessage(new DutyNeuroRenameSquadMessage(squadId, newCallsign));

        _window.OnDisbandSquad += squadId =>
            SendMessage(new DutyNeuroDisbandSquadMessage(squadId));

        _window.OnInviteMember += (squadId, targetEntity) =>
            SendMessage(new DutyNeuroInviteMemberMessage(squadId, targetEntity));

        _window.OnRemoveMember += (squadId, targetEntity) =>
            SendMessage(new DutyNeuroRemoveMemberMessage(squadId, targetEntity));

        _window.OnSetLeader += (squadId, targetEntity) =>
            SendMessage(new DutyNeuroSetLeaderMessage(squadId, targetEntity));

        _window.OnSendCallout += (calloutType, zoneName) =>
            SendMessage(new DutyNeuroSendCalloutMessage(calloutType, zoneName));

        _window.OnCreateTimer += (title, seconds) =>
            SendMessage(new DutyNeuroCreateTimerMessage(title, seconds));

        _window.OnTimerAction += (timerId, action) =>
            SendMessage(new DutyNeuroTimerActionMessage(timerId, action));

        _window.OnCreateTask += (title, desc, assignee, priority) =>
            SendMessage(new DutyNeuroCreateTaskMessage(title, desc, assignee, priority));

        _window.OnToggleTask += taskId =>
            SendMessage(new DutyNeuroToggleTaskMessage(taskId));

        _window.OnDeleteTask += taskId =>
            SendMessage(new DutyNeuroDeleteTaskMessage(taskId));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is DutyNeuroTabletBuiState tabletState)
            _window?.UpdateState(tabletState);
    }

    protected override void Dispose(bool disposing)
    {
        // Отписываем карту и радиальное меню до закрытия окна: меню живёт в ModalRoot и не должно
        // вызвать приказ в уже уничтоженный BUI.
        if (disposing)
            _window?.Detach();

        base.Dispose(disposing);
    }
}
