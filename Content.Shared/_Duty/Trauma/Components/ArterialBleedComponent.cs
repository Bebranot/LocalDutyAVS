// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Trauma.Components;

/// <summary>
/// _Duty: артериальное кровотечение — беззонный дебафф, который надо лечить (жгутом/зажимом).
/// Даёт быстрый доп. отток крови поверх обычного bleed'а, НЕ подменяя его: слушает
/// <see cref="Content.Shared.Body.Events.BleedModifierEvent"/> и добавляет
/// <see cref="ExtraBleedPerTick"/> к оттоку за тик. Обычная повязка такое не останавливает —
/// снимается только специальным лечением (см. системы лечения травм).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ArterialBleedComponent : Component
{
    /// <summary>
    /// Доп. кровопотеря за тик поверх обычной. Сетевое — тик bleed'а предсказывается на клиенте,
    /// поэтому значение должно совпадать на обеих сторонах.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ExtraBleedPerTick = 1.0f;

    /// <summary>
    /// Разовый скачок накопленного bleed'а в момент получения травмы («резкая кровопотеря»).
    /// Применяется один раз сервером при наложении, поэтому сеть не нужна.
    /// </summary>
    [DataField]
    public float InitialBleedSpike = 5.0f;
}
