// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared.ActionBlocker;
using Content.Shared.Camera;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Vehicle;
using Robust.Shared.Map;

namespace Content.Shared._RMC14.Vehicle;

// _Duty: замены для RMC-систем, которых у нас нет (телепорт с тягой, тряска камеры).
public sealed partial class VehicleSystem
{
    [Dependency] private readonly ActionBlockerSystem _dutyBlocker = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _cameraRecoil = default!;

    /// <summary>
    /// Переносит пользователя в точку вместе с тем, кого он тащит (как RMC Teleporter.HandlePulling):
    /// иначе тягу рвёт на переходе между картой станции и картой интерьера.
    /// </summary>
    private void HandlePulling(EntityUid user, MapCoordinates teleport)
    {
        if (TryComp(user, out PullableComponent? otherPullable) && otherPullable.Puller != null)
            _pulling.TryStopPull(user, otherPullable, otherPullable.Puller.Value);

        if (TryComp(user, out PullerComponent? puller) &&
            puller.Pulling is { } pulling &&
            TryComp(pulling, out PullableComponent? pullable))
        {
            if (TryComp(pulling, out PullerComponent? chained) &&
                chained.Pulling is { } chainedPulling &&
                TryComp(chainedPulling, out PullableComponent? chainedPullable))
            {
                _pulling.TryStopPull(chainedPulling, chainedPullable, pulling);
            }

            _pulling.TryStopPull(pulling, pullable, user);
            _transform.SetMapCoordinates(user, teleport);
            _transform.SetMapCoordinates(pulling, teleport);
            _pulling.TryStartPull(user, pulling);
            return;
        }

        _transform.SetMapCoordinates(user, teleport);
    }

    private void InitializeDuty()
    {
        SubscribeLocalEvent<GridVehicleMoverComponent, VehicleOperatorSetEvent>(OnGridOperatorSet);
    }

    /// <summary>
    /// В RMC ванильный VehicleSystem пропатчен под MovementKind.Grid. Мы апстрим не трогаем:
    /// ванилла ставит обычный relay ввода, а здесь мы меняем его на сеточное управление RMC
    /// (водитель сидит в интерьере на другой карте, его ввод читает GridVehicleMoverSystem).
    /// </summary>
    private void OnGridOperatorSet(Entity<GridVehicleMoverComponent> ent, ref VehicleOperatorSetEvent args)
    {
        if (args.OldOperator is { } old && old != args.NewOperator)
        {
            RemCompDeferred<GridVehicleOperatorComponent>(old);
            RemCompDeferred<VehicleWatchingComponent>(old);
        }

        if (args.NewOperator is not { } driver)
            return;

        // Снимаем сразу и пересчитываем CanMove: пока relay висит, пристёгнутому разрешено «ходить»,
        // и мехвод вместо езды вставал бы с кресла.
        RemComp<RelayInputMoverComponent>(driver);
        RemComp<MovementRelayTargetComponent>(ent.Owner);
        _dutyBlocker.UpdateCanMove(driver);
        EnsureComp<GridVehicleOperatorComponent>(driver);

        var watching = EnsureComp<VehicleWatchingComponent>(driver);
        watching.Watching = ent.Owner;
        Dirty(driver, watching);
    }

    private void ShakeCamera(EntityUid occupant, int strength)
    {
        _cameraRecoil.KickCamera(occupant, new Vector2(strength * 0.25f, strength * 0.25f));
    }
}
