// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Trauma.Components;

/// <summary>
/// _Duty: артериальное кровотечение — беззонный дебафф, который надо лечить (жгутом/зажимом).
///
/// Модель «медленная утечка + стабильный урон»: пока уровень крови выше <see cref="BloodFloor"/>,
/// серверный <c>ArterialBleedSystem</c> поддерживает небольшую скорость кровотечения
/// (<see cref="BleedTarget"/>) — видимые лужи, бледность и ванильный Bloodloss-урон. У пола
/// кровотечение прекращается, поэтому кровь НЕ утекает в ноль: смерть наступает от накопленного
/// Bloodloss-урона, а не от нулевого объёма крови. Обычная повязка такое не остановит — снимается
/// только специальным лечением, при снятии кровотечение гасится.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ArterialBleedComponent : Component
{
    /// <summary>
    /// Доля крови, ниже которой перестаём кровить (чтобы объём не уходил в ноль). Держится чуть
    /// ниже ванильного порога кровопотери, чтобы Bloodloss-урон шёл стабильно, но без разгона.
    /// </summary>
    [DataField]
    public float BloodFloor = 0.6f;

    /// <summary>
    /// Поддерживаемая скорость кровотечения (медленная утечка), пока крови больше <see cref="BloodFloor"/>.
    /// </summary>
    [DataField]
    public float BleedTarget = 1.5f;

    /// <summary>Период тика поддержки в секундах.</summary>
    [DataField]
    public float UpdateIntervalSeconds = 1f;

    /// <summary>Время следующего тика (серверное).</summary>
    [DataField]
    public TimeSpan NextUpdate;
}
