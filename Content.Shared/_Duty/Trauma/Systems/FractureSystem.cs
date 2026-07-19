// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.Events;
using Content.Shared.HealthExaminable;
using Content.Shared.Movement.Systems;
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

    // ── Тюнинг (Phase 6). ──────────────────────────────────────────────────────

    /// <summary>Через сколько без шины тир снижается на один шаг (медленно).</summary>
    private static readonly TimeSpan UnsplintedHealStep = TimeSpan.FromMinutes(5);

    /// <summary>Через сколько с шиной тир снижается на один шаг (быстрее).</summary>
    private static readonly TimeSpan SplintedHealStep = TimeSpan.FromMinutes(2);

    public override void Initialize()
    {
        SubscribeLocalEvent<TraumaTargetComponent, TraumaRolledEvent>(OnTraumaRolled);
        SubscribeLocalEvent<FractureComponent, HealthBeingExaminedEvent>(OnHealthExamined);
    }

    public override void Update(float frameTime)
    {
        // Пассивное заживление — серверная авторитетная логика.
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;

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

            if (comp.Zones.Count == 0)
                RemComp<FractureComponent>(uid);
            else if (changed)
                Dirty(uid, comp);

            if (changed)
                _movementSpeed.RefreshMovementSpeedModifiers(uid);
        }
    }

    private void OnTraumaRolled(Entity<TraumaTargetComponent> ent, ref TraumaRolledEvent args)
    {
        if (args.Type != TraumaType.Fracture || args.Zone is not { } zone)
            return;

        var comp = EnsureComp<FractureComponent>(ent);

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
        Dirty(ent.Owner, comp);
        _movementSpeed.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnHealthExamined(Entity<FractureComponent> ent, ref HealthBeingExaminedEvent args)
    {
        foreach (var (zone, state) in ent.Comp.Zones)
        {
            args.Message.PushNewline();
            args.Message.AddMarkupOrThrow(Loc.GetString(
                state.Splinted ? "trauma-examine-fracture-splinted" : "trauma-examine-fracture",
                ("zone", Loc.GetString(ZoneLocKey(zone))),
                ("tier", Loc.GetString(TierLocKey(state.Tier)))));
        }
    }

    private TimeSpan HealStep(FractureZoneState state) =>
        state.Splinted ? SplintedHealStep : UnsplintedHealStep;

    private static FractureTier Raise(FractureTier tier) =>
        tier >= FractureTier.Open ? FractureTier.Open : (FractureTier)((byte)tier + 1);

    private static FractureTier Lower(FractureTier tier) =>
        tier <= FractureTier.Crack ? FractureTier.Crack : (FractureTier)((byte)tier - 1);

    private static string ZoneLocKey(BodyZone zone) => zone switch
    {
        BodyZone.Head => "trauma-zone-head",
        BodyZone.Torso => "trauma-zone-torso",
        BodyZone.LeftArm => "trauma-zone-left-arm",
        BodyZone.RightArm => "trauma-zone-right-arm",
        BodyZone.LeftLeg => "trauma-zone-left-leg",
        BodyZone.RightLeg => "trauma-zone-right-leg",
        _ => "trauma-zone-torso",
    };

    private static string TierLocKey(FractureTier tier) => tier switch
    {
        FractureTier.Crack => "trauma-fracture-tier-crack",
        FractureTier.Full => "trauma-fracture-tier-full",
        FractureTier.Open => "trauma-fracture-tier-open",
        _ => "trauma-fracture-tier-crack",
    };
}
