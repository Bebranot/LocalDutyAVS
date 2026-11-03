// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx. Отличие: оригинал фильтрует по BodyComponent.ThermalVisibility
// (поле их собственной переписки Body, которую мы не переносили) — у нас вместо него
// MobStateComponent, то есть силуэтятся все живые мобы, а не только "тепловые" тела.

using System.Numerics;
using Content.Client.Stealth;
using Content.Shared.Mobs.Components;
using Content.Shared.Stealth.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;

namespace Content.Client._Duty.Drone;

public sealed partial class DroneVisionOverlay : Overlay
{
    [Dependency] private IEntityManager _entity = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IEyeManager _eye = default!;

    private readonly ContainerSystem _container;
    private readonly SpriteSystem _sprite;
    private readonly StealthSystem _stealth;
    private readonly TransformSystem _transform;
    private readonly HashSet<EntityUid> _rendered = [];

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public DroneVisionOverlay()
    {
        IoCManager.InjectDependencies(this);
        _container = _entity.System<ContainerSystem>();
        _sprite = _entity.System<SpriteSystem>();
        _stealth = _entity.System<StealthSystem>();
        _transform = _entity.System<TransformSystem>();
        ZIndex = -1;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
        => args.Viewport.Eye == _eye.CurrentEye;

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not { } eye || _player.LocalEntity is not { } player)
            return;

        var mapId = eye.Position.MapId;
        var eyeRotation = eye.Rotation;
        _rendered.Clear();

        var entities = _entity.EntityQueryEnumerator<MobStateComponent, SpriteComponent, TransformComponent>();
        while (entities.MoveNext(out var uid, out _, out var sprite, out var transform))
        {
            if (uid == player || !CanSee(uid, sprite))
                continue;

            var entity = uid;
            if (_container.TryGetOuterContainer(uid, transform, out var container) &&
                _entity.TryGetComponent(container.Owner, out SpriteComponent? ownerSprite) &&
                _entity.TryGetComponent(container.Owner, out TransformComponent? ownerTransform))
            {
                entity = container.Owner;
                sprite = ownerSprite;
                transform = ownerTransform;
            }

            if (!_rendered.Add(entity) || transform.MapID != mapId || !CanSee(entity, sprite))
                continue;

            var originalColor = sprite.Color;
            _sprite.SetColor((entity, sprite), Color.Black);
            _sprite.RenderSprite((entity, sprite), args.WorldHandle,
                eyeRotation,
                _transform.GetWorldRotation(transform),
                _transform.GetWorldPosition(transform));
            _sprite.SetColor((entity, sprite), originalColor);
        }

        args.WorldHandle.SetTransform(Matrix3x2.Identity);
    }

    private bool CanSee(EntityUid uid, SpriteComponent sprite)
    {
        return sprite.Visible && (!_entity.TryGetComponent(uid, out StealthComponent? stealth) ||
                                  _stealth.GetVisibility(uid, stealth) > 0.5f);
    }
}
