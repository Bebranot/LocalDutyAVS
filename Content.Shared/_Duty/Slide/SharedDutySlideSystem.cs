// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared._Duty.Movement;
using Content.Shared.ActionBlocker;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.CCVar;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Gravity;
using Content.Shared.Input;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Pulling.Events;
using Content.Shared.Random.Helpers;
using Content.Shared.Slippery;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Input.Binding;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Slide;

/// <summary>
/// _Duty: подкат «Мастера подкатов» (<see cref="DutySlideMasterComponent"/>).
///
/// Здесь всё, что должно предсказываться у самого игрока: решение по R, старт, фазы, торможение
/// (через <see cref="DutySlideController"/>), прерывания, нокдаун от стены и сам факт тарана
/// (бросок детерминированный). Урон, стан цели, чат и попапы — на сервере через виртуальные хуки.
///
/// Перехват R устроен неочевидно. Клиентский InputSystem не отправляет клавишу на сервер, если
/// какой-то локальный хендлер её поглотил. Поэтому клиент, решив делать подкат, поглощает R и шлёт
/// <see cref="DutySlideRequestEvent"/> предсказуемым событием, а сервер отвечает и на запрос, и на
/// «сырую» R (если клиент решил, что подката нет, а сервер видит иначе).
/// </summary>
public abstract partial class SharedDutySlideSystem : EntitySystem
{
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private DutySprintSystem _sprint = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StandingStateSystem _standing = default!;

    private EntityQuery<DutySlideMasterComponent> _masterQuery;
    private EntityQuery<DutySlidingComponent> _slidingQuery;
    private EntityQuery<DutyStaminaComponent> _staminaQuery;
    private EntityQuery<StaminaComponent> _combatStaminaQuery;
    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<InputMoverComponent> _moverQuery;
    private EntityQuery<KnockedDownComponent> _knockedQuery;
    private EntityQuery<MobStateComponent> _mobStateQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<PullableComponent> _pullableQuery;
    private EntityQuery<PullerComponent> _pullerQuery;
    private EntityQuery<SleepingComponent> _sleepingQuery;
    private EntityQuery<SlipperyComponent> _slipperyQuery;
    private EntityQuery<StunnedComponent> _stunnedQuery;

    /// <summary>Поля, которые шаг скольжения меняет каждый тик, — только они уходят дельтой.</summary>
    private static readonly string[] TickDirtyFields =
    {
        nameof(DutySlidingComponent.LastPosition),
        nameof(DutySlidingComponent.LastSpeed),
        nameof(DutySlidingComponent.StallTicks),
    };

    private readonly HashSet<EntityUid> _contacts = new();
    private readonly HashSet<Entity<MobStateComponent>> _candidates = new();

    public override void Initialize()
    {
        base.Initialize();

        _masterQuery = GetEntityQuery<DutySlideMasterComponent>();
        _slidingQuery = GetEntityQuery<DutySlidingComponent>();
        _staminaQuery = GetEntityQuery<DutyStaminaComponent>();
        _combatStaminaQuery = GetEntityQuery<StaminaComponent>();
        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _moverQuery = GetEntityQuery<InputMoverComponent>();
        _knockedQuery = GetEntityQuery<KnockedDownComponent>();
        _mobStateQuery = GetEntityQuery<MobStateComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _pullableQuery = GetEntityQuery<PullableComponent>();
        _pullerQuery = GetEntityQuery<PullerComponent>();
        _sleepingQuery = GetEntityQuery<SleepingComponent>();
        _slipperyQuery = GetEntityQuery<SlipperyComponent>();
        _stunnedQuery = GetEntityQuery<StunnedComponent>();

        SubscribeLocalEvent<DutySlidingComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<DutySlidingComponent, UpdateCanMoveEvent>(OnUpdateCanMove);
        SubscribeLocalEvent<DutySlidingComponent, SlipAttemptEvent>(OnSlipAttempt);
        SubscribeLocalEvent<DutySlidingComponent, AttemptMobCollideEvent>(OnMobCollide);
        SubscribeLocalEvent<DutySlidingComponent, AttemptMobTargetCollideEvent>(OnMobTargetCollide);
        SubscribeLocalEvent<DutySlidingComponent, BuckleAttemptEvent>(OnBuckleAttempt);
        SubscribeLocalEvent<DutySlidingComponent, BeingPulledAttemptEvent>(OnBeingPulledAttempt);
        SubscribeLocalEvent<DutySlidingComponent, StartCollideEvent>(OnStartCollide);

        SubscribeAllEvent<DutySlideRequestEvent>(OnSlideRequest);

        // Раньше ванильного хендлера SharedStunSystem: вернув true, мы не даём R уронить персонажа
        // обычным способом.
        CommandBinds.Builder
            .BindBefore(ContentKeyFunctions.ToggleKnockdown,
                new PointerInputCmdHandler(OnSlideInput, ignoreUp: true, outsidePrediction: true),
                typeof(SharedStunSystem))
            .Register<SharedDutySlideSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<SharedDutySlideSystem>();
    }

