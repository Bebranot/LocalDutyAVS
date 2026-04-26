// SPDX-FileCopyrightText: 2025 LocalDuty
// SPDX-License-Identifier: MIT

using Content.Shared._Duty.PocketPlayer;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.PocketPlayer;

public sealed class PocketPlayerSystem : SharedPocketPlayerSystem
{
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        // Когда сервер обновил состояние компонента — обновляем UI
        SubscribeLocalEvent<PocketPlayerComponent, AfterAutoHandleStateEvent>(OnStateUpdate);
        _protoManager.PrototypesReloaded += OnProtoReload;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _protoManager.PrototypesReloaded -= OnProtoReload;
    }

    private void OnStateUpdate(Entity<PocketPlayerComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!_uiSystem.TryGetOpenUi<PocketPlayerBoundUserInterface>(ent.Owner, PocketPlayerUiKey.Key, out var bui))
            return;

        bui.Reload();
    }

    private void OnProtoReload(PrototypesReloadedEventArgs obj)
    {
        if (!obj.WasModified<DutyTrackPrototype>())
            return;

        var query = AllEntityQuery<PocketPlayerComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out _, out var ui))
        {
            if (!_uiSystem.TryGetOpenUi<PocketPlayerBoundUserInterface>((uid, ui), PocketPlayerUiKey.Key, out var bui))
                continue;

            bui.PopulateTracks();
        }
    }
}
