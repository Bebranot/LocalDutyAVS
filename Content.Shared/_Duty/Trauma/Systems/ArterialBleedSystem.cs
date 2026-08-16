// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.Events;
using Content.Shared.Alert;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.HealthExaminable;
using Content.Shared.Rejuvenate;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Trauma.Systems;

/// <summary>
/// _Duty: эффект артериального кровотечения (лечение — в отдельной системе).
///
/// Тик крови авторитетен на сервере (гейт по <see cref="INetManager.IsServer"/>), а обработчик
/// осмотра здоровья — в shared, чтобы строка показывалась там, где строится examine.
///
/// Урон наносим САМИ, напрямую (<see cref="ArterialBloodlossDamagePerSecond"/>) — ванильный
/// авто-тик Bloodloss (в <c>SharedBloodstreamSystem.Update</c>) зависит от порога 90% крови и
/// на практике почти не срабатывает при нашей модели (кровь стабилизируется у пола, см. ниже).
/// Объём крови при этом падает медленно и не уходит в ноль (держим у пола, а не до смерти).
/// </summary>
public sealed class ArterialBleedSystem : EntitySystem
{
    [Dependency] private readonly SharedBloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;

    /// <summary>Алерт-иконка (по типу HP/стамины) — показывается, пока активно кровотечение.</summary>
    private static readonly ProtoId<AlertPrototype> ArteryAlert = "DutyArtery";

    /// <summary>
    /// Прямой урон Bloodloss в секунду от артерии, НЕ завязанный на ванильный авто-тик
    /// (<c>SharedBloodstreamSystem.Update</c>) — тот применяет урон только пока % крови ниже
    /// <c>BloodlossThreshold</c> (90%) и на практике почти не срабатывает при нашей модели
    /// стабилизации крови у пола. Скорость вытекания крови (<c>BleedAmount</c>/<c>BleedTarget</c>/
    /// <c>BloodFloor</c> ниже) этим не затрагивается — только сам урон.
    /// </summary>
    private const float ArterialBloodlossDamagePerSecond = 0.75f;

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

            // Прямой урон Bloodloss — не зависит от ванильного порога/автотика и не зависит от
            // пола крови ниже: боль должна идти постоянно, пока кровотечение активно.
            var dmg = new DamageSpecifier();
            dmg.DamageDict.Add("Bloodloss", ArterialBloodlossDamagePerSecond * comp.UpdateIntervalSeconds);
            _damageable.TryChangeDamage(uid, dmg, ignoreResistances: false, interruptsDoAfters: false);

            // Пока крови больше пола — поддерживаем небольшую скорость кровотечения (медленная
            // утечка). У пола перестаём — объём крови стабилизируется, в ноль не уходит.
            if (_bloodstream.GetBloodLevel(uid) <= comp.BloodFloor)
                continue;

            if (blood.BleedAmount < comp.BleedTarget)
                _bloodstream.TryModifyBleedAmount(uid, comp.BleedTarget - blood.BleedAmount);
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
        _alerts.ShowAlert(target, ArteryAlert);
    }

    private void OnShutdown(Entity<ArterialBleedComponent> ent, ref ComponentShutdown args)
    {
        // Снимаем алерт при удалении компонента любым путём (лечение/реджувенейт/смерть).
        _alerts.ClearAlert(ent.Owner, ArteryAlert);

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
