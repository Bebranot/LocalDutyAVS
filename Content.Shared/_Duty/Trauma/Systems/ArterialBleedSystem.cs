// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.Events;
using Content.Shared.Body.Events;
using Content.Shared.Body.Systems;

namespace Content.Shared._Duty.Trauma.Systems;

/// <summary>
/// _Duty: сам эффект артериального кровотечения (без лечения — оно в отдельной системе).
///
/// Обработчик <see cref="BleedModifierEvent"/> живёт в shared, потому что тик bleed'а
/// предсказывается на клиенте — доп. отток должен считаться одинаково на обеих сторонах.
/// Наложение травмы приходит серверным <see cref="TraumaRolledEvent"/> (роллер авторитетен
/// по RNG), поэтому этот хендлер де-факто выполняется только на сервере, а компонент
/// разъезжается на клиент сетевым состоянием.
/// </summary>
public sealed class ArterialBleedSystem : EntitySystem
{
    [Dependency] private readonly SharedBloodstreamSystem _bloodstream = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ArterialBleedComponent, BleedModifierEvent>(OnBleedModifier);
        SubscribeLocalEvent<TraumaTargetComponent, TraumaRolledEvent>(OnTraumaRolled);
    }

    private void OnBleedModifier(Entity<ArterialBleedComponent> ent, ref BleedModifierEvent args)
    {
        args.BleedAmount += ent.Comp.ExtraBleedPerTick;
    }

    private void OnTraumaRolled(Entity<TraumaTargetComponent> ent, ref TraumaRolledEvent args)
    {
        if (args.Type != TraumaType.ArterialBleed)
            return;

        // Уже кровит — повторный ролл не складывает эффект (снимается только лечением).
        if (HasComp<ArterialBleedComponent>(ent))
            return;

        var comp = EnsureComp<ArterialBleedComponent>(ent);
        _bloodstream.TryModifyBleedAmount(ent.Owner, comp.InitialBleedSpike);
    }
}
