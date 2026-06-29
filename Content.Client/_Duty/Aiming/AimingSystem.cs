using Content.Shared._Duty.Aiming;
using Content.Shared._Duty.Aiming.Events;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Input;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Wieldable.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Aiming;

/// <summary>
/// Следит за состоянием клавиши Aim (по умолчанию ПКМ) и шлёт предсказанные запросы
/// на начало/конец прицеливания. Серверная/общая валидация и эффекты — в SharedAimingSystem.
/// </summary>
public sealed class AimingSystem : EntitySystem
{
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly IInputManager _inputManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly InputSystem _inputSystem = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (_player.LocalEntity is not { } user)
            return;

        var down = _inputSystem.CmdStates.GetState(ContentKeyFunctions.Aim) == BoundKeyState.Down;
        var aiming = HasComp<AimingComponent>(user);

        if (!down)
        {
            if (aiming)
                RaisePredictiveEvent(new RequestStopAimEvent());
            return;
        }

        if (aiming)
            return;

        if (!TryGetAimableGun(user, out var gunUid))
            return;

        var mousePos = _eyeManager.PixelToMap(_inputManager.MouseScreenPosition);

        if (mousePos.MapId == MapId.Nullspace)
            return;

        var coordinates = _xform.ToCoordinates(user, mousePos);

        RaisePredictiveEvent(new RequestAimEvent
        {
            Gun = GetNetEntity(gunUid),
            Coordinates = GetNetCoordinates(coordinates),
        });
    }

    private bool TryGetAimableGun(EntityUid user, out EntityUid gunUid)
    {
        gunUid = default;

        if (!_hands.TryGetActiveItem(user, out var heldNullable) || heldNullable is not { } held)
            return false;

        if (!TryComp<WieldableComponent>(held, out var wieldable) || !wieldable.Wielded)
            return false;

        if (!TryComp<GunComponent>(held, out var gun) || !gun.UseKey)
            return false;

        if (!HasComp<AimableComponent>(held))
            return false;

        gunUid = held;
        return true;
    }
}
