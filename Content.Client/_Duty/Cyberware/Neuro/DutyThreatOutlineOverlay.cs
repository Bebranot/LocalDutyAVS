// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Client._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Neuro;
using Content.Shared.Security.Components;
using Content.Shared.StatusIcon;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Cyberware.Neuro;

/// <summary>
/// _Duty: «Распознаватель угроз» — красный контур разыскиваемых и опасных по криминальной базе.
/// Тот же post-shader, что у обводки отряда нейроинтерфейса: силуэт целиком, без заливки слоёв.
/// </summary>
public sealed partial class DutyThreatOutlineOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> OutlineShader = "DutyNeuroSquadOutline";
    private const string PostShaderId = "duty-threat-outline";

    private static readonly ProtoId<SecurityIconPrototype> WantedIcon = "SecurityIconWanted";
    private static readonly ProtoId<SecurityIconPrototype> HostileIcon = "SecurityIconHostile";

    private static readonly Color ThreatColor = Color.FromHex("#FF2A3D");
    private const float BoundsMargin = 2f;

    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly ShaderInstance _shader;
    private readonly SpriteComponent.PostShaderEntry[] _post;
    private readonly SpriteSystem _sprite;
    private readonly SharedTransformSystem _transform;
    private readonly DutyCyberwareClientSystem _cyber;

    private EntityUid _local;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public DutyThreatOutlineOverlay()
    {
        IoCManager.InjectDependencies(this);

        _sprite = _entMan.System<SpriteSystem>();
        _transform = _entMan.System<SharedTransformSystem>();
        _cyber = _entMan.System<DutyCyberwareClientSystem>();

        _shader = _proto.Index(OutlineShader).InstanceUnique();
        _shader.SetParameter("outline_color", ThreatColor);
        _post = new[] { new SpriteComponent.PostShaderEntry(PostShaderId, _shader) };
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye == null || _player.LocalEntity is not { } local)
            return false;

        if (!_entMan.HasComponent<DutyThreatScannerComponent>(local) || _cyber.IsHostEmped(local))
            return false;

        _local = local;
        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not { } eye)
            return;

        var handle = args.WorldHandle;
        var visible = args.WorldAABB.Enlarged(BoundsMargin);
        var pulse = 0.75f + 0.25f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 5f);
        _shader.SetParameter("outline_color", ThreatColor.WithAlpha(pulse));

        var query = _entMan.EntityQueryEnumerator<CriminalRecordComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var record, out var sprite, out var xform))
        {
            if (uid == _local || record.StatusIcon != WantedIcon && record.StatusIcon != HostileIcon)
                continue;

            if (!sprite.Visible || sprite.ContainerOccluded || xform.MapID != args.MapId)
                continue;

            // «Призрак в сети» глушит и распознаватель: база его не видит, пока не сорван ЭМИ.
            if (_entMan.TryGetComponent(uid, out DutyNeuroGhostComponent? ghost) && ghost.Hidden)
                continue;

            var (position, rotation) = _transform.GetWorldPositionRotation(xform);
            if (!visible.Contains(position))
                continue;

            _sprite.RenderSprite((uid, sprite), handle, eye.Rotation, rotation, position, _post);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }
}
