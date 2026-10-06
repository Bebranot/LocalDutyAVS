// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared.ActionBlocker;
using Content.Shared.Buckle;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Gravity;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Weapons.Halberd;

/// <summary>
/// _Duty: рывок алебардой. Всё, что двигает тело и решает исход, — здесь и предсказывается у
/// самого бегущего; урон цели, реплики в чат и зацикленный звук — серверные хуки.
///
/// Раньше рывок жил только на сервере: тело телепортировалось каждый тик с выключенными
/// коллизиями, а клиент видел свой рывок скачками по ~0.6 тайла и с задержкой в пинг. Теперь
/// скорость задаёт физ-контроллер после мувера и трения, коллизии включены: стена сбоку просто
/// «проскальзывает» мимо, а упором считается только реальная остановка тела (как у подката).
/// </summary>
public abstract partial class SharedHalberdChargeSystem : EntitySystem
{
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementModStatusSystem _movementMod = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StandingStateSystem _standing = default!;

    private static readonly EntProtoId HitSlowdownEffect = "HalberdChargeHitSlowdownStatusEffect";

    /// <summary>Разгон: за столько тайлов скорость выходит с <see cref="MinSpeedFactor"/> на полную.</summary>
    private const float RampUpDistance = 1.5f;

    /// <summary>Торможение: на столько тайлов до конца скорость плавно сбрасывается.</summary>
    private const float RampDownDistance = 2f;

    private const float MinSpeedFactor = 0.3f;

    /// <summary>Запас времени поверх идеального пробега — страховка от «вечного» рывка.</summary>
    private const float DeadlineSlack = 2.5f;

    /// <summary>Поля, которые шаг рывка меняет каждый тик, — только они уходят дельтой.</summary>
    private static readonly string[] TickDirtyFields =
    {
        nameof(HalberdChargingComponent.LastPosition),
        nameof(HalberdChargingComponent.LastSpeed),
        nameof(HalberdChargingComponent.StallTicks),
    };

    private EntityQuery<HalberdChargeComponent> _chargeQuery;
    private EntityQuery<WieldableComponent> _wieldableQuery;
    private readonly HashSet<Entity<MobStateComponent>> _candidates = new();

    public override void Initialize()
    {
        base.Initialize();

        _chargeQuery = GetEntityQuery<HalberdChargeComponent>();
        _wieldableQuery = GetEntityQuery<WieldableComponent>();

        SubscribeLocalEvent<HalberdChargeActionEvent>(OnChargeAction);

        SubscribeLocalEvent<HalberdChargeComponent, ItemWieldedEvent>(OnWielded);
        SubscribeLocalEvent<HalberdChargeComponent, ItemUnwieldedEvent>(OnUnwielded);
        SubscribeLocalEvent<HalberdChargeComponent, ComponentShutdown>(OnHalberdShutdown);

        SubscribeLocalEvent<HalberdChargingComponent, ComponentShutdown>(OnChargingShutdown);
        SubscribeLocalEvent<HalberdChargingComponent, UpdateCanMoveEvent>(OnUpdateCanMove);
        SubscribeLocalEvent<HalberdChargingComponent, BeforeDamageChangedEvent>(OnBeforeDamage);
        SubscribeLocalEvent<HalberdChargingComponent, KnockDownAttemptEvent>(OnChargingKnockDownAttempt);
        SubscribeLocalEvent<HalberdChargingComponent, AttemptMobCollideEvent>(OnMobCollide);
        SubscribeLocalEvent<HalberdChargingComponent, AttemptMobTargetCollideEvent>(OnMobTargetCollide);

        // Не ронять алебарду при стане. DropAttempt/KnockDownAttempt движок шлёт directed на ЮЗЕРА,
        // поэтому подписка на маркер на нём, а не на алебарде.
        SubscribeLocalEvent<HalberdWieldedComponent, KnockDownAttemptEvent>(OnWieldedKnockDownAttempt);
        SubscribeLocalEvent<HalberdWieldedComponent, DropAttemptEvent>(OnDropAttempt);
    }

    // ── Wield / unwield ───────────────────────────────────────

    private void OnWielded(Entity<HalberdChargeComponent> ent, ref ItemWieldedEvent args)
    {
        ent.Comp.WieldedBy = args.User;
        var wielded = EnsureComp<HalberdWieldedComponent>(args.User);
        wielded.Halberd = ent;
        Dirty(args.User, wielded);

        OnHalberdWielded(ent, args.User);
    }

