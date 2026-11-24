// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Tank;
using Content.Shared._RMC14.Stun;
using Content.Shared.Doors.Components;
using Content.Shared.Projectiles;
using Content.Shared.Tag;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Vehicle;

// _Duty: в RMC здесь ксено-системы (укрепление, размеры, пролезание под техникой).
// У нас ксеноморфов RMC нет — оставляем вызовы, но через пустые заглушки, чтобы не править логику столкновений.
public sealed partial class GridVehicleMoverSystem
{
    private readonly NoXenoFortify _fortify = new();
    private readonly NoXenoSize _size = new();
    private readonly NoSqueezeUnder _squeezeUnder = new();

    [Dependency] private readonly TagSystem _dutyTag = default!;

    private static readonly ProtoId<TagPrototype> DutyAirlockTag = "Airlock";
    private static readonly ProtoId<TagPrototype> DutyWindowTag = "Window";

    /// <summary>
    /// Предохранитель сноса для танка (DutyTankSmasher). Если цель проходит проверку —
    /// навешиваем ей VehicleSmashable, дальше работает обычная логика сноса RMC.
    /// Только на сервере: проверка дешёвая, но добавлять сетевой компонент на клиенте нельзя.
    /// Клиент на один тик предскажет упор в стену — это допустимо.
    /// </summary>
    private void DutyTryMarkSmashable(EntityUid vehicle, EntityUid target)
    {
        if (_net.IsClient || HasComp<VehicleSmashableComponent>(target))
            return;

        if (!TryComp(vehicle, out DutyTankSmasherComponent? smasher))
            return;

        if (!DutyIsSmashableStructure(target, smasher))
            return;

        if (DutyNearSpace(target))
            return;

        var smashable = EnsureComp<VehicleSmashableComponent>(target);
        smashable.DeleteOnHit = true;
        smashable.DamageOnHit = smasher.DamageOnHit;
        smashable.SlowdownMultiplier = smasher.SlowdownMultiplier;
        smashable.SlowdownDuration = smasher.SlowdownDuration;
    }

    private bool DutyIsSmashableStructure(EntityUid target, DutyTankSmasherComponent smasher)
    {
        var protoId = MetaData(target).EntityPrototype?.ID;
        if (protoId == null)
            return false;

        foreach (var part in smasher.ForbiddenIdParts)
        {
            if (protoId.Contains(part, StringComparison.Ordinal))
                return false;
        }

        // Тега «стена» в ванилле нет — опознаём по id прототипа (WallSolid, WallWood, ...).
        if (protoId.StartsWith("Wall", StringComparison.Ordinal) ||
            protoId.StartsWith("Grille", StringComparison.Ordinal) ||
            protoId.StartsWith("Girder", StringComparison.Ordinal))
        {
            return true;
        }

        if (HasComp<DoorComponent>(target) && _dutyTag.HasTag(target, DutyAirlockTag))
            return true;

        return _dutyTag.HasTag(target, DutyWindowTag);
    }

    /// <summary>
    /// Есть ли космос (пустой тайл) в квадрате 3×3 вокруг цели. Сущность вне грида считаем «у космоса».
    /// </summary>
    private bool DutyNearSpace(EntityUid target)
    {
        var xform = Transform(target);
        if (xform.GridUid is not { } gridUid || !TryComp(gridUid, out MapGridComponent? grid))
            return true;

        var center = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var indices = new Vector2i(center.X + dx, center.Y + dy);
                if (!_map.TryGetTileRef(gridUid, grid, indices, out var tile) || tile.Tile.IsEmpty)
                    return true;
            }
        }

        return false;
    }

    [Dependency] private readonly VehicleTopologySystem _dutyTopology = default!;

    private bool DutyIsOwnProjectile(EntityUid vehicle, EntityUid other)
    {
        if (!TryComp(other, out ProjectileComponent? projectile))
            return false;

        if (projectile.Weapon is not { } weapon)
            return false;

        if (weapon == vehicle)
            return true;

        return _dutyTopology.TryGetVehicle(weapon, out var weaponVehicle) && weaponVehicle == vehicle;
    }

    private sealed class NoXenoFortify
    {
        public bool IsFortified(EntityUid xeno) => false;

        public void TryRelocateFortified<T>(EntityUid xeno, T target)
        {
        }
    }

    private sealed class NoXenoSize
    {
        public bool TryGetSize(EntityUid uid, out RMCSizes size)
        {
            size = RMCSizes.Humanoid;
            return false;
        }
    }

    private sealed class NoSqueezeUnder
    {
        public bool CanSqueezeUnder(Entity<VehicleSqueezeUnderComponent> vehicle, EntityUid xeno) => false;

        public void TryMarkUnder(EntityUid xeno, Entity<VehicleSqueezeUnderComponent> vehicle)
        {
        }
    }
}
