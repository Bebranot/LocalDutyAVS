// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Trauma.Components;

/// <summary>
/// _Duty: артериальное кровотечение — беззонный дебафф, который надо лечить (жгутом/зажимом).
///
/// Реализовано как «непросыхающая рана»: пока висит компонент, серверный
/// <c>ArterialBleedSystem</c> периодически подкачивает <c>BloodstreamComponent.BleedAmount</c>
/// (тот сам клампится на своём максимуме), поэтому ванильный пайплайн кровит быстро и стабильно,
/// а обычный клоттинг НЕ обнуляет кровотечение и не гасит алерт «кровь». Обычная повязка такое
/// не остановит — снимается только специальным лечением (см. системы лечения травм), при снятии
/// накопленный bleed резко гасится (жгут останавливает быстро).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ArterialBleedComponent : Component
{
    /// <summary>
    /// На сколько поднимать bleed за одну подкачку. Значение с запасом перекрывает естественный
    /// клоттинг, так что bleed держится у своего потолка (см. <c>BloodstreamComponent.MaxBleedAmount</c>).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float BleedTopUp = 4f;

    /// <summary>Период подкачки в секундах.</summary>
    [DataField, AutoNetworkedField]
    public float UpdateIntervalSeconds = 1.5f;

    /// <summary>Время следующей подкачки (серверное, не сетевое).</summary>
    [DataField]
    public TimeSpan NextUpdate;
}