    private void OnUnwielded(EntityUid uid, HalberdChargeComponent comp, ItemUnwieldedEvent args)
    {
        comp.WieldedBy = null;
        RemComp<HalberdWieldedComponent>(args.User);

        if (TryComp<HalberdChargingComponent>(args.User, out var charging) && charging.Halberd == uid)
            EndCharge((args.User, charging), HalberdChargeEndReason.Miss);

        OnHalberdUnwielded((uid, comp), args.User);
    }

    private void OnHalberdShutdown(Entity<HalberdChargeComponent> ent, ref ComponentShutdown args)
    {
        OnHalberdRemoved(ent);

        if (ent.Comp.WieldedBy is not { } user || TerminatingOrDeleted(user))
            return;

        // Алебарду удалили или уничтожили, минуя обычный unwield: без этого бегущий так и остался
        // бы с заблокированным движением, резистом и зацикленным звуком.
        if (TryComp<HalberdChargingComponent>(user, out var charging) && charging.Halberd == ent.Owner)
            EndCharge((user, charging), HalberdChargeEndReason.Aborted);

        RemComp<HalberdWieldedComponent>(user);
    }

    // ── Старт ─────────────────────────────────────────────────

    private void OnChargeAction(HalberdChargeActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        if (HasComp<HalberdChargingComponent>(user))
            return;

        if (!TryGetHeldHalberd(user, out var halberd, out var comp))
            return;

        if (!IsWieldedBy(halberd, user))
        {
            _popup.PopupClient(Loc.GetString("halberd-charge-need-wield"), user, user, PopupType.SmallCaution);
            return;
        }

        if (_standing.IsDown(user) || HasComp<KnockedDownComponent>(user))
        {
            _popup.PopupClient(Loc.GetString("halberd-charge-need-stand"), user, user, PopupType.SmallCaution);
            return;
        }

        if (!_mobState.IsAlive(user)
            || !_blocker.CanMove(user)
            || _buckle.IsBuckled(user)
            || _container.IsEntityInContainer(user))
        {
            return;
        }

        // В невесомости отталкиваться не от чего — рывок без опоры только унёс бы в космос.
        if (_gravity.IsWeightless(user))
        {
            _popup.PopupClient(Loc.GetString("halberd-charge-no-footing"), user, user, PopupType.SmallCaution);
            return;
        }

        var userPos = _transform.GetWorldPosition(user);
        var direction = _transform.ToMapCoordinates(args.Target).Position - userPos;
        direction = direction.LengthSquared() < 0.001f
            ? _transform.GetWorldRotation(user).ToWorldVec()
            : Vector2.Normalize(direction);

        var speed = Math.Max(comp.ChargeSpeed, 0.1f);
        var charging = EnsureComp<HalberdChargingComponent>(user);
        charging.Halberd = halberd;
        charging.Direction = direction;
        charging.Origin = userPos;
        charging.Distance = comp.ChargeDistance;
        charging.Speed = speed;
        charging.Resistance = comp.ChargeResistance;
        charging.LastPosition = userPos;
        charging.LastSpeed = 0f;
        charging.StallTicks = 0;
        charging.Deadline = Timing.CurTime + TimeSpan.FromSeconds(comp.ChargeDistance / speed * DeadlineSlack + 0.5f);
        Dirty(user, charging);

        // Разворачиваем лицом по рывку и сразу даём стартовую скорость — первый же кадр должен
        // выглядеть как бросок, а не как шаг.
        _transform.SetWorldRotation(user, direction.ToWorldAngle());
        _physics.SetLinearVelocity(user, direction * speed * MinSpeedFactor);
        _blocker.UpdateCanMove(user);

        OnChargeStarted(user, (halberd, comp));
        args.Handled = true;
    }

    // ── Тик (зовёт HalberdChargeController) ───────────────────

