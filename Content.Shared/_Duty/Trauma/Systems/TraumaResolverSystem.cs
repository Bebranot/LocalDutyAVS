// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Random.Helpers;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Shared.Network;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Trauma.Systems;

/// <summary>
/// _Duty: агрегатор функциональных последствий переломов (движение, атака, боль, падения).
/// Отдельно от <see cref="FractureSystem"/> (состояние), чтобы позже сюда же подмешивались вывихи —
/// единая точка эффектов. Модификаторы движения/атаки идут через ванильные события (предсказаны),
/// а урон/падения/роняние — серверным тиком.
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

    /// <summary>Доля проваленных ударов сломанной рукой по тиру (эффект «−80% скорости атак»).</summary>
    private const float ArmFailCrack = 0.25f;
    private const float ArmFailFull = 0.8f;
    private const float ArmFailOpen = 0.9f;

    /// <summary>Шанс уронить предмет из сломанной руки за тик (Full+).</summary>
    private const float ArmDropChance = 0.15f;

    /// <summary>Урон за тик при движении со сломанной ногой.</summary>
    private const float LegMoveDamageFull = 0.5f;
    private const float LegMoveDamageOpen = 1.5f;

    /// <summary>Шанс упасть при движении с открытым переломом ноги за тик.</summary>
    private const float LegFallChanceOpen = 0.1f;

    /// <summary>Поддерживаемый уровень неартериального кровотечения при открытом переломе.</summary>
    private const float OpenBleed = 1.0f;

    public override void Initialize()
    {
        SubscribeLocalEvent<FractureComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<FractureComponent, AttackAttemptEvent>(OnAttackAttempt);
    }

    private void OnAttackAttempt(EntityUid uid, FractureComponent comp, AttackAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var armTier = GetWorstTier(comp, BodyZoneCategory.IsArm);
        var failChance = armTier switch
        {
            FractureTier.Crack => ArmFailCrack,
            FractureTier.Full => ArmFailFull,
            FractureTier.Open => ArmFailOpen,
            _ => 0f,
        };

        // Предсказуемый бросок (одинаков на клиенте и сервере), иначе промахи будут
        // рассинхронизироваться и «дёргать» атаку при предсказании.
        if (failChance > 0f && SharedRandomExtensions.PredictedProb(_timing, failChance, GetNetEntity(uid)))
            args.Cancel();
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
        var hasOpen = false;
        foreach (var state in comp.Zones.Values)
        {
            if (state.Tier >= FractureTier.Open)
                hasOpen = true;
        }

        // Нога: движение с полным/открытым переломом причиняет боль и урон, открытый — ещё и падения.
        if (legTier >= FractureTier.Full)
        {
            var pos = _xform.GetWorldPosition(uid);
            var moved = (pos - comp.LastPosition).Length() > MoveThreshold;
            comp.LastPosition = pos;

            if (moved)
            {
                var dmg = legTier >= FractureTier.Open ? LegMoveDamageOpen : LegMoveDamageFull;
                DealBlunt(uid, dmg);
                _popup.PopupEntity(Loc.GetString("trauma-fracture-leg-pain"), uid, uid, PopupType.SmallCaution);

                if (legTier >= FractureTier.Open && _random.Prob(LegFallChanceOpen))
                    _stun.TryKnockdown((uid, null), TimeSpan.FromSeconds(2), refresh: true, drop: false);
            }
        }

        // Рука: полный/открытый перелом временами роняет предмет из руки.
        if (armTier >= FractureTier.Full && _random.Prob(ArmDropChance))
        {
            var drop = new DropHandItemsEvent();
            RaiseLocalEvent(uid, ref drop);
        }

        // Открытый перелом — доп. неартериальное кровотечение (поддерживаем небольшой уровень).
        if (hasOpen)
            _bloodstream.TryModifyBleedAmount(uid, OpenBleed);
    }

    private void OnRefreshSpeed(EntityUid uid, FractureComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        var legTier = GetWorstTier(comp, BodyZoneCategory.IsLeg);

        switch (legTier)
        {
            case FractureTier.Crack:
                args.ModifySpeed(0.85f, 0.85f);
                break;
            case FractureTier.Full:
                args.ModifySpeed(0.55f, 0.5f);
                break;
            case FractureTier.Open:
                args.ModifySpeed(0.35f, 0.3f);
                break;
        }
    }

    /// <summary>Наибольший тир среди сломанных зон, подходящих под фильтр (null — таких нет).</summary>
    private static FractureTier? GetWorstTier(FractureComponent comp, Func<BodyZone, bool> filter)
    {
        FractureTier? worst = null;
        foreach (var (zone, state) in comp.Zones)
        {
            if (!filter(zone))
                continue;
            if (worst is null || state.Tier > worst)
                worst = state.Tier;
        }

        return worst;
    }

    private void DealBlunt(EntityUid uid, float amount)
    {
        var dmg = new DamageSpecifier();
        dmg.DamageDict.Add("Blunt", amount);
        _damageable.TryChangeDamage(uid, dmg, ignoreResistances: true, interruptsDoAfters: false);
    }
}
