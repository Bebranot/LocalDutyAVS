// SPDX-FileCopyrightText: 2026 LocalDuty
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Sandevistan;

/// <summary>
/// _Duty: маркер замедления времени на сущности, находящейся на одной карте с активным пользователем Sandevistan.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SandevistanTimeDilatedComponent : Component
{
    /// <summary>
    /// Множитель скорости течения времени для сущности (например, 0.2f = в 5 раз медленнее).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TimeScale = 0.2f;
}

/// <summary>
/// _Duty: хранит применённый коэффициент замедления на снаряде, чтобы чисто восстановить скорость при выходе из дилатации.
/// </summary>
[RegisterComponent]
public sealed partial class SandevistanSlowedProjectileComponent : Component
{
    [DataField]
    public float AppliedScale = 1f;
}