    /// <summary>
    /// Один шаг рывка: срывы, упор в стену, цель на пути и скорость по профилю. Зовётся из
    /// физ-контроллера после мувера и трения, чтобы наша скорость была последней.
    /// </summary>
    public void UpdateCharge(Entity<HalberdChargingComponent, PhysicsComponent, TransformComponent> ent, float frameTime)
    {
        var (uid, charging, physics, xform) = ent;

        if (!_mobState.IsAlive(uid)
            || _container.IsEntityInContainer(uid)
            || _gravity.IsWeightless(uid))
        {
            EndCharge((uid, charging), HalberdChargeEndReason.Aborted);
            return;
        }

        if (!_chargeQuery.TryComp(charging.Halberd, out var halberd) || !IsWieldedBy(charging.Halberd, uid))
        {
            EndCharge((uid, charging), HalberdChargeEndReason.Miss);
            return;
        }

        var position = _transform.GetWorldPosition(xform);

        // Упор: тело сдвинулось заметно меньше заданного — значит, его держит препятствие. Два
        // тика подряд, чтобы одиночный тычок о угол на скорости не считался ударом.
        if (charging.LastSpeed > 0f)
        {
            var moved = (position - charging.LastPosition).Length();
            var expected = charging.LastSpeed * frameTime;
            charging.StallTicks = moved < expected * halberd.StallFraction ? charging.StallTicks + 1 : 0;

            if (charging.StallTicks >= 2)
            {
                EndCharge((uid, charging), HalberdChargeEndReason.Wall);
                return;
            }
        }

        charging.LastPosition = position;

        if (FindTarget(uid, charging, halberd, xform, position) is { } target)
        {
            EndCharge((uid, charging), HalberdChargeEndReason.HitEntity, target);
            return;
        }

        var traveled = Vector2.Dot(position - charging.Origin, charging.Direction);
        if (traveled >= charging.Distance || Timing.CurTime >= charging.Deadline)
        {
            EndCharge((uid, charging), HalberdChargeEndReason.Miss);
            return;
        }

        var speed = charging.Speed * GetSpeedFactor(traveled, charging.Distance);
        _physics.SetLinearVelocity(uid, charging.Direction * speed, body: physics);
        charging.LastSpeed = speed;
        DirtyFields(uid, charging, null, TickDirtyFields);
    }

