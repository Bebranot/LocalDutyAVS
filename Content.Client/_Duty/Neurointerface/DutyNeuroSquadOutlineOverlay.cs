// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared._Duty.Neurointerface;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Neurointerface;

/// <summary>
/// _Duty: обводка соотрядцев носителя нейроинтерфейса, видимая сквозь стены.
/// Подход как у тепловизора ADT (<c>BaseEntityHighlightOverlay</c>) и <c>DroneVisionOverlay</c>:
/// спрайт перерисовывается через <c>SpriteSystem.RenderSprite</c> в <see cref="OverlaySpace.WorldSpace"/>, который Clyde рисует ПОСЛЕ света и FOV-маски,
/// поэтому контур не перекрывается ни стенами, ни затемнением вне поля зрения.
/// Спрайт идёт через post-shader (весь силуэт в буфер, затем контур), шейдер рисует только внешний контур.
/// </summary>
public sealed partial class DutyNeuroSquadOutlineOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> OutlineShader = "DutyNeuroSquadOutline";

    private const string PostShaderId = "duty-neuro-squad-outline";

    private static readonly Color AllyColor = Color.FromHex("#3D8BFF");
    private static readonly Color CriticalColor = Color.FromHex("#FFA51F");
    /// <summary>Своих в тактической сети Горлекса обводим ржавым — под палитру их HUD.</summary>
    private static readonly Color GorlexAllyColor = Color.FromHex("#D9302B");

    // Запас к видимой области: позиция — центр сущности, а спрайт выступает за неё.
    private const float BoundsMargin = 2f;

    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    // Два инстанса вместо смены параметра между отрисовками: параметры шейдера читаются при
    // сбросе батча, и перекраска одного инстанса посреди кадра окрасила бы всех одинаково.
    private readonly ShaderInstance _allyShader;
    private readonly ShaderInstance _criticalShader;
    private readonly ShaderInstance _gorlexShader;

    private readonly SpriteSystem _sprite;
    private readonly SharedTransformSystem _transform;
    private readonly DutyNeuroClientSystem _clientSystem;
    private readonly EntityQuery<SpriteComponent> _spriteQuery;
    private readonly EntityQuery<TransformComponent> _xformQuery;

    // Готовые списки post-шейдеров: создаются один раз, чтобы не аллоцировать в Draw.
    private readonly SpriteComponent.PostShaderEntry[] _allyPost;
    private readonly SpriteComponent.PostShaderEntry[] _criticalPost;
    private readonly SpriteComponent.PostShaderEntry[] _gorlexPost;

    private EntityUid _local;
    private DutyNeurointerfaceWearerComponent? _wearer;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public DutyNeuroSquadOutlineOverlay()
    {
        IoCManager.InjectDependencies(this);

        _sprite = _entMan.System<SpriteSystem>();
        _transform = _entMan.System<SharedTransformSystem>();
        _clientSystem = _entMan.System<DutyNeuroClientSystem>();
        _spriteQuery = _entMan.GetEntityQuery<SpriteComponent>();
        _xformQuery = _entMan.GetEntityQuery<TransformComponent>();

        var proto = _proto.Index(OutlineShader);
        _allyShader = proto.InstanceUnique();
        _allyShader.SetParameter("outline_color", AllyColor);
        _criticalShader = proto.InstanceUnique();
        _criticalShader.SetParameter("outline_color", CriticalColor);
        _gorlexShader = proto.InstanceUnique();
        _gorlexShader.SetParameter("outline_color", GorlexAllyColor);

        _allyPost = new[] { new SpriteComponent.PostShaderEntry(PostShaderId, _allyShader) };
        _criticalPost = new[] { new SpriteComponent.PostShaderEntry(PostShaderId, _criticalShader) };
        _gorlexPost = new[] { new SpriteComponent.PostShaderEntry(PostShaderId, _gorlexShader) };
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye == null)
            return false;

        if (!_clientSystem.TryGetActiveWearer(out _local, out var wearer))
            return false;

        // Мёртвому носителю нейроинтерфейс уже ничего не показывает (идёт flatline).
        // Под ЭМИ панель погашена целиком — обводка тоже.
        if (wearer.OwnerStatus == DutyNeuroMemberStatus.Dead || wearer.Jammed || wearer.SquadMembers.Count == 0)
            return false;

        _wearer = wearer;
        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_wearer == null || args.Viewport.Eye is not { } eye)
            return;

        var handle = args.WorldHandle;
        var eyeRotation = eye.Rotation;
        var visible = args.WorldAABB.Enlarged(BoundsMargin);

        // Лёгкий пульс прозрачности — «живой» сигнал интерфейса, а не статичная рамка.
        var pulse = 0.8f + 0.2f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 3f);
        _allyShader.SetParameter("outline_color", AllyColor.WithAlpha(pulse));
        _criticalShader.SetParameter("outline_color", CriticalColor.WithAlpha(pulse));
        _gorlexShader.SetParameter("outline_color", GorlexAllyColor.WithAlpha(pulse));
        var allyPost = _wearer.Network == DutyNeuroNetwork.Gorlex ? _gorlexPost : _allyPost;

        foreach (var member in _wearer.SquadMembers)
        {
            // Мёртвых сервер уже отфильтровал, но запись могла прийти до обновления статуса.
            // Офлайн (SSD, тело без игрока) обводим: соотрядца надо найти и тогда, когда он не за клавиатурой.
            // «Призрак в сети» не даёт сигнала — и контура тоже.
            if (member.Status == DutyNeuroMemberStatus.Dead || member.NoSignal)
                continue;

            // Вне PVS сущности на клиенте нет — тогда просто не рисуем.
            if (!_entMan.TryGetEntity(member.Entity, out var uidNullable) || uidNullable is not { } uid)
                continue;

            if (uid == _local)
                continue;

            if (!_spriteQuery.TryGetComponent(uid, out var sprite) ||
                !_xformQuery.TryGetComponent(uid, out var xform))
                continue;

            // В шкафу/мешке спрайт скрыт контейнером — контур посреди ящика только путал бы.
            if (!sprite.Visible || sprite.ContainerOccluded || xform.MapID != args.MapId)
                continue;

            var (position, rotation) = _transform.GetWorldPositionRotation(xform);
            if (!visible.Contains(position))
                continue;

            var post = member.Status == DutyNeuroMemberStatus.Critical ? _criticalPost : allyPost;
            DrawOutline(handle, post, (uid, sprite), eyeRotation, rotation, position);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    private void DrawOutline(
        DrawingHandleWorld handle,
        SpriteComponent.PostShaderEntry[] postShaders,
        Entity<SpriteComponent> ent,
        Angle eyeRotation,
        Angle worldRotation,
        Vector2 worldPosition)
    {
        // Через post-shader движок сначала собирает ВЕСЬ спрайт (тело, одежда, волосы) в отдельный буфер,
        // а шейдер обводит уже готовый силуэт. Если же рисовать слои по одному под шейдером, контур получает
        // каждый слой, и внутренние края заливают бойца синим целиком.
        _sprite.RenderSprite(ent, handle, eyeRotation, worldRotation, worldPosition, postShaders);
    }
}
