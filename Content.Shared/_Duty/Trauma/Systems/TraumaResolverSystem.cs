// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.Events;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Random.Helpers;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Network;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Trauma.Systems;

/// <summary>
/// _Duty: единый агрегатор функциональных последствий ВСЕХ травм (переломы, вывихи, сотрясение).
///
/// Ключевая причина, почему это один обработчик, а не по одному на компонент: независимые
/// обработчики перемножали бы штрафы (сломанная нога × вывих ноги × сотрясение ≈ 12% скорости),
/// то есть две-три травмы делали бы персонажа полностью недееспособным. Здесь штрафы собираются
/// вместе и складываются с диминишингом (худший — в полную силу, остальные — вполовину) и общим
/// потолком, поэтому травмы всегда ощутимы, но не превращаются в мгновенный soft-lock.
/// </summary>
public sealed class TraumaResolverSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedBloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    // ── Тюнинг (Phase 6). ──────────────────────────────────────────────────────
    private static readonly TimeSpan EffectInterval = TimeSpan.FromSeconds(1);
    private const float MoveThreshold = 0.1f;

    /// <summary>Вес каждого штрафа кроме самого тяжёлого (диминишинг при стакинге травм).</summary>
    private const float SecondarySourceWeight = 0.5f;

    /// <summary>Максимальная суммарная потеря скорости (0.7 → минимум 30% скорости).</summary>
    private const float MaxMovePenalty = 0.7f;

    /// <summary>Максимальная суммарная доля проваленных ударов (всегда остаётся шанс ударить).</summary>
    private const float MaxAttackFail = 0.9f;

    // Потеря скорости по источникам.
    private const float LegFractureCrackPenalty = 0.15f;
    private const float LegFractureFullPenalty = 0.45f;
    private const float LegFractureOpenPenalty = 0.65f;
    private const float LegDislocPenalty = 0.5f;
    private const float LegResidualPenalty = 0.15f;
    private const float HeadLightPenalty = 0.05f;
    private const float HeadMediumPenalty = 0.15f;
    private const float HeadSeverePenalty = 0.3f;

    // Доля проваленных ударов по источникам.
    private const float ArmFractureCrackFail = 0.25f;
    private const float ArmFractureFullFail = 0.8f;
    private const float ArmFractureOpenFail = 0.9f;
    private const float ArmDislocFail = 0.85f;
    private const float ArmResidualFail = 0.25f;

    /// <summary>Шанс уронить предмет из сломанной руки за тик (Full+).</summary>
    private const float ArmDropChance = 0.15f;

    /// <summary>Урон за тик при движении со сломанной ногой.</summary>
    private const float LegMoveDamageFull = 0.5f;
    private const float LegMoveDamageOpen = 1.5f;

    /// <summary>Шанс упасть при движении с открытым переломом ноги за тик.</summary>
    private const float LegFallChanceOpen = 0.1f;

    /// <summary>Поддерживаемый (не превышаемый) уровень неартериального кровотечения при открытом переломе.</summary>
    private const float OpenBleedTarget = 1.5f;

    /// <summary>Ниже этой доли крови открытый перелом перестаёт кровить (чтобы не осушать в ноль).</summary>
    private const float OpenBleedBloodFloor = 0.5f;

    /// <summary>Множитель интервала кашля кровью (BloodCoughSystem) при переломе торса по тиру.</summary>
    private const float TorsoCoughCrackMultiplier = 0.85f;
    private const float TorsoCoughFullMultiplier = 0.55f;
    private const float TorsoCoughOpenMultiplier = 0.3f;

    /// <summary>Штраф урона ближним оружием за каждую сломанную руку (линейный, не диминишинг).</summary>
    private const float ArmFractureMeleeDamagePenalty = 0.15f;

    public override void Initialize()
    {
        // Подписка на общем маркере травмируемого существа — чтобы штрафы всех травм считались
        // в одном месте и агрегировались, а не перемножались независимо.
        SubscribeLocalEvent<TraumaTargetComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<TraumaTargetComponent, AttackAttemptEvent>(OnAttackAttempt);

        // Торс: перелом учащает кашель кровью (BloodCoughSystem, ADT) — обратный канал событием,
        // чтобы не тянуть Content.Server из Content.Shared (см. коммент события).
        SubscribeLocalEvent<FractureComponent, BloodCoughIntervalModifierEvent>(OnBloodCoughModifier);

        // GetMeleeDamageEvent поднимается на ОРУЖИИ, а не на атакующем (см. User в событии) —
        // без фильтра по компоненту, чтобы поймать его вне зависимости от того, чем бьют.
        SubscribeLocalEvent<GetMeleeDamageEvent>(OnGetMeleeDamage);
    }

    /// <summary>
    /// Перелом руки бьёт по урону ближним оружием, а не только по шансу промаха (тот считается
    /// отдельно в <see cref="OnAttackAttempt"/>). Стакинг тут намеренно линейный, а не диминишинг:
    /// две сломанные руки = -30% урона, обе половины страдают независимо.
    /// </summary>
    private void OnGetMeleeDamage(ref GetMeleeDamageEvent args)
    {
        if (!TryComp<FractureComponent>(args.User, out var fracture))
            return;

        var brokenArms = 0;
        if (fracture.Zones.TryGetValue(BodyZone.LeftArm, out var left) && left.GetEffectiveTier() is not null)
            brokenArms++;
        if (fracture.Zones.TryGetValue(BodyZone.RightArm, out var right) && right.GetEffectiveTier() is not null)
            brokenArms++;

        if (brokenArms == 0)
            return;

        args.Damage *= 1f - ArmFractureMeleeDamagePenalty * brokenArms;
    }

    private void OnBloodCoughModifier(EntityUid uid, FractureComponent comp, ref BloodCoughIntervalModifierEvent args)
    {
        if (!comp.Zones.TryGetValue(BodyZone.Torso, out var state) || state.GetEffectiveTier() is not { } tier)
            return;

        args.Multiplier *= tier switch
        {
            FractureTier.Crack => TorsoCoughCrackMultiplier,
            FractureTier.Full => TorsoCoughFullMultiplier,
            FractureTier.Open => TorsoCoughOpenMultiplier,
            _ => 1f,
        };
    }

    private void OnRefreshSpeed(EntityUid uid, TraumaTargetComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        var max = 0f;
        var sum = 0f;

        if (TryComp<FractureComponent>(uid, out var fracture))
        {
            AddPenalty(ref max, ref sum, GetWorstTier(fracture, BodyZoneCategory.IsLeg) switch
            {
                FractureTier.Crack => LegFractureCrackPenalty,
                FractureTier.Full => LegFractureFullPenalty,
                FractureTier.Open => LegFractureOpenPenalty,
                _ => 0f,
            });
        }

        if (TryComp<DislocationComponent>(uid, out var dislocation))
        {
            AddPenalty(ref max, ref sum,
                dislocation.Dislocated.Any(BodyZoneCategory.IsLeg) ? LegDislocPenalty :
                dislocation.Residual.Keys.Any(BodyZoneCategory.IsLeg) ? LegResidualPenalty : 0f);
        }

        if (TryComp<HeadTraumaComponent>(uid, out var head))
        {
            AddPenalty(ref max, ref sum, head.Tier switch
            {
                HeadTraumaTier.Light => HeadLightPenalty,
                HeadTraumaTier.Medium => HeadMediumPenalty,
                HeadTraumaTier.Severe => HeadSeverePenalty,
                _ => 0f,
            });
        }

        var penalty = Combine(max, sum, MaxMovePenalty);
        if (penalty <= 0f)
            return;

        var modifier = 1f - penalty;
        args.ModifySpeed(modifier, modifier);
    }

    private void OnAttackAttempt(EntityUid uid, TraumaTargetComponent comp, AttackAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var max = 0f;
        var sum = 0f;

        if (TryComp<FractureComponent>(uid, out var fracture))
        {
            AddPenalty(ref max, ref sum, GetWorstTier(fracture, BodyZoneCategory.IsArm) switch
            {
                FractureTier.Crack => ArmFractureCrackFail,
                FractureTier.Full => ArmFractureFullFail,
                FractureTier.Open => ArmFractureOpenFail,
                _ => 0f,
            });
        }

        if (TryComp<DislocationComponent>(uid, out var dislocation))
        {
            AddPenalty(ref max, ref sum,
                dislocation.Dislocated.Any(BodyZoneCategory.IsArm) ? ArmDislocFail :
                dislocation.Residual.Keys.Any(BodyZoneCategory.IsArm) ? ArmResidualFail : 0f);
        }

        var failChance = Combine(max, sum, MaxAttackFail);
        if (failChance <= 0f)
            return;

        // Предсказуемый бросок (одинаков на клиенте и сервере), иначе промахи будут
        // рассинхронизироваться и «дёргать» атаку при предсказании.
        if (SharedRandomExtensions.PredictedProb(_timing, failChance, GetNetEntity(uid)))
            args.Cancel();
    }

    /// <summary>Учитывает один штраф в агрегате: копит сумму и запоминает максимум.</summary>
    private static void AddPenalty(ref float max, ref float sum, float penalty)
    {
        if (penalty <= 0f)
            return;

        sum += penalty;
        if (penalty > max)
            max = penalty;
    }

    /// <summary>
    /// Складывает штрафы с диминишингом: самый тяжёлый — в полную силу, остальные — с половинным
    /// весом, итог ограничен потолком. Так несколько травм всегда хуже одной, но не отключают
    /// персонажа полностью.
    /// </summary>
    private static float Combine(float max, float sum, float cap)
    {
        if (max <= 0f)
            return 0f;

        return Math.Min(max + SecondarySourceWeight * (sum - max), cap);
    }

    public override void Update(float frameTime)
    {
        // Урон/падения/роняние — серверная авторитетная логика.
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;

        var query = EntityQueryEnumerator<FractureComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextEffectTick)
                continue;
            comp.NextEffectTick = now + EffectInterval;

            TickFractureEffects((uid, comp));
        }
    }

    private void TickFractureEffects(Entity<FractureComponent> ent)
    {
        var (uid, comp) = ent;

        var legTier = GetWorstTier(comp, BodyZoneCategory.IsLeg);
        var armTier = GetWorstTier(comp, BodyZoneCategory.IsArm);

        // ЭФФЕКТИВНЫЙ тир (учитывает шину) — иначе шинированный открытый перелом кровил бы вечно,
        // хотя шина по дизайну «останавливает ухудшение» так же, как и для движения/атаки.
        var hasOpen = false;
        foreach (var state in comp.Zones.Values)
        {
            if (state.GetEffectiveTier() >= FractureTier.Open)
                hasOpen = true;
        }

        // Отслеживаем перемещение всегда (иначе позиция устареет между тирами).
        var pos = _xform.GetWorldPosition(uid);
        var moved = (pos - comp.LastPosition).Length() > MoveThreshold;
        comp.LastPosition = pos;

        // Нога: движение с полным/открытым переломом причиняет боль и урон, открытый — ещё и падения.
        if (legTier >= FractureTier.Full && moved)
        {
            var dmg = legTier >= FractureTier.Open ? LegMoveDamageOpen : LegMoveDamageFull;
            DealBlunt(uid, dmg);
            _popup.PopupEntity(Loc.GetString("trauma-fracture-leg-pain"), uid, uid, PopupType.SmallCaution);

            if (legTier >= FractureTier.Open && _random.Prob(LegFallChanceOpen))
                _stun.TryKnockdown((uid, null), TimeSpan.FromSeconds(2), refresh: true, drop: false);
        }

        // Рука: полный/открытый перелом временами роняет предмет из руки.
        if (armTier >= FractureTier.Full && _random.Prob(ArmDropChance))
        {
            var drop = new DropHandItemsEvent();
            RaiseLocalEvent(uid, ref drop);
        }

        // Открытый перелом — доп. неартериальное кровотечение: держим низкий целевой уровень и не
        // ниже пола крови, чтобы не осушать пациента в ноль (как это было бы при слепой подкачке).
        if (hasOpen
            && TryComp<BloodstreamComponent>(uid, out var blood)
            && _bloodstream.GetBloodLevel(uid) > OpenBleedBloodFloor
            && blood.BleedAmount < OpenBleedTarget)
        {
            _bloodstream.TryModifyBleedAmount(uid, OpenBleedTarget - blood.BleedAmount);
        }
    }

    /// <summary>
    /// Наибольший ЭФФЕКТИВНЫЙ тир среди зон под фильтр (null — таких нет). Шина снижает
    /// функциональную тяжесть на тир (трещина в шине эффекта не даёт).
    /// </summary>
    private static FractureTier? GetWorstTier(FractureComponent comp, Func<BodyZone, bool> filter)
    {
        FractureTier? worst = null;
        foreach (var (zone, state) in comp.Zones)
        {
            if (!filter(zone))
                continue;

            if (state.GetEffectiveTier() is not { } tier)
                continue;

            if (worst is null || tier > worst)
                worst = tier;
        }

        return worst;
    }

    private void DealBlunt(EntityUid uid, float amount)
    {
        var dmg = new DamageSpecifier();
        dmg.DamageDict.Add("Blunt", amount);
        // origin = сам моб: этот «боль при ходьбе» урон не должен провоцировать новую травму.
        _damageable.TryChangeDamage(uid, dmg, ignoreResistances: true, interruptsDoAfters: false, origin: uid);
    }
}
