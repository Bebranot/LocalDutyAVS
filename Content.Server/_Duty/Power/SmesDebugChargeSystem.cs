using Content.Server._Duty.RoundStartVote;
using Content.Server.Power.SMES;
using Content.Shared.GameTicking;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;

namespace Content.Server._Duty.Power;

/// <summary>
/// _Duty: обработчик эффекта голосования раунд-старта "SmesInfiniteCharge" — переводит все
/// СМЭС станции в дебаг-режим бесконечного заряда до конца раунда. Реагирует на
/// <see cref="RoundStartVoteEffectAppliedEvent"/> по строковому <see cref="EffectId"/>, а не
/// завязан на конкретный прототип голосования, поэтому фреймворк голосований остаётся не в курсе про СМЭС.
///
/// Держит заряд полным реактивно: топит заряд каждый раз, когда он падает
/// (<see cref="ChargeChangedEvent"/> прилетает у СМЭС практически каждый тик, пока сеть его
/// заряжает/разряжает — см. BatterySystem.PostSync), плюс сразу топит СМЭС, появившиеся после
/// применения эффекта (стройка/админ-спавн).
/// </summary>
public sealed class SmesDebugChargeSystem : EntitySystem
{
    public const string EffectId = "SmesInfiniteCharge";

    [Dependency] private readonly SharedBatterySystem _battery = default!;

    private bool _active;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartVoteEffectAppliedEvent>(OnEffectApplied);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<SmesComponent, MapInitEvent>(OnSmesMapInit);
        SubscribeLocalEvent<SmesComponent, ChargeChangedEvent>(OnSmesChargeChanged);
    }

    private void OnEffectApplied(RoundStartVoteEffectAppliedEvent ev)
    {
        if (ev.EffectId != EffectId)
            return;

        _active = true;

        var query = EntityQueryEnumerator<SmesComponent, BatteryComponent>();
        while (query.MoveNext(out var uid, out _, out var battery))
        {
            _battery.SetCharge((uid, battery), battery.MaxCharge);
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _active = false;
    }

    private void OnSmesMapInit(Entity<SmesComponent> ent, ref MapInitEvent args)
    {
        if (!_active || !TryComp<BatteryComponent>(ent.Owner, out var battery))
            return;

        _battery.SetCharge((ent.Owner, battery), battery.MaxCharge);
    }

    private void OnSmesChargeChanged(Entity<SmesComponent> ent, ref ChargeChangedEvent args)
    {
        if (!_active || args.CurrentCharge >= args.MaxCharge)
            return;

        if (!TryComp<BatteryComponent>(ent.Owner, out var battery))
            return;

        _battery.SetCharge((ent.Owner, battery), args.MaxCharge);
    }
}
