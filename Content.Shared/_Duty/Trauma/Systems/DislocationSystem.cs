// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.Events;
using Content.Shared.HealthExaminable;
using Content.Shared.Movement.Systems;
using Content.Shared.Rejuvenate;
using Content.Shared.Standing;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Trauma.Systems;

/// <summary>
/// _Duty: вывихи — состояние, остаточная слабость и вправление-хелперы (функциональные эффекты
/// агрегирует <see cref="TraumaResolverSystem"/>, лечение — <c>DislocationTreatmentSystem</c>).
/// Наложение приходит серверным <see cref="TraumaRolledEvent"/>, истечение остаточной слабости
/// крутится на сервере, осмотр — в shared.
/// </summary>
public sealed class DislocationSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;

    /// <summary>Сколько длится остаточная слабость зоны после вправления (Phase 6 — тюнинг).</summary>
    public static readonly TimeSpan ResidualDuration = TimeSpan.FromSeconds(30);

    public override void Initialize()
    {
        SubscribeLocalEvent<TraumaRolledEvent>(OnTraumaRolled);
        SubscribeLocalEvent<DislocationComponent, HealthBeingExaminedEvent>(OnHealthExamined);
        SubscribeLocalEvent<DislocationComponent, RejuvenateEvent>(OnRejuvenate);
    }

    private void OnRejuvenate(Entity<DislocationComponent> ent, ref RejuvenateEvent args)
    {
        RemComp<DislocationComponent>(ent);
        _movementSpeed.RefreshMovementSpeedModifiers(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        List<EntityUid>? toRemove = null;

        var query = EntityQueryEnumerator<DislocationComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Residual.Count == 0)
                continue;

            // Сначала дешёвая проверка «есть ли вообще что снимать»: перебор Values не выделяет
            // память (struct-энумератор), а Keys.ToArray() — выделяет массив каждый тик на каждого
            // раненого. Истечение остаточной слабости — событие раз в десятки секунд, так что
            // копию ключей берём только в тот единственный тик, когда она реально нужна.
            var due = false;
            foreach (var expiry in comp.Residual.Values)
            {
                if (now < expiry)
                    continue;

                due = true;
                break;
            }

            if (!due)
                continue;

            foreach (var zone in comp.Residual.Keys.ToArray())
            {
                if (now < comp.Residual[zone])
                    continue;

                comp.Residual.Remove(zone);
            }

            // Удаление компонента откладываем — нельзя менять структуру во время перебора запроса.
            if (comp.Dislocated.Count == 0 && comp.Residual.Count == 0)
                (toRemove ??= new()).Add(uid);
            else
                Dirty(uid, comp);

            _movementSpeed.RefreshMovementSpeedModifiers(uid);
        }

        if (toRemove is null)
            return;

        foreach (var uid in toRemove)
            RemComp<DislocationComponent>(uid);
    }

    private void OnTraumaRolled(TraumaRolledEvent args)
    {
        if (args.Type != TraumaType.Dislocation || args.Zone is not { } zone)
            return;

        var target = args.Target;
        var comp = EnsureComp<DislocationComponent>(target);

        if (!comp.Dislocated.Add(zone))
            return;

        Dirty(target, comp);
        _movementSpeed.RefreshMovementSpeedModifiers(target);

        // Вывих руки роняет то, что в ней было.
        if (BodyZoneCategory.IsArm(zone))
        {
            var drop = new DropHandItemsEvent();
            RaiseLocalEvent(target, ref drop);
        }
    }

    private void OnHealthExamined(Entity<DislocationComponent> ent, ref HealthBeingExaminedEvent args)
    {
        foreach (var zone in ent.Comp.Dislocated)
        {
            args.Message.PushNewline();
            args.Message.AddMarkupOrThrow(Loc.GetString("trauma-examine-dislocation", ("zone", Loc.GetString(TraumaLoc.ZoneKey(zone)))));
        }

        foreach (var zone in ent.Comp.Residual.Keys)
        {
            args.Message.PushNewline();
            args.Message.AddMarkupOrThrow(Loc.GetString("trauma-examine-residual", ("zone", Loc.GetString(TraumaLoc.ZoneKey(zone)))));
        }
    }

    /// <summary>Есть ли активный вывих.</summary>
    public bool HasDislocation(EntityUid uid) =>
        TryComp<DislocationComponent>(uid, out var comp) && comp.Dislocated.Count > 0;

    /// <summary>
    /// Вправляет одну (первую) вывихнутую зону: снимает вывих, зона переходит в остаточную слабость.
    /// false — если вправлять нечего.
    /// </summary>
    public bool TryReduceFirst(EntityUid uid)
    {
        if (!TryComp<DislocationComponent>(uid, out var comp) || comp.Dislocated.Count == 0)
            return false;

        var zone = comp.Dislocated.First();
        comp.Dislocated.Remove(zone);
        comp.Residual[zone] = _timing.CurTime + ResidualDuration;

        Dirty(uid, comp);
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
        return true;
    }

}
