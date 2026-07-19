// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma;
using Content.Shared._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.Events;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Trauma.Systems;

/// <summary>
/// _Duty: сам эффект артериального кровотечения (лечение — в отдельной системе). Серверный:
/// кровь авторитетна на сервере, а наложение приходит серверным <see cref="TraumaRolledEvent"/>.
///
/// Пока висит <see cref="ArterialBleedComponent"/>, система периодически поднимает bleed цели,
/// перекрывая естественный клоттинг — иначе ванильный <c>TickBleed</c> при <c>BleedAmount == 0</c>
/// просто перестал бы вызываться и кровотечение остановилось бы само (см. коммент в компоненте).
/// </summary>
public sealed class ArterialBleedSystem : EntitySystem
{
    [Dependency] private readonly SharedBloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<TraumaTargetComponent, TraumaRolledEvent>(OnTraumaRolled);
        SubscribeLocalEvent<ArterialBleedComponent, ComponentShutdown>(OnShutdown);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;

        var query = EntityQueryEnumerator<ArterialBleedComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextUpdate)
                continue;

            comp.NextUpdate = now + TimeSpan.FromSeconds(comp.UpdateIntervalSeconds);
            // TryModifyBleedAmount сам клампит на MaxBleedAmount, так что bleed «залипает» у потолка.
            _bloodstream.TryModifyBleedAmount(uid, comp.BleedTopUp);
        }
    }

    private void OnTraumaRolled(Entity<TraumaTargetComponent> ent, ref TraumaRolledEvent args)
    {
        if (args.Type != TraumaType.ArterialBleed)
            return;

        // Уже кровит — повторный ролл не складывается (снимается только лечением).
        if (HasComp<ArterialBleedComponent>(ent))
            return;

        // Без кровеносной системы кровить нечему (напр. IPC) — не вешаем бесполезный компонент.
        if (!HasComp<BloodstreamComponent>(ent))
            return;

        var comp = EnsureComp<ArterialBleedComponent>(ent);
        comp.NextUpdate = _timing.CurTime; // первая подкачка — сразу на следующем апдейте
    }

    private void OnShutdown(Entity<ArterialBleedComponent> ent, ref ComponentShutdown args)
    {
        // Снятие (лечение) резко гасит накопленный bleed — жгут останавливает быстро, а не за
        // десятки секунд естественного клоттинга.
        _bloodstream.TryModifyBleedAmount(ent.Owner, -ent.Comp.BleedTopUp * 3f);
    }
}
