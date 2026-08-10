// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.Events;
using Content.Shared.HealthExaminable;
using Content.Shared.Medical;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
using Content.Shared.Stunnable;
using Robust.Shared.Network;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Trauma.Systems;

/// <summary>
/// _Duty: сотрясение мозга (НЕ аудио-контузия из <c>_Duty/Concussion</c> — см. коммент компонента).
/// Запускается травмой головы, лечится только временем; повторный удар до восстановления
/// эскалирует нелинейно. Восстановление и симптомы крутятся на сервере, штрафы движения и осмотр —
/// в shared.
/// </summary>
public sealed class BrainTraumaSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly VomitSystem _vomit = default!;

    // ── Тюнинг (Phase 6). ──────────────────────────────────────────────────────

    /// <summary>Сколько держится тир до снижения на шаг.</summary>
    private static readonly TimeSpan LightRecovery = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MediumRecovery = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SevereRecovery = TimeSpan.FromMinutes(10);

    /// <summary>Окно «второго удара»: повторная травма головы внутри него бьёт вдвойне.</summary>
    private static readonly TimeSpan SecondImpactWindow = TimeSpan.FromSeconds(60);

    /// <summary>Период тика симптомов.</summary>
    private static readonly TimeSpan EffectInterval = TimeSpan.FromSeconds(10);

    /// <summary>Шанс за тик словить симптом (дезориентация — попап без рвоты) на среднем и выше.</summary>
    private const float SymptomChance = 0.35f;

    /// <summary>Шанс за тик реально вырвать (отдельно от симптома) на среднем и выше.</summary>
    private const float VomitChance = 0.12f;

    /// <summary>Шанс за тик кратковременно отключиться при тяжёлом сотрясении.</summary>
    private const float BlackoutChance = 0.15f;

    private static readonly TimeSpan BlackoutTime = TimeSpan.FromSeconds(3);

    public override void Initialize()
    {
        SubscribeLocalEvent<TraumaRolledEvent>(OnTraumaRolled);
        SubscribeLocalEvent<HeadTraumaComponent, HealthBeingExaminedEvent>(OnHealthExamined);
        SubscribeLocalEvent<HeadTraumaComponent, RejuvenateEvent>(OnRejuvenate);
    }

    public override void Update(float frameTime)
    {
        // Восстановление и симптомы — серверная авторитетная логика.
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        List<EntityUid>? toRemove = null;

        var query = EntityQueryEnumerator<HeadTraumaComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            TickSymptoms(uid, comp, now);

            if (now < comp.NextRecovery)
                continue;

            // Лёгкое сотрясение проходит совсем; иначе снижаем тяжесть на шаг.
            if (comp.Tier <= HeadTraumaTier.Light)
            {
                // Удаление откладываем — нельзя менять структуру во время перебора запроса.
                (toRemove ??= new()).Add(uid);
                continue;
            }

            comp.Tier = Lower(comp.Tier);
            comp.NextRecovery = now + RecoveryStep(comp.Tier);
            Dirty(uid, comp);
            _movementSpeed.RefreshMovementSpeedModifiers(uid);
        }

        if (toRemove is null)
            return;

        foreach (var uid in toRemove)
        {
            RemComp<HeadTraumaComponent>(uid);
            _movementSpeed.RefreshMovementSpeedModifiers(uid);
        }
    }

    private void TickSymptoms(EntityUid uid, HeadTraumaComponent comp, TimeSpan now)
    {
        if (now < comp.NextEffectTick)
            return;

        comp.NextEffectTick = now + EffectInterval;

        // Тяжёлое — риск кратковременной отключки.
        if (comp.Tier >= HeadTraumaTier.Severe && _random.Prob(BlackoutChance))
        {
            _popup.PopupEntity(Loc.GetString("trauma-head-blackout"), uid, uid, PopupType.LargeCaution);
            _stun.TryKnockdown((uid, null), BlackoutTime, refresh: true, drop: true);
            return;
        }

        // Среднее и выше — тошнота волнами (реальная рвота, не только попап) и дезориентация.
        if (comp.Tier < HeadTraumaTier.Medium)
            return;

        if (_random.Prob(VomitChance))
        {
            _vomit.Vomit(uid);
            return;
        }

        if (_random.Prob(SymptomChance))
            _popup.PopupEntity(Loc.GetString("trauma-head-symptom"), uid, uid, PopupType.MediumCaution);
    }

    private void OnTraumaRolled(TraumaRolledEvent args)
    {
        // Сотрясение не роллится напрямую — его запускает травма головы.
        if (args.Type != TraumaType.Fracture || args.Zone != BodyZone.Head)
            return;

        ApplyHeadTrauma(args.Target);
    }

    /// <summary>
    /// Наносит/усугубляет сотрясение. Повторная травма внутри окна «второго удара» поднимает
    /// тяжесть сразу на два шага.
    /// </summary>
    public void ApplyHeadTrauma(EntityUid uid)
    {
        var now = _timing.CurTime;
        var isNew = !HasComp<HeadTraumaComponent>(uid);
        var comp = EnsureComp<HeadTraumaComponent>(uid);

        if (isNew)
        {
            comp.Tier = HeadTraumaTier.Light;
        }
        else
        {
            var steps = now < comp.SecondImpactUntil ? 2 : 1;
            comp.Tier = Raise(comp.Tier, steps);
        }

        comp.SecondImpactUntil = now + SecondImpactWindow;
        comp.NextRecovery = now + RecoveryStep(comp.Tier);
        comp.NextEffectTick = now + EffectInterval;

        Dirty(uid, comp);
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
        _popup.PopupEntity(Loc.GetString("trauma-head-received"), uid, uid, PopupType.MediumCaution);
    }

    private void OnHealthExamined(Entity<HeadTraumaComponent> ent, ref HealthBeingExaminedEvent args)
    {
        args.Message.PushNewline();
        args.Message.AddMarkupOrThrow(Loc.GetString(
            "trauma-examine-head",
            ("tier", Loc.GetString(TraumaLoc.HeadTraumaTierKey(ent.Comp.Tier)))));
    }

    private void OnRejuvenate(Entity<HeadTraumaComponent> ent, ref RejuvenateEvent args)
    {
        RemComp<HeadTraumaComponent>(ent);
        _movementSpeed.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private static TimeSpan RecoveryStep(HeadTraumaTier tier) => tier switch
    {
        HeadTraumaTier.Light => LightRecovery,
        HeadTraumaTier.Medium => MediumRecovery,
        HeadTraumaTier.Severe => SevereRecovery,
        _ => LightRecovery,
    };

    private static HeadTraumaTier Raise(HeadTraumaTier tier, int steps)
    {
        var raised = (int)tier + steps;
        return raised >= (int)HeadTraumaTier.Severe ? HeadTraumaTier.Severe : (HeadTraumaTier)raised;
    }

    private static HeadTraumaTier Lower(HeadTraumaTier tier) =>
        tier <= HeadTraumaTier.Light ? HeadTraumaTier.Light : (HeadTraumaTier)((byte)tier - 1);
}
