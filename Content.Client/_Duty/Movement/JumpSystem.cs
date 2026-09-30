// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared._Duty.Movement;
using Content.Shared.Damage.Components;
using Robust.Client.Animations;
using Robust.Shared.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Movement;

/// <summary>
/// _Duty: клиентская часть прыжка, портировано из Space Onyx. Оригинал ещё проигрывал сжатие
/// спрайта через собственный <c>PrototypedAnimationPlayerSystem</c> и мерял высоту спрайта их
/// <c>MarkingBoundsHelper</c> (исключая слои маркингов) — у нас ни того ни другого нет, поэтому
/// высота берётся обычным <see cref="SpriteSystem.GetLocalBounds"/>, а подлёт спрайта (EmoteJump)
/// повторён обычной анимацией смещения через <see cref="AnimationPlayerSystem"/>.
/// </summary>
public sealed partial class JumpSystem : SharedJumpSystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly AnimationPlayerSystem _animation = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private const string HopAnimationKey = "duty-jump-hop";

    /// <summary>Ключи других анимаций, которые тоже двигают Sprite.Offset (усталость).</summary>
    private const string CombatStaminaAnimationKey = "stamina";
    private const string EnduranceShakeAnimationKey = "duty-endurance-shake";

    private static readonly TimeSpan HopLength = TimeSpan.FromSeconds(0.5);
    private static readonly Vector2 HopHeight = new(0f, 0.5f);

    /// <summary>
    /// «Земное» смещение спрайта на время подлёта. Подлёт двигает Sprite.Offset, и всё, что в этот
    /// момент запоминает текущий Offset как исходный (анимации усталости), запомнило бы точку в воздухе
    /// и потом вернуло бы спрайт туда — персонаж так и висел над полом. Эти системы берут исходное
    /// смещение отсюда (<see cref="TryGetRestingOffset"/>), а сам подлёт в конце всегда кладёт спрайт
    /// обратно на него — даже если его оборвали.
    /// </summary>
    private readonly Dictionary<EntityUid, Vector2> _restingOffsets = new();

    private JumpShadowOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new JumpShadowOverlay(EntityManager, _timing);
        _overlays.AddOverlay(_overlay);

        SubscribeLocalEvent<JumpComponent, AfterAutoHandleStateEvent>(OnJumpState);
        SubscribeLocalEvent<JumpComponent, AnimationCompletedEvent>(OnAnimationCompleted);
        SubscribeLocalEvent<JumpComponent, ComponentRemove>(OnRemove);
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<JumpShadowOverlay>();
        base.Shutdown();
    }

    /// <summary>Идёт ли подлёт, и если да — на каком смещении спрайт стоит «на земле».</summary>
    public bool TryGetRestingOffset(EntityUid uid, out Vector2 offset)
        => _restingOffsets.TryGetValue(uid, out offset);

    /// <summary>Свой прыжок — сразу, по предсказанию, а не с задержкой пинга.</summary>
    protected override void OnJumpStarted(Entity<JumpComponent> ent)
    {
        if (_timing.IsFirstTimePredicted)
            PlayHop(ent.Owner);
    }

    /// <summary>Чужой прыжок — по состоянию с сервера, пока он ещё идёт.</summary>
    private void OnJumpState(Entity<JumpComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        // Свой прыжок уже подброшен предсказанием в OnJumpStarted. Серверное подтверждение
        // приходит через RTT; при пинге больше длины подлёта предсказанный подлёт к этому моменту
        // уже закончился, и без этой отсечки персонаж подпрыгивал бы второй раз.
        if (ent.Owner == _playerManager.LocalEntity)
            return;

        if (ent.Comp.IsJumping && _timing.CurTime - ent.Comp.JumpStarted < HopLength)
            PlayHop(ent.Owner);
    }

    private void PlayHop(EntityUid uid)
    {
        // Уже подлетаем (свой предсказанный прыжок, повторное состояние) — второй раз не запускаем.
        if (_restingOffsets.ContainsKey(uid) ||
            _animation.HasRunningAnimation(uid, HopAnimationKey) ||
            !TryComp<SpriteComponent>(uid, out var sprite))
            return;

        // Анимации усталости тоже крутят Offset: вдвоём они дёргали бы спрайт вразнобой. Гасим их
        // и берём их исходное смещение как «землю» — они сами перезапустятся после приземления.
        var resting = sprite.Offset;
        if (_animation.HasRunningAnimation(uid, CombatStaminaAnimationKey) &&
            TryComp<StaminaComponent>(uid, out var stamina))
        {
            resting = stamina.StartOffset;
            _animation.Stop(uid, CombatStaminaAnimationKey);
        }

        if (_animation.HasRunningAnimation(uid, EnduranceShakeAnimationKey) &&
            TryComp<DutyStaminaComponent>(uid, out var endurance))
        {
            resting = endurance.StartOffset;
            _animation.Stop(uid, EnduranceShakeAnimationKey);
        }

        _restingOffsets[uid] = resting;
        _animation.Play(uid, new Animation
        {
            Length = HopLength,
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Cubic,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(resting, 0f),
                        new AnimationTrackProperty.KeyFrame(resting + HopHeight, 0.25f),
                        new AnimationTrackProperty.KeyFrame(resting, 0.25f),
                    },
                },
            },
        }, HopAnimationKey);
    }

    private void OnAnimationCompleted(Entity<JumpComponent> ent, ref AnimationCompletedEvent args)
    {
        if (args.Key != HopAnimationKey || !_restingOffsets.Remove(ent.Owner, out var resting))
            return;

        // И при нормальном завершении, и при обрыве — спрайт строго на землю.
        if (TryComp<SpriteComponent>(ent, out var sprite))
            _sprite.SetOffset((ent.Owner, sprite), resting);
    }

    private void OnRemove(Entity<JumpComponent> ent, ref ComponentRemove args)
    {
        _restingOffsets.Remove(ent.Owner);
    }
}

public sealed class JumpShadowOverlay(IEntityManager entities, IGameTiming timing) : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    private const float HopDuration = 0.5f;

    private readonly SharedTransformSystem _transform = entities.System<SharedTransformSystem>();
    private readonly SpriteSystem _sprite = entities.System<SpriteSystem>();

    protected override void Draw(in OverlayDrawArgs args)
    {
        var eyeRot = args.Viewport.Eye?.Rotation ?? Angle.Zero;
        var screenDown = (-eyeRot).RotateVec(new Vector2(0f, -1f));
        var screenTilt = Matrix3Helpers.CreateRotation(-eyeRot);
        var now = timing.CurTime;

        var query = entities.EntityQueryEnumerator<JumpComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var jump, out var sprite, out var xform))
        {
            var elapsed = (now - jump.JumpStarted).TotalSeconds;
            if (elapsed < 0 || elapsed > HopDuration)
                continue;

            var progress = elapsed / HopDuration;
            var height = MathF.Sin((float) progress * MathF.PI);
            var feet = _transform.GetWorldPosition(xform) +
                screenDown * (_sprite.GetLocalBounds((uid, sprite)).Height / 2f);
            if (!args.WorldAABB.Contains(feet))
                continue;

            args.WorldHandle.SetTransform(Matrix3x2.CreateScale(1f + height * 1.5f, 0.45f + height * 0.3f) *
                                          screenTilt *
                                          Matrix3Helpers.CreateTranslation(feet));
            args.WorldHandle.DrawCircle(Vector2.Zero, 0.22f, Color.Black.WithAlpha(0.35f - height * 0.15f));
        }

        args.WorldHandle.SetTransform(Matrix3x2.Identity);
    }
}
