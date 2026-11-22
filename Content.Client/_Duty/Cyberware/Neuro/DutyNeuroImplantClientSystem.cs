// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client.Overlays;
using Content.Shared._Duty.Cyberware.Neuro;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;

namespace Content.Client._Duty.Cyberware.Neuro;

/// <summary>
/// _Duty: клиент нейросетевых имплантов — оверлеи навигатора и распознавателя угроз,
/// плюс «Призрак в сети» прячет свой криминальный статус от распознавателя.
/// </summary>
public sealed partial class DutyNeuroImplantClientSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;

    private DutyNavigatorOverlay _navigatorOverlay = default!;
    private DutyThreatOutlineOverlay _threatOverlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        // after: иконку статуса кладёт ShowCriminalRecordIconsSystem — снимать её надо после него.
        SubscribeLocalEvent<DutyNeuroGhostComponent, GetStatusIconsEvent>(OnGhostStatusIcons,
            after: new[] { typeof(ShowCriminalRecordIconsSystem) });

        _navigatorOverlay = new DutyNavigatorOverlay();
        _threatOverlay = new DutyThreatOutlineOverlay();
        _overlays.AddOverlay(_navigatorOverlay);
        _overlays.AddOverlay(_threatOverlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlays.RemoveOverlay(_navigatorOverlay);
        _overlays.RemoveOverlay(_threatOverlay);
    }

    private void OnGhostStatusIcons(Entity<DutyNeuroGhostComponent> ent, ref GetStatusIconsEvent args)
    {
        if (!ent.Comp.Hidden ||
            _player.LocalEntity is not { } local ||
            !HasComp<DutyThreatScannerComponent>(local))
        {
            return;
        }

        args.StatusIcons.RemoveAll(icon => icon is SecurityIconPrototype);
    }
}
