// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Implants.Components;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Cyberware;

/// <summary>
/// _Duty: общий API киберимплантов. Состояние меняет только сервер (<c>DutyCyberwareSystem</c>),
/// а проверки «работает ли модуль» доступны обеим сторонам — для предикта пассивных эффектов.
/// </summary>
public abstract partial class SharedDutyCyberwareSystem : EntitySystem
{
    [Dependency] protected IGameTiming Timing = default!;

    /// <summary>Носитель импланта, если имплант сейчас вживлён.</summary>
    public bool TryGetHost(EntityUid implant, out EntityUid host)
    {
        host = default;
        if (!TryComp<SubdermalImplantComponent>(implant, out var sub) || sub.ImplantedEntity is not { } owner)
            return false;

        host = owner;
        return true;
    }

    /// <summary>Носитель сейчас под ЭМИ-сбоем.</summary>
    public bool IsHostEmped(EntityUid host)
    {
        return TryComp<DutyCyberHostComponent>(host, out var comp)
               && comp.EmpUntil is { } until
               && until > Timing.CurTime;
    }

    /// <summary>Носитель перегружен (нагрузка выше лимита).</summary>
    public bool IsOverloaded(EntityUid host)
    {
        return TryComp<DutyCyberHostComponent>(host, out var comp) && comp.Load > comp.LoadLimit;
    }

    /// <summary>
    /// Можно ли сейчас пользоваться модулем: вживлён, не выгорел, не выключен и не задавлен ЭМИ.
    /// Импланты с действиями зовут это в начале обработчика.
    /// </summary>
    public bool IsModuleOnline(EntityUid implant, DutyCyberwareComponent? comp = null)
    {
        if (!Resolve(implant, ref comp, false))
            return false;

        if (comp.State is DutyCyberModuleState.Offline or DutyCyberModuleState.Burned)
            return false;

        if (!TryGetHost(implant, out var host))
            return false;

        return !comp.EmpSensitive || !IsHostEmped(host);
    }

    /// <summary>
    /// Сколько всего имплантов-прототипа <paramref name="predicate"/> в теле (для стакающихся модулей).
    /// </summary>
    /// <summary>
    /// Первый оставшийся в теле имплант с компонентом <typeparamref name="T"/>. Нужен держателям ссылки
    /// на «текущий» имплант: при извлечении одного из двух одинаковых ссылка переезжает на второй, а не протухает.
    /// </summary>
    public EntityUid? FindImplant<T>(EntityUid host) where T : IComponent
    {
        if (!TryComp<ImplantedComponent>(host, out var implanted))
            return null;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (HasComp<T>(implant))
                return implant;
        }

        return null;
    }

    public int CountImplants(EntityUid host, Func<EntityUid, bool> predicate)
    {
        if (!TryComp<ImplantedComponent>(host, out var implanted))
            return 0;

        var count = 0;
        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (predicate(implant))
                count++;
        }

        return count;
    }
}
