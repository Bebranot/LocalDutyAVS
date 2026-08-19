using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.Physics;
using Timer = Robust.Shared.Timing.Timer;

/*
    ╔════════════════════════════════════╗
    ║   Schrödinger's Cat Code   🐾      ║
    ║   /\_/\\                           ║
    ║  ( o.o )  Meow!                    ║
    ║   > ^ <                            ║
    ╚════════════════════════════════════╝

*/

namespace Content.Server.ADT.SpeedBoostWake;

public sealed class SlippingWakeSystem : EntitySystem
{
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeedModifierSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SpeedBoostWakeComponent, PhysicsWakeEvent>(MobWakeCheck);
    }

    public void MobWakeCheck(EntityUid uid, SpeedBoostWakeComponent comp, PhysicsWakeEvent args)
    {
        var movementSpeed = EnsureComp<MovementSpeedModifierComponent>(uid);

        var baseWalkSpeed = movementSpeed.BaseWalkSpeed;
        var baseSprintSpeed = movementSpeed.BaseSprintSpeed;
        var boostedSprintSpeed = baseSprintSpeed * comp.SpeedModified;
        var boostedWalkSpeed = baseWalkSpeed * comp.SpeedModified;

        _movementSpeedModifierSystem.ChangeBaseSpeed(uid, boostedWalkSpeed, boostedSprintSpeed, movementSpeed.Acceleration, movementSpeed);

        _stun.TryUpdateParalyzeDuration(uid, TimeSpan.FromSeconds(comp.ParalyzeTime));

        // _Duty: было — второй ChangeBaseSpeed вызывался СРАЗУ следующей строкой, синхронно,
        // без единого await/задержки, так что буст скорости откатывался в тот же тик, что и
        // применялся, и фактически никогда не действовал. Откат нужно отложить на ParalyzeTime.
        Timer.Spawn(TimeSpan.FromSeconds(comp.ParalyzeTime), () =>
        {
            if (!Exists(uid) || !TryComp<MovementSpeedModifierComponent>(uid, out var current))
                return;

            _movementSpeedModifierSystem.ChangeBaseSpeed(uid, baseWalkSpeed, baseSprintSpeed, current.Acceleration, current);
        });
    }

}
