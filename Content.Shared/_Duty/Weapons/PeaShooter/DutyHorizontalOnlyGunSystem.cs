using System.Numerics;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;

namespace Content.Shared._Duty.Weapons.PeaShooter;

/// <summary>
/// _Duty: см. <see cref="DutyHorizontalOnlyGunComponent"/>. Работает через <see cref="GunShotEvent"/>,
/// который движок кидает по ref прямо перед вызовом Shoot — успеваем подменить точку прицеливания
/// до того, как снаряд посчитает направление полёта. Чистая математика без рандома и без
/// серверного состояния — безопасно предсказывается и на клиенте.
/// </summary>
public sealed class DutyHorizontalOnlyGunSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyHorizontalOnlyGunComponent, GunShotEvent>(OnShot);
    }

    private void OnShot(Entity<DutyHorizontalOnlyGunComponent> ent, ref GunShotEvent args)
    {
        var fromMap = _transform.ToMapCoordinates(args.FromCoordinates);
        var toMap = _transform.ToMapCoordinates(args.ToCoordinates);

        if (fromMap.MapId != toMap.MapId)
            return;

        var direction = toMap.Position.X >= fromMap.Position.X ? 1f : -1f;
        var targetPos = fromMap.Position + new Vector2(direction * ent.Comp.AimDistance, 0f);

        args.ToCoordinates = _transform.ToCoordinates(new MapCoordinates(targetPos, fromMap.MapId));
    }
}
