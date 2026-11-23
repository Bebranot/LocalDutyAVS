// SPDX-FileCopyrightText: 2026 LocalDuty
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.ADT.Sandevistan;
using Content.Server.GameTicking;
using Content.Shared._Duty.Sandevistan;
using Content.Shared.ADT.Sandevistan;
using Content.Shared.DoAfter;
using Content.Shared.GameTicking;
using Content.Shared.Ghost.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Duty.Sandevistan;

/// <summary>
/// _Duty: серверная система картово-изолированной дилатации времени вокруг активных пользователей Sandevistan.
/// Работает только при <see cref="GameRunLevel.InRound"/>, затрагивает только тот же <see cref="MapId"/>,
/// исключает наблюдателей (<see cref="GhostComponent"/>) и активных пользователей Sandevistan.
/// </summary>
public sealed class DutySandevistanTimeDilationSystem : EntitySystem
{
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    private readonly Dictionary<MapId, float> _activeMaps = new();
    private readonly List<EntityUid> _toRemoveDilated = new();
    private readonly List<EntityUid> _toRestoreProjectiles = new();

    // Полный проход по мобам/оружию/DoAfter — тысячи сущностей, поэтому не каждый тик: события старта/конца
    // активности и смены карты и так зовут ReconcileMapDilation сразу, а таймер лишь подхватывает новые
    // сущности (заспавненные, вошедшие на карту, выброшенные из рук). Снаряды быстрые — их обрабатываем каждый тик.
    private const float FullReconcileInterval = 0.5f;
    private float _reconcileAccumulator;

    private EntityQuery<ActiveSandevistanUserComponent> _activeQuery;
    private EntityQuery<SandevistanTimeDilatedComponent> _dilatedQuery;
    private EntityQuery<SandevistanSlowedProjectileComponent> _slowedQuery;
    private EntityQuery<GhostComponent> _ghostQuery;

    public override void Initialize()
    {
        base.Initialize();

        _activeQuery = GetEntityQuery<ActiveSandevistanUserComponent>();
        _dilatedQuery = GetEntityQuery<SandevistanTimeDilatedComponent>();
        _slowedQuery = GetEntityQuery<SandevistanSlowedProjectileComponent>();
        _ghostQuery = GetEntityQuery<GhostComponent>();

        SubscribeLocalEvent<ActiveSandevistanUserComponent, ComponentStartup>(OnActiveUserStartup);
        SubscribeLocalEvent<ActiveSandevistanUserComponent, ComponentShutdown>(OnActiveUserShutdown);
        SubscribeLocalEvent<ActiveSandevistanUserComponent, EntParentChangedMessage>(OnActiveUserParentChanged);

        SubscribeLocalEvent<SandevistanTimeDilatedComponent, EntParentChangedMessage>(OnDilatedParentChanged);
        SubscribeLocalEvent<SandevistanSlowedProjectileComponent, EntParentChangedMessage>(OnSlowedProjectileParentChanged);
        SubscribeLocalEvent<ProjectileComponent, ComponentStartup>(OnProjectileStartup);

        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!HasAnyActiveOrDilated())
        {
            _reconcileAccumulator = 0f;
            return;
        }

        _reconcileAccumulator += frameTime;
        if (_reconcileAccumulator >= FullReconcileInterval)
        {
            _reconcileAccumulator = 0f;
            ReconcileMapDilation();
            return;
        }

