// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma.Components;

namespace Content.Shared._Duty.Trauma.Systems;

/// <summary>
/// _Duty: сборка списка травм для анализатора здоровья. Вынесено сюда, чтобы вендорный
/// HealthAnalyzerSystem не знал внутренностей травм — он лишь дёргает один метод.
/// </summary>
public sealed class TraumaAnalyzerSystem : EntitySystem
{
    /// <summary>
    /// Возвращает список травм существа для вывода в анализаторе, или null если травм нет.
    /// </summary>
    public List<TraumaAnalyzerEntry>? GetEntries(EntityUid uid)
    {
        List<TraumaAnalyzerEntry>? entries = null;

        if (TryComp<FractureComponent>(uid, out var fracture))
        {
            foreach (var (zone, state) in fracture.Zones)
            {
                (entries ??= new()).Add(
                    new TraumaAnalyzerEntry(TraumaAnalyzerKind.Fracture, zone, state.Tier, state.Splinted));
            }
        }

        if (TryComp<DislocationComponent>(uid, out var dislocation))
        {
            foreach (var zone in dislocation.Dislocated)
            {
                (entries ??= new()).Add(new TraumaAnalyzerEntry(TraumaAnalyzerKind.Dislocation, zone));
            }

            foreach (var zone in dislocation.Residual.Keys)
            {
                (entries ??= new()).Add(new TraumaAnalyzerEntry(TraumaAnalyzerKind.DislocationResidual, zone));
            }
        }

        if (HasComp<ArterialBleedComponent>(uid))
            (entries ??= new()).Add(new TraumaAnalyzerEntry(TraumaAnalyzerKind.ArterialBleed));

        return entries;
    }
}
