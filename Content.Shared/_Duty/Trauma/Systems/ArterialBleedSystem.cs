// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.Events;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.HealthExaminable;
using Content.Shared.Rejuvenate;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Trauma.Systems;

/// <summary>
/// _Duty: эффект артериального кровотечения (лечение — в отдельной системе).
///
/// Тик крови авторитетен на сервере (гейт по <see cref="INetManager.IsServer"/>), а обработчик
/// осмотра здоровья — в shared, чтобы строка показывалась там, где строится examine.
///
/// Логика урона (см. коммент компонента): держим кровь чуть ниже ванильного порога кровопотери,
/// поэтому ванильный Bloodloss-урон идёт стабильно, а не разгоняется до мгновенной смерти от
/// нулевой крови.
/// </summary>
public sealed class ArterialBleedSystem : EntitySystem
{
    [Dependency] private readonly SharedBloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;

    /// <summary>
    /// Доп. доля DPS от кровопотери поверх ванильного расчёта (см. <c>Update</c>) — ×1.5 итого.
    /// Именно доп.-урон, а не ускорение вытекания крови: пользователь просил не трогать скорость,
    /// с которой падает объём крови, только итоговый урон в секунду.
    /// </summary>
    private const float ArterialBonusDamageMultiplier = 0.5f;

    public override void Initialize()
    {
        SubscribeLocalEvent<TraumaRolledEvent>(OnTraumaRolled);
        SubscribeLocalEvent<ArterialBleedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ArterialBleedComponent, HealthBeingExaminedEvent>(OnHealthExamined);
        SubscribeLocalEvent<ArterialBleedComponent, RejuvenateEvent>(OnRejuvenate);
    }

    private void OnRejuvenate(Entity<ArterialBleedComponent> ent, ref RejuvenateEvent args)
    {
        // Полное исцеление снимает артерию (иначе она снова закровит после долива крови ванилью).
        RemComp<ArterialBleedComponent>(ent);
    }

    public override void Update(float frameTime)
    {
        // Кровь авторитетна на сервере — тик крутим только там.
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ArterialBleedComponent, BloodstreamComponent>();
        while (query.MoveNext(out var uid, out var comp, out var blood))
        {
            if (now < comp.NextUpdate)
                continue;
            comp.NextUpdate = now + TimeSpan.FromSeconds(comp.UpdateIntervalSeconds);

            // Пока крови больше пола — поддерживаем небольшую скорость кровотечения (медленная
            // утечка). У пола перестаём — объём крови стабилизируется, в ноль не уходит.
            var bloodLevel = _bloodstream.GetBloodLevel(uid);
            if (bloodLevel <= comp.BloodFloor)
                continue;

            if (blood.BleedAmount < comp.BleedTarget)
                _bloodstream.TryModifyBleedAmount(uid, comp.BleedTarget - blood.BleedAmount);

            // Доп.-урон поверх ванильного Bloodloss-DPS (см. SharedBloodstreamSystem.Update) —
            // то же условие и та же формула, что и у ваниль, только домноженная на нашу долю.
            // Скорость вытекания крови (выше) этим не затрагивается.
            if (bloodLevel < blood.BloodlossThreshold)
            {
                var extra = blood.BloodlossDamage / (0.1f + bloodLevel) * ArterialBonusDamageMultiplier;
                _damageable.TryChangeDamage(uid, extra, ignoreResistances: false, interruptsDoAfters: false);
            }
        }
    }

    private void OnTraumaRolled(TraumaRolledEvent args)
    {
        if (args.Type != TraumaType.ArterialBleed)
            return;

        var target = args.Target;

        // Уже кровит — повторный ролл не складывается (снимается только лечением).
        if (HasComp<ArterialBleedComponent>(target))
            return;

        // Без кровеносной системы кровить нечему (напр. IPC) — не вешаем бесполезный компонент.
        if (!HasComp<BloodstreamComponent>(target))
            return;

        var comp = EnsureComp<ArterialBleedComponent>(target);
        comp.NextUpdate = _timing.CurTime;
    }

    private void OnShutdown(Entity<ArterialBleedComponent> ent, ref ComponentShutdown args)
    {
        // Лечение: гасим кровотечение; кровь и Bloodloss-урон восстановятся сами со временем.
        if (_net.IsServer)
            _bloodstream.TryModifyBleedAmount(ent.Owner, -100f);
    }

    private void OnHealthExamined(Entity<ArterialBleedComponent> ent, ref HealthBeingExaminedEvent args)
    {
        args.Message.PushNewline();
        args.Message.AddMarkupOrThrow(Loc.GetString("trauma-examine-arterial"));
    }
}
