using Content.Shared._Duty.Aiming.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Aiming;

/// <summary>
/// Прицеливание с двуручного огнестрела (зажатие клавиши Aim/ПКМ): увеличивает FOV, замедляет движение,
/// уменьшает разброс пуль. Сбивается при получении урона, оглушении, смене боеспособности.
/// Нельзя сменить стойку (лечь/встать) во время прицеливания; выход из прицеливания лёжа
/// на короткое время обездвиживает персонажа.
/// </summary>
public sealed class SharedAimingSystem : EntitySystem
{
    [Dependency] private readonly SharedContentEyeSystem _contentEye = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeedModifier = default!;
    [Dependency] private readonly MovementModStatusSystem _movementMod = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly EntProtoId AimRecoveryEffect = "AimRecoveryImmobilizeStatusEffect";
    private static readonly TimeSpan TooCloseWarningCooldown = TimeSpan.FromSeconds(5);

    private readonly Dictionary<EntityUid, TimeSpan> _nextTooCloseWarning = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeAllEvent<RequestAimEvent>(OnAimRequest);
        SubscribeAllEvent<RequestStopAimEvent>(OnStopAimRequest);

        SubscribeLocalEvent<AimableComponent, ItemUnwieldedEvent>(OnGunUnwielded);

        SubscribeLocalEvent<AimableComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);

        SubscribeLocalEvent<AimingComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<AimingComponent, DamageChangedEvent>(OnAimingDamaged);
        SubscribeLocalEvent<AimingComponent, StunnedEvent>(OnAimingStunned);
        SubscribeLocalEvent<AimingComponent, MobStateChangedEvent>(OnAimingMobStateChanged);
        SubscribeLocalEvent<AimingComponent, KnockDownAttemptEvent>(OnAimingKnockDownAttempt);
        SubscribeLocalEvent<AimingComponent, StandUpAttemptEvent>(OnAimingStandUpAttempt);
        SubscribeLocalEvent<AimingComponent, ComponentShutdown>(OnAimingShutdown);
    }

    private void OnAimRequest(RequestAimEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user)
            return;

        var gunUid = GetEntity(msg.Gun);

        if (HasComp<AimingComponent>(user))
            return;

        if (!TryComp<AimableComponent>(gunUid, out var aimable) ||
            !TryComp<WieldableComponent>(gunUid, out var wieldable) ||
            !wieldable.Wielded || wieldable.User != user ||
            !TryComp<GunComponent>(gunUid, out var gun) ||
            !gun.UseKey)
        {
            return;
        }

        var clickPos = _transform.ToMapCoordinates(GetCoordinates(msg.Coordinates)).Position;
        var userPos = _transform.GetWorldPosition(user);

        if ((clickPos - userPos).Length() < aimable.MinAimDistance)
        {
            if (!_nextTooCloseWarning.TryGetValue(user, out var next) || _timing.CurTime >= next)
            {
                _popup.PopupClient(Loc.GetString("aiming-too-close"), user, user);
                _nextTooCloseWarning[user] = _timing.CurTime + TooCloseWarningCooldown;
                PruneTooCloseWarnings();
            }

            return;
        }

        var aiming = AddComp<AimingComponent>(user);
        aiming.Gun = gunUid;
        aiming.IsProne = HasComp<KnockedDownComponent>(user);
        Dirty(user, aiming);

        _contentEye.SetZoom(user, System.Numerics.Vector2.One * (aiming.IsProne ? aimable.ProneZoomMultiplier : aimable.ZoomMultiplier), true);
        _movementSpeedModifier.RefreshMovementSpeedModifiers(user);
        _gun.RefreshModifiers(gunUid);
        EnsureSafeSpreadAngles(gunUid);

        _popup.PopupClient(Loc.GetString("aiming-start"), user, user);
    }

    /// <summary>
    /// Чистит словарь кулдаунов предупреждения "слишком близко" от просроченных/мёртвых записей.
    /// Без этого ключи-сущности (мобы) копятся бесконечно за время жизни сервера (утечка памяти).
    /// </summary>
    private void PruneTooCloseWarnings()
    {
        var now = _timing.CurTime;
        if (_nextTooCloseWarning.Count < 64)
            return;

        var stale = new List<EntityUid>();
        foreach (var (uid, time) in _nextTooCloseWarning)
        {
            if (now >= time || !Exists(uid))
                stale.Add(uid);
        }

        foreach (var uid in stale)
            _nextTooCloseWarning.Remove(uid);
    }

    private void OnStopAimRequest(RequestStopAimEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } user)
            StopAiming(user);
    }

    public void StopAiming(EntityUid user)
    {
        if (!TryComp<AimingComponent>(user, out var aiming))
            return;

        var gunUid = aiming.Gun;
        var wasProne = aiming.IsProne;

        RemComp<AimingComponent>(user);

        _contentEye.ResetZoom(user);
        _movementSpeedModifier.RefreshMovementSpeedModifiers(user);

        if (Exists(gunUid))
        {
            _gun.RefreshModifiers(gunUid);
            EnsureSafeSpreadAngles(gunUid);
        }

        if (wasProne && TryComp<AimableComponent>(gunUid, out var aimable))
        {
            _movementMod.TryAddMovementSpeedModDuration(user, AimRecoveryEffect, aimable.PostProneAimImmobilizeDuration, 0.01f);
            _popup.PopupClient(Loc.GetString("aiming-recovery"), user, user);
        }

        _popup.PopupClient(Loc.GetString("aiming-stop"), user, user);
    }

    private void OnAimingShutdown(Entity<AimingComponent> ent, ref ComponentShutdown args)
    {
        _contentEye.ResetZoom(ent);
        _movementSpeedModifier.RefreshMovementSpeedModifiers(ent);

        if (Exists(ent.Comp.Gun))
        {
            _gun.RefreshModifiers(ent.Comp.Gun);
            EnsureSafeSpreadAngles(ent.Comp.Gun);
        }
    }

    private void OnGunUnwielded(Entity<AimableComponent> ent, ref ItemUnwieldedEvent args)
    {
        if (TryComp<AimingComponent>(args.User, out var aiming) && aiming.Gun == ent.Owner)
            StopAiming(args.User);
    }

    private void OnGunRefreshModifiers(Entity<AimableComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (!TryComp<WieldableComponent>(ent, out var wieldable) ||
            wieldable.User is not { } user ||
            !TryComp<AimingComponent>(user, out var aiming) ||
            aiming.Gun != ent.Owner)
        {
            return;
        }

        // Минимальный пол на угол на случай если другие модификаторы (GunWieldBonus и т.д.) применяются
        // после нас в порядке подписчиков — иначе MaxAngleModified может уйти в ноль/минус и сломать
        // assert в GetRecoilAngle.
        const double minTheta = 0.001;

        var minAngle = Math.Max(args.MinAngle.Theta * ent.Comp.AimSpreadMultiplier, minTheta);
        var maxAngle = Math.Max(args.MaxAngle.Theta * ent.Comp.AimSpreadMultiplier, minAngle);

        args.MinAngle = new Angle(minAngle);
        args.MaxAngle = new Angle(maxAngle);
        args.AngleIncrease = new Angle(Math.Max(args.AngleIncrease.Theta * ent.Comp.AimSpreadMultiplier, 0));
        args.CameraRecoilScalar *= ent.Comp.AimCameraRecoilScalar;

        // Прицеливание лёжа снижает скорострельность (стрельба с упора — точнее, но реже).
        if (aiming.IsProne)
            args.FireRate *= ent.Comp.ProneFireRateMultiplier;
    }

    /// <summary>
    /// Принудительно гарантирует, что итоговые углы разброса оружия после RefreshModifiers не уходят
    /// в ноль/минус и что Min &lt;= Max. Применяется ПОСЛЕ полного завершения GunRefreshModifiersEvent,
    /// поэтому не зависит от порядка срабатывания подписчиков (наш множитель внутри события мог
    /// применяться раньше или позже, например, GunWieldBonusComponent — итоговый результат всё равно
    /// будет безопасным. Без этого Content.Server.Weapons.Ranged.Systems.GunSystem.GetRecoilAngle
    /// падает на DebugTools.Assert при отрицательном MaxAngleModified.
    /// </summary>
    private void EnsureSafeSpreadAngles(EntityUid gunUid)
    {
        if (!TryComp<GunComponent>(gunUid, out var gun))
            return;

        const double minTheta = 0.001;

        var min = Math.Max(gun.MinAngleModified.Theta, minTheta);
        var max = Math.Max(gun.MaxAngleModified.Theta, min);

        if (gun.MinAngleModified.Theta != min)
        {
            gun.MinAngleModified = new Angle(min);
            DirtyField(gunUid, gun, nameof(GunComponent.MinAngleModified));
        }

        if (gun.MaxAngleModified.Theta != max)
        {
            gun.MaxAngleModified = new Angle(max);
            DirtyField(gunUid, gun, nameof(GunComponent.MaxAngleModified));
        }

        if (gun.AngleIncreaseModified.Theta < 0)
        {
            gun.AngleIncreaseModified = new Angle(0);
            DirtyField(gunUid, gun, nameof(GunComponent.AngleIncreaseModified));
        }
    }

    private void OnRefreshMovementSpeed(Entity<AimingComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!TryComp<AimableComponent>(ent.Comp.Gun, out var aimable))
            return;

        // Лёжа прицеливание полностью обездвиживает (стрельба с упора). Стоя — обычное замедление.
        if (ent.Comp.IsProne)
            args.ModifySpeed(0f, 0f);
        else
            args.ModifySpeed(aimable.WalkSpeedModifier, aimable.SprintSpeedModifier);
    }

    private void OnAimingDamaged(Entity<AimingComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased)
            StopAiming(ent);
    }

    private void OnAimingStunned(Entity<AimingComponent> ent, ref StunnedEvent args)
    {
        StopAiming(ent);
    }

    private void OnAimingMobStateChanged(Entity<AimingComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            StopAiming(ent);
    }

    private void OnAimingKnockDownAttempt(Entity<AimingComponent> ent, ref KnockDownAttemptEvent args)
    {
        if (ent.Comp.IsProne)
            return;

        args.Cancelled = true;
        _popup.PopupClient(Loc.GetString("aiming-cant-change-stance"), ent, ent);
    }

    private void OnAimingStandUpAttempt(Entity<AimingComponent> ent, ref StandUpAttemptEvent args)
    {
        if (!ent.Comp.IsProne)
            return;

        args.Cancelled = true;
        args.Message = (Loc.GetString("aiming-cant-change-stance"), PopupType.SmallCaution);
    }
}
