using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Weapons.PeaShooter;

/// <summary>
/// _Duty: маркер-компонент на оружии. Горохострел не рисован смотрящим вверх/вниз (только
/// влево/вправо), поэтому вместе с ним ограничиваем и стрельбу — снаряд всегда летит строго
/// по горизонтали в сторону курсора, а не туда, куда реально целится игрок.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DutyHorizontalOnlyGunComponent : Component
{
    /// <summary>На какое расстояние (в тайлах) отодвигать точку прицеливания по горизонтали.
    /// Не влияет на реальную дальность полёта снаряда — снаряду важно только направление.</summary>
    [DataField]
    public float AimDistance = 10f;
}