    /// <summary>Плавный разгон и торможение — smoothstep с обоих краёв, без ступенек в скорости.</summary>
    private static float GetSpeedFactor(float traveled, float distance)
    {
        var up = SmoothStep(Math.Clamp(traveled / RampUpDistance, 0f, 1f));
        var down = SmoothStep(Math.Clamp((distance - traveled) / RampDownDistance, 0f, 1f));
        return MinSpeedFactor + (1f - MinSpeedFactor) * Math.Min(up, down);
    }

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);

    /// <summary>
    /// Живой (или в крите) моб прямо по ходу. Сзади и сбоку стоящих не цепляем — иначе рывок
    /// «бил» бы того, рядом с кем стартовал. Из нескольких — с меньшим NetEntity, чтобы клиент и
    /// сервер выбрали одного и того же.
    /// </summary>
    private EntityUid? FindTarget(EntityUid uid, HalberdChargingComponent charging, HalberdChargeComponent halberd,
        TransformComponent xform, Vector2 position)
    {
        _candidates.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, halberd.HitRadius, _candidates);

        EntityUid? best = null;
        var bestId = int.MaxValue;

        foreach (var candidate in _candidates)
        {
            var target = candidate.Owner;
            if (target == uid || _mobState.IsDead(target, candidate.Comp) || _container.IsEntityInContainer(target))
                continue;

            var offset = _transform.GetWorldPosition(target) - position;
            if (Vector2.Dot(offset, charging.Direction) < -0.1f)
                continue;

            var id = GetNetEntity(target).Id;
            if (id >= bestId)
                continue;

            best = target;
            bestId = id;
        }

        return best;
    }

    // ── Финал ─────────────────────────────────────────────────

    private void EndCharge(Entity<HalberdChargingComponent> ent, HalberdChargeEndReason reason, EntityUid? target = null)
    {
        var (uid, charging) = ent;
        var halberdUid = charging.Halberd;
        _chargeQuery.TryComp(halberdUid, out var halberd);

        // Компонент снимаем первым: нокдаун ниже иначе попал бы в наш же KnockDownAttempt и
        // посчитался бы «сбили посреди рывка».
        RemComp<HalberdChargingComponent>(uid);

        if (TryComp<PhysicsComponent>(uid, out var physics) && reason != HalberdChargeEndReason.KnockedDown)
            _physics.SetLinearVelocity(uid, Vector2.Zero, body: physics);

        if (halberd != null)
        {
            switch (reason)
            {
                case HalberdChargeEndReason.HitEntity:
                    _audio.PlayPredicted(halberd.HitSound, uid, uid);
                    _movementMod.TryAddMovementSpeedModDuration(uid, HitSlowdownEffect,
                        TimeSpan.FromSeconds(halberd.HitSlowdownDuration), halberd.HitSlowdownSpeedModifier);
                    break;

                case HalberdChargeEndReason.Wall:
                    _audio.PlayPredicted(halberd.WallSound, uid, uid);
                    _stun.TryKnockdown(uid, TimeSpan.FromSeconds(halberd.KnockdownOnHitWall), true);
                    break;

                case HalberdChargeEndReason.Miss:
                    _stun.TryKnockdown(uid, TimeSpan.FromSeconds(halberd.KnockdownOnMiss), true);
                    break;
            }
        }

        OnChargeEnded(uid, halberdUid, halberd, reason, target);
    }

    // ── Подписки ──────────────────────────────────────────────

    private void OnChargingShutdown(Entity<HalberdChargingComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent))
            _blocker.UpdateCanMove(ent);
    }

    private void OnUpdateCanMove(Entity<HalberdChargingComponent> ent, ref UpdateCanMoveEvent args)
    {
        // При снятии компонента он ещё жив в момент пересчёта — не держим блок.
        if (ent.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        args.Cancel();
    }

    private void OnBeforeDamage(Entity<HalberdChargingComponent> ent, ref BeforeDamageChangedEvent args)
    {
        args.Damage *= (FixedPoint2) (1f - ent.Comp.Resistance);
    }

    private void OnChargingKnockDownAttempt(Entity<HalberdChargingComponent> ent, ref KnockDownAttemptEvent args)
    {
        // Лёг, поскользнулся или сбили посреди рывка: алебарду не роняем, рывок обрывается без
        // удара о стену — падение уже обеспечило исходное событие.
        args.Drop = false;
        EndCharge(ent, HalberdChargeEndReason.KnockedDown);
    }

    private void OnMobCollide(Entity<HalberdChargingComponent> ent, ref AttemptMobCollideEvent args)
    {
        // Мягкое расталкивание мобов на рывке только дёргало бы траекторию — цель решает FindTarget.
        args.Cancelled = true;
    }

    private void OnMobTargetCollide(Entity<HalberdChargingComponent> ent, ref AttemptMobTargetCollideEvent args)
    {
        args.Cancelled = true;
    }

    private void OnWieldedKnockDownAttempt(Entity<HalberdWieldedComponent> ent, ref KnockDownAttemptEvent args)
    {
        args.Drop = false;
    }

    private void OnDropAttempt(EntityUid uid, HalberdWieldedComponent comp, DropAttemptEvent args)
    {
        // Алебарда не выпадает никогда, кроме дизарма (он роняет её сам, в обход этой проверки).
        args.Cancel();
    }

    // ── Хелперы ───────────────────────────────────────────────

    private bool TryGetHeldHalberd(EntityUid user, out EntityUid halberd, out HalberdChargeComponent comp)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (!_chargeQuery.TryComp(held, out var found))
                continue;

            halberd = held;
            comp = found;
            return true;
        }

        halberd = default;
        comp = default!;
        return false;
    }

    private bool IsWieldedBy(EntityUid halberd, EntityUid user)
    {
        if (!_hands.IsHolding(user, halberd, out _))
            return false;

        return !_wieldableQuery.TryComp(halberd, out var wieldable) || wieldable.Wielded;
    }

    // ── Серверные хуки ────────────────────────────────────────

    protected virtual void OnHalberdWielded(Entity<HalberdChargeComponent> halberd, EntityUid user)
    {
    }

    protected virtual void OnHalberdUnwielded(Entity<HalberdChargeComponent> halberd, EntityUid user)
    {
    }

    protected virtual void OnHalberdRemoved(Entity<HalberdChargeComponent> halberd)
    {
    }

    /// <summary>Рывок начался (сервер — боевой клич и зацикленный звук).</summary>
    protected virtual void OnChargeStarted(EntityUid user, Entity<HalberdChargeComponent> halberd)
    {
    }

    /// <summary>
    /// Рывок закончился; движение и нокдаун уже применены (они предсказываются). Сервер — урон цели,
    /// реплики, остановка звука, перезапуск кулдауна.
    /// </summary>
    protected virtual void OnChargeEnded(EntityUid user, EntityUid halberdUid, HalberdChargeComponent? halberd,
        HalberdChargeEndReason reason, EntityUid? target)
    {
    }
}
