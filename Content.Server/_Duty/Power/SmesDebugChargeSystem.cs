using Content.Server._Duty.RoundStartVote;
using Content.Server.Power.SMES;
using Content.Shared.GameTicking;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;

namespace Content.Server._Duty.Power;

/// <summary>
/// _Duty: обработчик эффекта голосования раунд-старта "SmesInfiniteCharge" — переводит все
/// СМЭС станции в дебаг-режим бесконечного заряда до конца раунда. Реагирует на
/// <see cref="RoundStartVoteEffectAppliedEvent"/> по строковому <see cref="EffectId"/>, а не
/// завязан на конкретный прототип голосования, поэтому фреймворк голосований остаётся не в курсе про СМЭС.
///
/// Держит заряд полным периодическим пересчётом в <see cref="Update"/>, а не подпиской на
/// <see cref="MapInitEvent"/>/<see cref="ChargeChangedEvent"/> у <see cref="SmesComponent"/> — в этом
/// движке directed-подписка на пару (компонент, событие) допускает ровно одного подписчика
/// (Dictionary.TryAdd в EntityEventBus.Directed), а обе эти пары уже заняты ванильным SmesSystem.
/// Раз в секунду достаточно — топит заряд на всех СМЭС станции разом, включая появившиеся
/// после применения эффекта (стройка/админ-спавн).
/// </summary>
public sealed class SmesDebugChargeSystem : EntitySystem
{
    public const string EffectId = "SmesInfiniteCharge";

    private const float RefreshInterval = 1f;

    [Dependency] private readonly SharedBatterySystem _battery = default!;

    private bool _active;
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartVoteEffectAppliedEvent>(OnEffectApplied);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_active)
            return;

        _accumulator += frameTime;
        if (_accumulator < RefreshInterval)
            return;

        _accumulator = 0f;
        TopUpAll();
    }

    private void OnEffectApplied(RoundStartVoteEffectAppliedEvent ev)
    {
        if (ev.EffectId != EffectId)
            return;

        _active = true;
        _accumulator = 0f;
        TopUpAll();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _active = false;
    }

    private void TopUpAll()
    {
        var query = EntityQueryEnumerator<SmesComponent, BatteryComponent>();
        while (query.MoveNext(out var uid, out _, out var battery))
        {
            if (_battery.GetCharge((uid, battery)) < battery.MaxCharge)
                _battery.SetCharge((uid, battery), battery.MaxCharge);
        }
    }
}
