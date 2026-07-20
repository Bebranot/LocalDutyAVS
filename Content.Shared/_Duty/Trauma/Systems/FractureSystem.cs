// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.Events;
using Content.Shared.HealthExaminable;
using Content.Shared.Movement.Systems;
using Content.Shared.Rejuvenate;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Trauma.Systems;

/// <summary>
/// _Duty: переломы — состояние, эскалация и пассивное заживление (функциональные дебаффы и
/// шинирование — в отдельных системах). Наложение приходит серверным <see cref="TraumaRolledEvent"/>,
/// пассивный тик заживления крутится на сервере, а осмотр здоровья — в shared.
/// </summary>
public sealed class FractureSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    // ── Тюнинг (Phase 6). ──────────────────────────────────────────────────────

    /// <summary>Через сколько без шины тир снижается на один шаг (медленно).</summary>
    private static readonly TimeSpan UnsplintedHealStep = TimeSpan.FromMinutes(5);

    /// <summary>Через сколько с шиной тир снижается на один шаг (быстрее).</summary>
    private static readonly TimeSpan SplintedHealStep = TimeSpan.FromMinutes(2);

    public override void Initialize()
    {
        SubscribeLocalEvent<TraumaRolledEvent>(OnTraumaRolled);
        SubscribeLocalEvent<FractureComponent, HealthBeingExaminedEvent>(OnHealthExamined);
        SubscribeLocalEvent<FractureComponent, RejuvenateEvent>(OnRejuvenate);
    }

    private void OnRejuvenate(Entity<FractureComponent> ent, ref RejuvenateEvent args)
    {
        RemComp<FractureComponent>(ent);
        _movementSpeed.RefreshMovementSpeedModifiers(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        // Пассивное заживление — серверная авторитетная логика.
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        List<EntityUid>? toRemove = null;

        var query = EntityQueryEnumerator<FractureComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var changed = false;

            foreach (var zone in comp.Zones.Keys.ToArray())
            {
                var state = comp.Zones[zone];
                if (now < state.NextHeal)
                    continue;

                // Трещина зажила полностью — убираем зону; иначе снижаем тир на шаг.
                if (state.Tier <= FractureTier.Crack)
                {
                    comp.Zones.Remove(zone);
                }
                else
                {
                    state.Tier = Lower(state.Tier);
                    state.NextHeal = now + HealStep(state);
                    comp.Zones[zone] = state;
                }

                changed = true;
            }

            if (!changed)
                continue;

            // Удаление компонента откладываем — нельзя менять структуру во время перебора запроса.
            if (comp.Zones.Count == 0)
                (toRemove ??= new()).Add(uid);
            else
                Dirty(uid, comp);

            _movementSpeed.RefreshMovementSpeedModifiers(uid);
        }

        if (toRemove is null)
            return;

        foreach (var uid in toRemove)
            RemComp<FractureComponent>(uid);
    }

    private void OnTraumaRolled(TraumaRolledEvent args)
    {
        if (args.Type != TraumaType.Fracture || args.Zone is not { } zone)
            return;

        var target = args.Target;
        var isNew = !HasComp<FractureComponent>(target);
        var comp = EnsureComp<FractureComponent>(target);

        // Инициализируем позицию для «урона при ходьбе», чтобы первый тик не сработал ложно.
        if (isNew)
            comp.LastPosition = _transform.GetWorldPosition(target);

        if (comp.Zones.TryGetValue(zone, out var state))
        {
            // Повторный удар по сломанной зоне — эскалация тира (шина при этом слетает).
            state.Tier = Raise(state.Tier);
            state.Splinted = false;
        }
        else
        {
            state = new FractureZoneState { Tier = FractureTier.Crack };
        }

        state.NextHeal = _timing.CurTime + HealStep(state);
        comp.Zones[zone] = state;
        Dirty(target, comp);
        _movementSpeed.RefreshMovementSpeedModifiers(target);
    }

    private void OnHealthExamined(Entity<FractureComponent> ent, ref HealthBeingExaminedEvent args)
    {
        foreach (var (zone, state) in ent.Comp.Zones)
        {
            args.Message.PushNewline();
            args.Message.AddMarkupOrThrow(Loc.GetString(
                state.Splinted ? "trauma-examine-fracture-splinted" : "trauma-examine-fracture",
                ("zone", Loc.GetString(TraumaLoc.ZoneKey(zone))),
                ("tier", Loc.GetString(TraumaLoc.FractureTierKey(state.Tier)))));
        }
    }

    /// <summary>Есть ли хотя бы одна сломанная, но ещё не зашинированная зона.</summary>
    public bool HasUnsplintedFracture(EntityUid uid) =>
        TryComp<FractureComponent>(uid, out var comp) && comp.Zones.Values.Any(z => !z.Splinted);

    /// <summary>
    /// Накладывает шину на самую тяжёлую ещё не зашинированную зону (стабилизация + ускоренное
    /// сращивание). false — если шинировать нечего.
    /// </summary>
    public bool TrySplintWorstZone(EntityUid uid)
    {
        if (!TryComp<FractureComponent>(uid, out var comp))
            return false;

        BodyZone? best = null;
        var bestTier = FractureTier.Crack;
        foreach (var (zone, state) in comp.Zones)
        {
            if (state.Splinted)
                continue;
            if (best is null || state.Tier > bestTier)
            {
                best = zone;
                bestTier = state.Tier;
            }
        }

        if (best is not { } target)
            return false;

        var st = comp.Zones[target];
        st.Splinted = true;
        st.NextHeal = _timing.CurTime + HealStep(st);
        comp.Zones[target] = st;
        Dirty(uid, comp);
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
        return true;
    }

    private TimeSpan HealStep(FractureZoneState state) =>
        state.Splinted ? SplintedHealStep : UnsplintedHealStep;

    private static FractureTier Raise(FractureTier tier) =>
        tier >= FractureTier.Open ? FractureTier.Open : (FractureTier)((byte)tier + 1);

    private static FractureTier Lower(FractureTier tier) =>
        tier <= FractureTier.Crack ? FractureTier.Crack : (FractureTier)((byte)tier - 1);

}
