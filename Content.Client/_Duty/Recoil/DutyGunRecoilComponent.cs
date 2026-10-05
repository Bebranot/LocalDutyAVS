// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

namespace Content.Client._Duty.Recoil;

/// <summary>
/// Только клиент, только стрелок: состояние отдачи камеры. Висит, пока камера не успокоилась,
/// потом снимается — без стрельбы система ничего не считает.
/// </summary>
[RegisterComponent]
public sealed partial class DutyGunRecoilComponent : Component
{
    /// <summary>Смещение пружины толчка (мировые координаты, тайлы).</summary>
    public Vector2 Position;

    /// <summary>Скорость пружины толчка.</summary>
    public Vector2 Velocity;

    /// <summary>Собственная частота пружины последнего выстрела, рад/с: тяжёлый калибр «качает» медленнее.</summary>
    public float Omega = 28f;

    /// <summary>«Травма» 0..1 — накопленная тряска от очереди.</summary>
    public float Trauma;

    /// <summary>Время шума тряски; фазы — свои у каждого компонента, чтобы тряска не повторялась.</summary>
    public float NoiseTime;
    public Vector4 NoisePhase;

    /// <summary>Сторона, куда уводит очередь (±1), и время последнего выстрела (реальное).</summary>
    public float DriftSign = 1f;
    public TimeSpan LastShot;

    /// <summary>Итоговое смещение для GetEyeOffsetEvent: толчок + тряска.</summary>
    public Vector2 Offset;
}
