// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Cyberware.Gorlex;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.GameStates;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.Cyberware.Gorlex;

/// <summary>_Duty: включает/выключает оверлей термовизора Горлекса у локального игрока по состоянию маркера носителя.</summary>
public sealed partial class DutyGorlexThermalClientSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    // Тот же шейдер, что у ADT-термовизора: силуэт тела без своих цветов спрайта.
    private const string BodyShaderId = "ADTThermalBodyShader";

    private DutyGorlexThermalOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyGorlexThermalHostComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<DutyGorlexThermalHostComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<DutyGorlexThermalHostComponent, LocalPlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<DutyGorlexThermalHostComponent, LocalPlayerDetachedEvent>(OnDetached);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Disable();
    }

    private void OnState(Entity<DutyGorlexThermalHostComponent> ent, ref AfterAutoHandleStateEvent args) => Refresh(ent);
    private void OnAttached(Entity<DutyGorlexThermalHostComponent> ent, ref LocalPlayerAttachedEvent args) => Refresh(ent);
    private void OnDetached(Entity<DutyGorlexThermalHostComponent> ent, ref LocalPlayerDetachedEvent args) => Disable();

    private void OnRemove(Entity<DutyGorlexThermalHostComponent> ent, ref ComponentRemove args)
    {
        if (ent.Owner == _player.LocalEntity)
            Disable();
    }

    private void Refresh(Entity<DutyGorlexThermalHostComponent> ent)
    {
        if (ent.Owner != _player.LocalEntity)
            return;

        if (!ent.Comp.Active)
        {
            Disable();
            return;
        }

        if (_overlay == null)
        {
            if (!_prototypes.TryIndex<ShaderPrototype>(BodyShaderId, out var shader))
                return;

            _overlay = new DutyGorlexThermalOverlay(shader);
            _overlays.AddOverlay(_overlay);
        }

        _overlay.Range = ent.Comp.Range;
    }

    private void Disable()
    {
        if (_overlay == null)
            return;

        _overlays.RemoveOverlay(_overlay);
        _overlay = null;
    }
}