        // Между полными проходами — только снаряды по уже известным активным картам.
        if (_activeMaps.Count > 0)
            UpdateProjectiles();
    }

    private bool HasAnyActiveOrDilated()
    {
        var activeQuery = EntityQueryEnumerator<ActiveSandevistanUserComponent>();
        if (activeQuery.MoveNext(out _, out _))
            return true;

        var dilatedQuery = EntityQueryEnumerator<SandevistanTimeDilatedComponent>();
        if (dilatedQuery.MoveNext(out _, out _))
            return true;

        var projQuery = EntityQueryEnumerator<SandevistanSlowedProjectileComponent>();
        return projQuery.MoveNext(out _, out _);
    }

    private void OnActiveUserStartup(Entity<ActiveSandevistanUserComponent> ent, ref ComponentStartup args)
    {
        ReconcileMapDilation();
    }

    private void OnActiveUserShutdown(Entity<ActiveSandevistanUserComponent> ent, ref ComponentShutdown args)
    {
        ReconcileMapDilation();
    }

    private void OnActiveUserParentChanged(Entity<ActiveSandevistanUserComponent> ent, ref EntParentChangedMessage args)
    {
        if (args.OldMapId != args.Transform.MapUid)
            ReconcileMapDilation();
    }

    private void OnDilatedParentChanged(Entity<SandevistanTimeDilatedComponent> ent, ref EntParentChangedMessage args)
    {
        if (args.OldMapId != args.Transform.MapUid)
        {
            ReconcileMapDilation();
            return;
        }

        // Предмет взял активный пользователь — снимаем замедление сразу, а не ждём полного прохода,
        // иначе его оружие до полсекунды стреляло бы замедленно.
        if (args.Transform.ParentUid.IsValid() && IsActiveSandevistanUser(args.Transform.ParentUid))
            RemCompDeferred<SandevistanTimeDilatedComponent>(ent);
    }

    private void OnSlowedProjectileParentChanged(Entity<SandevistanSlowedProjectileComponent> ent, ref EntParentChangedMessage args)
    {
        if (args.OldMapId != args.Transform.MapUid)
            ReconcileMapDilation();
    }

    private void OnProjectileStartup(Entity<ProjectileComponent> ent, ref ComponentStartup args)
    {
        if (_gameTicker.RunLevel != GameRunLevel.InRound)
            return;

        // Если стрелок уже замедлен дилатацией, GunRefreshModifiersEvent уже умножил ProjectileSpeed на TimeScale.
        // Ставим маркер заранее, чтобы ReconcileMapDilation не замедлил снаряд повторно.
        if (ent.Comp.Shooter is { } shooter &&
            TryComp<SandevistanTimeDilatedComponent>(shooter, out var shooterDilated) &&
            shooterDilated.LifeStage < ComponentLifeStage.Stopping)
        {
            var slowed = EnsureComp<SandevistanSlowedProjectileComponent>(ent);
            slowed.AppliedScale = Math.Clamp(shooterDilated.TimeScale, 0.05f, 1f);
        }
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        ReconcileMapDilation();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        ClearAllDilation();
    }

    /// <summary>
    /// Пересчитывает активные карты и синхронизирует <see cref="SandevistanTimeDilatedComponent"/> и скорости снарядов.
    /// </summary>
    public void ReconcileMapDilation()
    {
        _activeMaps.Clear();

        if (_gameTicker.RunLevel == GameRunLevel.InRound)
        {
            var activeQuery = EntityQueryEnumerator<ActiveSandevistanUserComponent, SandevistanUserComponent, TransformComponent>();
            while (activeQuery.MoveNext(out var uid, out var active, out var user, out var xform))
            {
                if (active.LifeStage >= ComponentLifeStage.Stopping || TerminatingOrDeleted(uid))
                    continue;

                if (xform.MapID == MapId.Nullspace)
                    continue;

                var scale = Math.Clamp(user.TimeDilationScale, 0.05f, 1f);
                if (_activeMaps.TryGetValue(xform.MapID, out var existing))
                    _activeMaps[xform.MapID] = MathF.Min(existing, scale);
                else
                    _activeMaps[xform.MapID] = scale;
            }
        }

        if (_activeMaps.Count == 0)
        {
            ClearAllDilation();
            return;
        }

        // 1. Снимаем дилатацию со всех, кто больше не подходит (сменил карту, стал активным пользователем или его предметом, призраком и т.п.)
        _toRemoveDilated.Clear();
        var dilatedQuery = EntityQueryEnumerator<SandevistanTimeDilatedComponent, TransformComponent>();
        while (dilatedQuery.MoveNext(out var uid, out var dilated, out var xform))
        {
            if (TerminatingOrDeleted(uid) ||
                IsActiveSandevistanUser(uid) ||
                (xform.ParentUid.IsValid() && IsActiveSandevistanUser(xform.ParentUid)) ||
                _ghostQuery.HasComp(uid) ||
                !_activeMaps.TryGetValue(xform.MapID, out var targetScale))
            {
                _toRemoveDilated.Add(uid);
                continue;
            }

            if (!MathHelper.CloseTo(dilated.TimeScale, targetScale))
            {
                dilated.TimeScale = targetScale;
                Dirty(uid, dilated);
            }
        }

        foreach (var uid in _toRemoveDilated)
        {
            RemComp<SandevistanTimeDilatedComponent>(uid);
        }

        // 2. Накладываем дилатацию на подходящие сущности на активных картах
        var moverQuery = EntityQueryEnumerator<MovementSpeedModifierComponent, TransformComponent>();
        while (moverQuery.MoveNext(out var uid, out _, out var xform))
        {
            TryApplyDilation(uid, xform);
        }

        var doAfterQuery = EntityQueryEnumerator<DoAfterComponent, TransformComponent>();
        while (doAfterQuery.MoveNext(out var uid, out _, out var xform))
        {
            TryApplyDilation(uid, xform);
        }

        var gunQuery = EntityQueryEnumerator<GunComponent, TransformComponent>();
        while (gunQuery.MoveNext(out var uid, out _, out var xform))
        {
            TryApplyDilation(uid, xform);
        }

        var meleeQuery = EntityQueryEnumerator<MeleeWeaponComponent, TransformComponent>();
        while (meleeQuery.MoveNext(out var uid, out _, out var xform))
        {
            TryApplyDilation(uid, xform);
        }

        UpdateProjectiles();
    }

    /// <summary>
    /// Восстанавливает снаряды, покинувшие активные карты, и замедляет новые. Дёшево (снарядов в полёте мало),
    /// поэтому вызывается каждый тик, пока есть активные карты.
    /// </summary>
    private void UpdateProjectiles()
    {
        // 3. Восстанавливаем снаряды, вышедшие за пределы активных карт
        _toRestoreProjectiles.Clear();
        var slowedProjQuery = EntityQueryEnumerator<SandevistanSlowedProjectileComponent, TransformComponent>();
        while (slowedProjQuery.MoveNext(out var uid, out _, out var xform))
        {
            if (TerminatingOrDeleted(uid) || !_activeMaps.ContainsKey(xform.MapID))
                _toRestoreProjectiles.Add(uid);
        }

        foreach (var uid in _toRestoreProjectiles)
        {
            RestoreProjectile(uid);
        }

        // 4. Замедляем новые/летящие снаряды на активных картах
        var projQuery = EntityQueryEnumerator<ProjectileComponent, PhysicsComponent, TransformComponent>();
        while (projQuery.MoveNext(out var uid, out _, out var physics, out var xform))
        {
            if (_slowedQuery.HasComp(uid) || TerminatingOrDeleted(uid))
                continue;

            if (!_activeMaps.TryGetValue(xform.MapID, out var scale))
                continue;

            var slowed = EnsureComp<SandevistanSlowedProjectileComponent>(uid);
            slowed.AppliedScale = scale;
            _physics.SetLinearVelocity(uid, physics.LinearVelocity * scale, body: physics);
        }
    }

    private void TryApplyDilation(EntityUid uid, TransformComponent xform)
    {
        // Сначала самая дешёвая проверка — карта; сущности с других карт отсеиваются без обращений к компонентам.
        if (!_activeMaps.TryGetValue(xform.MapID, out var scale) ||
            TerminatingOrDeleted(uid) ||
            IsActiveSandevistanUser(uid) ||
            (xform.ParentUid.IsValid() && IsActiveSandevistanUser(xform.ParentUid)) ||
            _ghostQuery.HasComp(uid))
        {
            return;
        }

        if (!_dilatedQuery.TryComp(uid, out var dilated))
            dilated = EnsureComp<SandevistanTimeDilatedComponent>(uid);
        if (!MathHelper.CloseTo(dilated.TimeScale, scale))
        {
            dilated.TimeScale = scale;
            Dirty(uid, dilated);
        }
    }

    private bool IsActiveSandevistanUser(EntityUid uid)
    {
        return _activeQuery.TryComp(uid, out var active) &&
               active.LifeStage < ComponentLifeStage.Stopping;
    }

    private void ClearAllDilation()
    {
        _toRemoveDilated.Clear();
        var dilatedQuery = EntityQueryEnumerator<SandevistanTimeDilatedComponent>();
        while (dilatedQuery.MoveNext(out var uid, out _))
        {
            _toRemoveDilated.Add(uid);
        }

        foreach (var uid in _toRemoveDilated)
        {
            RemComp<SandevistanTimeDilatedComponent>(uid);
        }

        _toRestoreProjectiles.Clear();
        var projQuery = EntityQueryEnumerator<SandevistanSlowedProjectileComponent>();
        while (projQuery.MoveNext(out var uid, out _))
        {
            _toRestoreProjectiles.Add(uid);
        }

        foreach (var uid in _toRestoreProjectiles)
        {
            RestoreProjectile(uid);
        }
    }

    private void RestoreProjectile(EntityUid uid)
    {
        if (!TryComp<SandevistanSlowedProjectileComponent>(uid, out var slowed))
            return;

        if (!TerminatingOrDeleted(uid) &&
            TryComp<PhysicsComponent>(uid, out var physics) &&
            slowed.AppliedScale > 0.001f)
        {
            _physics.SetLinearVelocity(uid, physics.LinearVelocity / slowed.AppliedScale, body: physics);
        }

        RemComp<SandevistanSlowedProjectileComponent>(uid);
    }
}
