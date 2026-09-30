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
public sealed partial class AimingSystem : EntitySystem
{
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IInputManager _inputManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private InputSystem _inputSystem = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>
    /// Пауза между повторными заявками, пока клавиша зажата, а прицеливание так и не началось
    /// (курсор слишком близко, оружие не взято в обе руки на сервере и т.п.). Без неё отклонённая
    /// заявка уходила на сервер каждый тик — десятки сетевых сообщений в секунду впустую.
    /// </summary>
    private static readonly TimeSpan RequestRetryInterval = TimeSpan.FromSeconds(0.2);

    private TimeSpan _nextAimRequest;

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
            // Новое нажатие должно начинать прицел сразу, без ожидания паузы от прошлого.
            _nextAimRequest = TimeSpan.Zero;

            if (aiming)
                RaisePredictiveEvent(new RequestStopAimEvent());
            return;
        }

        if (aiming || _timing.CurTime < _nextAimRequest)
            return;

        if (!TryGetAimableGun(user, out var gunUid))
            return;

        var mousePos = _eyeManager.PixelToMap(_inputManager.MouseScreenPosition);

        if (mousePos.MapId == MapId.Nullspace)
            return;

        var coordinates = _xform.ToCoordinates(user, mousePos);

        _nextAimRequest = _timing.CurTime + RequestRetryInterval;
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

        if (!TryComp<AimableComponent>(held, out var aimable))
            return false;

        if (aimable.RequiresWield)
        {
            if (!TryComp<WieldableComponent>(held, out var wieldable) || !wieldable.Wielded)
                return false;
        }

        if (!TryComp<GunComponent>(held, out var gun) || !gun.UseKey)
            return false;

        gunUid = held;
        return true;
    }
}
