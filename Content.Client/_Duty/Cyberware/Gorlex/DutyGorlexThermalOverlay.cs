// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client.ADT.VisionOverlay;
using Content.Shared.Body;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.Cyberware.Gorlex;

/// <summary>
/// _Duty: тепловые силуэты мобов сквозь стены, но только в радиусе импланта. Рисуем тем же шейдером, что и
/// ADT-термовизор, но с отсечением по расстоянию до глаза — это и есть ограничение «6–8 тайлов».
/// </summary>
public sealed class DutyGorlexThermalOverlay : BaseEntityHighlightOverlay
{
    public float Range = 7f;

    public DutyGorlexThermalOverlay(ShaderPrototype shader) : base(shader)
    {
        ZIndex = (int) OverlayZIndexes.ThermalVisionEntityHighlight;
    }

    protected override void DrawHighlights(in OverlayDrawArgs args, Angle eyeRotation)
    {
        if (args.Viewport.Eye is not { } eye)
            return;

        var origin = eye.Position.Position;
        var rangeSquared = Range * Range;

        var query = _entityManager.EntityQueryEnumerator<BodyComponent, MetaDataComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var meta, out var sprite, out var xform))
        {
            if (xform.MapID != args.MapId || _containerSystem.IsEntityInContainer(uid, meta))
                continue;

            var pos = _transform.GetWorldPosition(xform);
            if ((pos - origin).LengthSquared() > rangeSquared)
                continue;

            DrawEntity(args, eyeRotation, sprite, xform);
        }
    }
}