    // ── Ввод ──────────────────────────────────────────────────────────────────

    private bool OnSlideInput(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        return HandleSlideInput(args.Session);
    }

    /// <summary>
    /// Решение по нажатию R. Вернуть true — R поглощена, ванильное падение не сработает.
    /// Базовая версия — серверная: здесь R уже доехала до сервера.
    /// </summary>
    protected virtual bool HandleSlideInput(ICommonSession? session)
    {
        if (session?.AttachedEntity is not { Valid: true } uid || !Exists(uid))
            return false;

        // Пока идёт подкат (включая оглушение после тарана и подъём), R ничего не делает: иначе
        // ванильный хендлер навесил бы KnockedDown посреди скольжения.
        if (_slidingQuery.HasComp(uid))
            return true;

        switch (CheckSlide(uid, out var master))
        {
            case DutySlideCheck.Ready:
                return TryStartSlide(uid, master!);
            case DutySlideCheck.Cooldown:
                NotifyCooldown(uid, master!);
                return false;
            default:
                return false;
        }
    }

    private void OnSlideRequest(DutySlideRequestEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } uid || !Exists(uid))
            return;

        if (_slidingQuery.HasComp(uid))
            return;

        switch (CheckSlide(uid, out var master))
        {
            case DutySlideCheck.Ready:
                if (TryStartSlide(uid, master!))
                    return;
                break;
            case DutySlideCheck.Cooldown:
                NotifyCooldown(uid, master!);
                break;
        }

        // Клиент поглотил R, думая, что подкат будет, а условия не сошлись. Игрок всё же нажал
        // «лечь» — делаем то же, что сделал бы ванильный ToggleKnockdown для стоящего.
        VanillaKnockdown(uid);
    }

    private void VanillaKnockdown(EntityUid uid)
    {
        if (!_cfg.GetCVar(CCVars.MovementCrawling)
            || !TryComp<CrawlerComponent>(uid, out var crawler)
            || _knockedQuery.HasComp(uid)
            || _standing.IsDown(uid))
        {
            return;
        }

        _stun.TryKnockdown((uid, crawler), crawler.DefaultKnockedDuration, true, false, false);
    }

    private void NotifyCooldown(EntityUid uid, DutySlideMasterComponent master)
    {
        var left = master.NextSlideAllowed - Timing.CurTime;
        OnCooldown(uid, Math.Max(1, (int) Math.Ceiling(left.TotalSeconds)));
    }

    // ── Условия и старт ───────────────────────────────────────────────────────

    public bool IsSliding(EntityUid uid)
    {
        return _slidingQuery.HasComp(uid);
    }

    /// <summary>
    /// Можно ли начать подкат прямо сейчас. <see cref="DutySlideCheck.Cooldown"/> — всё
    /// позволяет, кроме перезарядки: только в этом случае игроку пишем, почему подката нет.
    /// </summary>
    public DutySlideCheck CheckSlide(EntityUid uid, out DutySlideMasterComponent? master)
    {
        if (!_masterQuery.TryComp(uid, out master))
            return DutySlideCheck.Blocked;

        if (!_staminaQuery.TryComp(uid, out var stamina) || !_sprint.IsSprinting(uid, stamina))
            return DutySlideCheck.Blocked;

        if (_standing.IsDown(uid) || !_mobState.IsAlive(uid))
            return DutySlideCheck.Blocked;

        if (_stunnedQuery.HasComp(uid) || _sleepingQuery.HasComp(uid))
            return DutySlideCheck.Blocked;

        // Наручники подкат не запрещают — как и спринт.
        if (_buckle.IsBuckled(uid) || _container.IsEntityInContainer(uid) || _gravity.IsWeightless(uid))
            return DutySlideCheck.Blocked;

        // Граб ADT — надстройка над pull, так что проверки pull закрывают и его.
        if (_pullerQuery.TryComp(uid, out var puller) && puller.Pulling != null)
            return DutySlideCheck.Blocked;

        if (_pullableQuery.TryComp(uid, out var pullable) && pullable.BeingPulled)
            return DutySlideCheck.Blocked;

        return Timing.CurTime < master.NextSlideAllowed
            ? DutySlideCheck.Cooldown
            : DutySlideCheck.Ready;
    }

    private bool TryStartSlide(EntityUid uid, DutySlideMasterComponent master)
    {
        if (!_standing.Down(uid, playSound: true, dropHeldItems: false))
            return false;

        var velocity = _physicsQuery.TryComp(uid, out var physics) ? physics.LinearVelocity : Vector2.Zero;
        var speed = velocity.Length();

        Vector2 direction;
        if (speed > 0.1f)
            direction = velocity / speed;
        else if (_moverQuery.TryComp(uid, out var mover) && mover.WishDir.LengthSquared() > 0.0001f)
            direction = Vector2.Normalize(mover.WishDir);
        else
            direction = _transform.GetWorldRotation(uid).ToWorldVec();

        var startSpeed = Math.Clamp(speed * master.SpeedMultiplier, master.MinStartSpeed, master.MaxStartSpeed);

        var comp = EnsureComp<DutySlidingComponent>(uid);
        comp.Phase = DutySlidePhase.Sliding;
        comp.Direction = direction;
        comp.StartSpeed = startSpeed;
        comp.ProfileSpeed = startSpeed;
        comp.ProfileStart = Timing.CurTime;
        comp.ProfileDuration = master.SlideDuration;
        comp.LastPosition = _transform.GetWorldPosition(uid);
        comp.LastSpeed = 0f;
        comp.StallTicks = 0;
        comp.SlipperyApplied = false;
        comp.Seed = (int) Timing.CurTick.Value;
        comp.StartTime = Timing.CurTime;
        comp.PhaseEnd = TimeSpan.Zero;

        // Рывок задаём сразу, не дожидаясь следующего тика контроллера: первый же кадр подката
        // должен быть быстрым, иначе старт выглядит как обычное падение.
        _physics.SetLinearVelocity(uid, direction * startSpeed);

        // Уже стоим в луже — новый контакт с ней не случится, поэтому проверяем сразу.
        if (IsTouchingSlippery(uid))
            ApplySlippery((uid, comp), master);

        Dirty(uid, comp);

        master.NextSlideAllowed = Timing.CurTime + master.Cooldown;
        Dirty(uid, master);

        _blocker.UpdateCanMove(uid);
        _movementSpeed.RefreshMovementSpeedModifiers(uid);

        OnSlideStarted(uid);
        return true;
    }

    private bool IsTouchingSlippery(EntityUid uid)
    {
        _contacts.Clear();
        _physics.GetContactingEntities(uid, _contacts);

        foreach (var other in _contacts)
        {
            if (_slipperyQuery.HasComp(other))
                return true;
        }

        return false;
    }

    // ── Профиль скорости ──────────────────────────────────────────────────────

    /// <summary>Скорость по профилю v = v0·(1 − t/T)^p на момент <paramref name="now"/>.</summary>
    private static float GetProfileSpeed(DutySlidingComponent comp, TimeSpan now, float exponent)
    {
        var duration = (float) comp.ProfileDuration.TotalSeconds;
        if (duration <= 0f)
            return 0f;

        var tau = Math.Clamp((float) (now - comp.ProfileStart).TotalSeconds / duration, 0f, 1f);
        return comp.ProfileSpeed * MathF.Pow(1f - tau, exponent);
    }

    /// <summary>Сколько профиль провёз с начала текущего отрезка (интеграл скорости) — по времени, а не по факту.</summary>
    private static float GetProfileDistance(DutySlidingComponent comp, TimeSpan now, float exponent)
    {
        var duration = (float) comp.ProfileDuration.TotalSeconds;
        if (duration <= 0f)
            return 0f;

        var tau = Math.Clamp((float) (now - comp.ProfileStart).TotalSeconds / duration, 0f, 1f);
        return comp.ProfileSpeed * duration / (exponent + 1f) * (1f - MathF.Pow(1f - tau, exponent + 1f));
    }

    /// <summary>
    /// Слизь/лужа под скользящим: торможение перезапускается от текущей скорости и растягивается
    /// так, чтобы от точки старта вышло около <see cref="DutySlideMasterComponent.SlipperyDistance"/>.
    /// Один раз за подкат. Считаем только от времени, чтобы клиент и сервер растянули одинаково.
    /// </summary>
    private void ApplySlippery(Entity<DutySlidingComponent> ent, DutySlideMasterComponent master)
    {
        var comp = ent.Comp;
        if (comp.SlipperyApplied)
            return;

        comp.SlipperyApplied = true;

        var now = Timing.CurTime;
        var speed = GetProfileSpeed(comp, now, master.SlideExponent);
        var remaining = master.SlipperyDistance - GetProfileDistance(comp, now, master.SlideExponent);

        if (speed > master.EndSpeed && remaining > 0f)
        {
            var duration = (master.SlideExponent + 1f) * remaining / speed;
            var left = (master.Timeout - (now - comp.StartTime)).TotalSeconds;
            comp.ProfileSpeed = speed;
            comp.ProfileStart = now;
            comp.ProfileDuration = TimeSpan.FromSeconds(Math.Min(duration, Math.Max(left, 0)));
        }

        Dirty(ent);
    }

    // ── Тик (зовёт DutySlideController) ───────────────────────────────────────

    /// <summary>
    /// Один шаг подката: прерывания, смена фаз, таран и скорость по профилю. Вызывается из
    /// физ-контроллера после мувера и трения, чтобы наша скорость была последней.
    /// </summary>
    public void UpdateSlide(Entity<DutySlidingComponent, PhysicsComponent, TransformComponent> ent, float frameTime)
    {
        var (uid, comp, physics, xform) = ent;

        if (TryInterrupt(uid, comp))
            return;

        if (!_masterQuery.TryComp(uid, out var master))
        {
            // Трейт сняли посреди подката — просто встаём.
            RemComp<DutySlidingComponent>(uid);
            return;
        }

        var now = Timing.CurTime;

        switch (comp.Phase)
        {
            case DutySlidePhase.TackleStun:
                if (now >= comp.PhaseEnd && !_stunnedQuery.HasComp(uid))
                    BeginStandUp((uid, comp), master);
                return;

            case DutySlidePhase.StandingUp:
                // Сам подъём (или нокдаун, если встать негде) — в OnShutdown.
                if (now >= comp.PhaseEnd)
                    RemComp<DutySlidingComponent>(uid);
                return;
        }

        var position = _transform.GetWorldPosition(xform);
        var moved = (position - comp.LastPosition).Length();
        comp.LastPosition = position;

        if (comp.LastSpeed > 0f)
        {
            var expected = comp.LastSpeed * frameTime;
            comp.StallTicks = moved < expected * master.StallFraction ? comp.StallTicks + 1 : 0;

            if (comp.StallTicks >= 2)
            {
                HitObstacle((uid, comp), comp.LastSpeed);
                return;
            }
        }

        if (TryFindTackleTarget(uid, comp, master, xform) is { } target)
        {
            BeginTackleStun((uid, comp), master.TackleSelfStun);
            OnTackle(uid, master, target);
            return;
        }

        var speed = GetProfileSpeed(comp, now, master.SlideExponent);
        if (speed < master.EndSpeed || now - comp.StartTime >= master.Timeout)
        {
            BeginStandUp((uid, comp), master);
            return;
        }

        _physics.SetLinearVelocity(uid, comp.Direction * speed, body: physics);
        comp.LastSpeed = speed;
        DirtyFields(uid, comp, null, TickDirtyFields);
    }

    // ── Таран ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Моб, которого подкат сбивает в этот тик, или null. Бросок детерминированный: seed из тика
    /// старта и обеих сущностей, так что у клиента и сервера исход одинаковый, а повторная проверка
    /// того же моба в следующих тиках даёт тот же результат — «один бросок на моба за подкат»
    /// выходит сам собой. Если сбиты сразу несколько — берём с меньшим NetEntity, чтобы клиент и
    /// сервер выбрали одного и того же.
    /// </summary>
    private EntityUid? TryFindTackleTarget(EntityUid uid, DutySlidingComponent comp, DutySlideMasterComponent master, TransformComponent xform)
    {
        _candidates.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, master.TackleRadius, _candidates);

        EntityUid? best = null;
        var bestId = int.MaxValue;
        float? chance = null;
        var ourId = GetNetEntity(uid).Id;

        foreach (var candidate in _candidates)
        {
            var target = candidate.Owner;
            if (target == uid
                || candidate.Comp.CurrentState != MobState.Alive
                || _slidingQuery.HasComp(target)
                || _standing.IsDown(target)
                || _container.IsEntityInContainer(target))
            {
                continue;
            }

            var targetId = GetNetEntity(target).Id;
            if (targetId >= bestId)
                continue;

            chance ??= GetTackleChance(uid, master);
            var rand = new System.Random(SharedRandomExtensions.HashCodeCombine(new[] { comp.Seed, ourId, targetId }));
            if (rand.NextDouble() >= chance.Value)
                continue;

            best = target;
            bestId = targetId;
        }

        return best;
    }

    /// <summary>
    /// Шанс тарана зависит от формы подкатывающего: 5% у еле живого и выдохшегося, 15% у здорового
    /// и свежего. Стамина — боевая (ванильная), а не наш пул спринта.
    /// </summary>
    private float GetTackleChance(EntityUid uid, DutySlideMasterComponent master)
    {
        var hpFrac = 1f;
        // До первого выхода из строя (софт-крит, если есть), а не до крита.
        if (TryComp<MobThresholdsComponent>(uid, out var thresholds)
            && _mobThreshold.TryGetIncapThreshold(uid, out var threshold, thresholds)
            && threshold > 0)
            hpFrac = 1f - (_damageable.GetTotalDamage(uid) / threshold.Value).Float();

        var stamFrac = 1f;
        if (_combatStaminaQuery.TryComp(uid, out var stamina) && stamina.CritThreshold > 0f)
            stamFrac = 1f - stamina.StaminaDamage / stamina.CritThreshold;

        var form = (Math.Clamp(hpFrac, 0f, 1f) + Math.Clamp(stamFrac, 0f, 1f)) / 2f;
        return Math.Clamp(master.TackleChanceBase + master.TackleChanceBonus * form, 0f, 1f);
    }

    /// <summary>
    /// Обычные правила важнее подката: внешний стан/нокдаун/сон/крит обрывают его, а персонаж
    /// остаётся в стандартном состоянии ADT. Возвращает true, если подкат снят.
    /// </summary>
    private bool TryInterrupt(EntityUid uid, DutySlidingComponent comp)
    {
        // Крит/смерть: лежанием дальше управляет MobStateSystem, OnShutdown поднимать не станет.
        // Чужой нокдаун: уже есть KnockedDown со своим механизмом подъёма.
        if (!_mobState.IsAlive(uid) || _knockedQuery.HasComp(uid))
        {
            RemComp<DutySlidingComponent>(uid);
            return true;
        }

        // Стан, наложенный тараном, — наш собственный, он прерыванием не считается.
        if (_sleepingQuery.HasComp(uid)
            || comp.Phase != DutySlidePhase.TackleStun && _stunnedQuery.HasComp(uid))
        {
            // Без KnockedDown персонаж повис бы лёжа без способа встать.
            FallbackKnockdown(uid);
            RemComp<DutySlidingComponent>(uid);
            return true;
        }

        // Невесомость (ванилла там встаёт мгновенно) или попал в контейнер — встаём без штрафов.
        if (_gravity.IsWeightless(uid) || _container.IsEntityInContainer(uid))
        {
            RemComp<DutySlidingComponent>(uid);
            return true;
        }

        return false;
    }

    // ── Переходы ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Жёсткий контакт или застревание. На скорости — удар с обычным нокдауном ADT, на излёте —
    /// мягкая остановка и быстрый подъём.
    /// </summary>
    public void HitObstacle(Entity<DutySlidingComponent> ent, float speed)
    {
        if (ent.Comp.Phase != DutySlidePhase.Sliding || !_masterQuery.TryComp(ent, out var master))
            return;

        if (speed < ent.Comp.StartSpeed * master.ImpactSpeedFraction)
        {
            BeginStandUp(ent, master);
            return;
        }

        // Урон — раньше нокдауна: урон по лежачему краулеру продлил бы нокдаун до дефолтного.
        OnWallImpact(ent, master);

        // Нокдаун — до снятия компонента, иначе OnShutdown успел бы поднять персонажа, а нокдаун
        // тут же уронил бы обратно (со звуком падения).
        _physics.SetLinearVelocity(ent, Vector2.Zero);
        _stun.TryKnockdown(ent.Owner, master.WallKnockdown, drop: false);
        RemComp<DutySlidingComponent>(ent);
    }

    public void BeginStandUp(Entity<DutySlidingComponent> ent, DutySlideMasterComponent master)
    {
        ent.Comp.Phase = DutySlidePhase.StandingUp;
        ent.Comp.PhaseEnd = Timing.CurTime + master.StandUpDelay;
        ent.Comp.LastSpeed = 0f;
        _physics.SetLinearVelocity(ent, Vector2.Zero);
        Dirty(ent);
    }

    /// <summary>Сбил моба: подкат оборван, лежим оглушённые, потом обычный быстрый подъём.</summary>
    private void BeginTackleStun(Entity<DutySlidingComponent> ent, TimeSpan duration)
    {
        ent.Comp.Phase = DutySlidePhase.TackleStun;
        ent.Comp.PhaseEnd = Timing.CurTime + duration;
        ent.Comp.LastSpeed = 0f;
        _physics.SetLinearVelocity(ent, Vector2.Zero);
        Dirty(ent);
    }

    private void FallbackKnockdown(EntityUid uid)
    {
        if (_knockedQuery.HasComp(uid))
            return;

        if (!_stun.TryCrawling(uid, drop: false))
            _standing.Stand(uid, force: true);
    }

    // ── Подписки ──────────────────────────────────────────────────────────────

    private void OnShutdown(Entity<DutySlidingComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        _blocker.UpdateCanMove(ent);
        _movementSpeed.RefreshMovementSpeedModifiers(ent);

        // Снятие пришло состоянием с сервера — стойку сервер пришлёт сам.
        if (Timing.ApplyingState)
            return;

        if (!_mobState.IsAlive(ent) || !_standing.IsDown(ent.Owner) || _knockedQuery.HasComp(ent))
            return;

        // Под столом/завесой встать некуда: вместо застревания внутри — обычный нокдаун ADT,
        // выползет и встанет по R.
        if (!HasRoomToStand(ent) || !_standing.Stand(ent))
            FallbackKnockdown(ent);
    }

    private void OnUpdateCanMove(EntityUid uid, DutySlidingComponent comp, UpdateCanMoveEvent args)
    {
        // При снятии компонента он ещё жив в момент пересчёта — не держим блок.
        if (comp.LifeStage > ComponentLifeStage.Running)
            return;

        args.Cancel();
    }

    private void OnSlipAttempt(EntityUid uid, DutySlidingComponent comp, SlipAttemptEvent args)
    {
        // SlipperySystem щадит только KnockedDown, а его у скользящего нет.
        args.NoSlip = true;
    }

    private void OnMobCollide(Entity<DutySlidingComponent> ent, ref AttemptMobCollideEvent args)
    {
        args.Cancelled = true;
    }

    private void OnMobTargetCollide(Entity<DutySlidingComponent> ent, ref AttemptMobTargetCollideEvent args)
    {
        args.Cancelled = true;
    }

    private void OnBuckleAttempt(Entity<DutySlidingComponent> ent, ref BuckleAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnBeingPulledAttempt(EntityUid uid, DutySlidingComponent comp, BeingPulledAttemptEvent args)
    {
        // Граб ADT начинается только из pull, так что этим закрыт и он.
        args.Cancel();
    }

    private void OnStartCollide(Entity<DutySlidingComponent> ent, ref StartCollideEvent args)
    {
        if (ent.Comp.Phase != DutySlidePhase.Sliding)
            return;

        if (_slipperyQuery.HasComp(args.OtherEntity))
        {
            if (_masterQuery.TryComp(ent, out var master))
                ApplySlippery(ent, master);
            return;
        }

        if (!args.OurFixture.Hard || !args.OtherFixture.Hard)
            return;

        // Мобы — не препятствие: под ними проезжаем, а таран решает сервер.
        if (_mobStateQuery.HasComp(args.OtherEntity))
            return;

        HitObstacle(ent, args.OurBody.LinearVelocity.Length());
    }

    // ── Хелперы ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Есть ли место встать. Та же проверка, что у ванильного подъёма из нокдауна (там она
    /// приватная): жёсткие фикстуры, которые мешают стоящему, перекрывают нас больше чем на 10%.
    /// </summary>
    private bool HasRoomToStand(EntityUid uid)
    {
        var intersecting = _physics.GetEntitiesIntersectingBody(uid, StandingStateSystem.StandingCollisionLayer, false);
        if (intersecting.Count == 0)
            return true;

        var xform = Transform(uid);
        var ourAabb = _lookup.GetAABBNoContainer(uid, xform.LocalPosition, xform.LocalRotation);

        foreach (var other in intersecting)
        {
            if (!_fixturesQuery.TryComp(other, out var fixtures) || !TryComp(other, out TransformComponent? otherXform))
                continue;

            var otherTransform = new Robust.Shared.Physics.Transform(otherXform.LocalPosition, otherXform.LocalRotation);

            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard
                    || (fixture.CollisionMask & StandingStateSystem.StandingCollisionLayer) != StandingStateSystem.StandingCollisionLayer)
                {
                    continue;
                }

                for (var i = 0; i < fixture.Shape.ChildCount; i++)
                {
                    if (fixture.Shape.ComputeAABB(otherTransform, i).IntersectPercentage(ourAabb) > 0.1f)
                        return false;
                }
            }
        }

        return true;
    }

    // ── Серверные хуки ────────────────────────────────────────────────────────

    /// <summary>Подкат начался (сервер — попап окружающим).</summary>
    protected virtual void OnSlideStarted(EntityUid uid)
    {
    }

    /// <summary>Удар о препятствие на скорости (сервер — урон и попапы). Вызывается до нокдауна.</summary>
    protected virtual void OnWallImpact(EntityUid uid, DutySlideMasterComponent master)
    {
    }

    /// <summary>
    /// Подкат сбил моба. Оглушение подкатывающего уже выставлено (оно предсказывается);
    /// сервер — урон, стан, падение цели, попапы, лог.
    /// </summary>
    protected virtual void OnTackle(EntityUid uid, DutySlideMasterComponent master, EntityUid target)
    {
    }

    /// <summary>R на перезарядке при выполненных остальных условиях (сервер — серое сообщение в чат).</summary>
    protected virtual void OnCooldown(EntityUid uid, int seconds)
    {
    }
}

public enum DutySlideCheck : byte
{
    /// <summary>Подкат невозможен — R работает как обычно, без сообщений.</summary>
    Blocked,

    /// <summary>Всё позволяет, кроме перезарядки.</summary>
    Cooldown,

    Ready,
}
