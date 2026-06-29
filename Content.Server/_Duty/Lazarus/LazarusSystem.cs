using System.Linq;
using Content.Server.Administration;
using Content.Server.Body.Systems;
using Content.Shared.Administration;
using Content.Shared._Duty.Lazarus;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Lazarus;

/// <summary>
/// Серверная логика эффекта Лазаруса ("Last Standing"). Следит за персонажами в
/// крите: когда урон вплотную подбирается к смерти, один раз крутит шанс 5–12%.
/// При успехе — вытаскивает из крита, вкалывает стимуляторы, замедляет и шлёт
/// клиенту кинематику. См. <see cref="LazarusComponent"/>.
/// </summary>
public sealed class LazarusSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly MovementModStatusSystem _movementMod = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IConsoleHost _console = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    /// <summary>Как часто опрашиваем критов (сек). Раз в тик не нужно.</summary>
    private const float ScanInterval = 0.25f;
    private float _accumulator;

    /// <summary>Запланированные "вставания": (сущность, время лечения).</summary>
    private readonly List<(EntityUid Uid, TimeSpan Time)> _pendingRevives = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawn);

        _console.RegisterCommand("duty_lazarus_test",
            "Принудительно запустить эффект Лазаруса у игрока (в обход шанса, крита и кулдауна).",
            "duty_lazarus_test <username>",
            TestCommand,
            TestCommandCompletion);
    }

    private void OnPlayerSpawn(PlayerSpawnCompleteEvent ev)
    {
        // Только гуманоиды — как и в системе реплик боли.
        if (!HasComp<HumanoidAppearanceComponent>(ev.Mob))
            return;

        EnsureComp<LazarusComponent>(ev.Mob);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        // Отложенные "вставания" обрабатываем каждый тик — для точного попадания в музыку.
        if (_pendingRevives.Count > 0)
        {
            for (var i = _pendingRevives.Count - 1; i >= 0; i--)
            {
                if (now < _pendingRevives[i].Time)
                    continue;

                var target = _pendingRevives[i].Uid;
                _pendingRevives.RemoveAt(i);

                if (Exists(target))
                    Revive(target);
            }
        }

        _accumulator += frameTime;
        if (_accumulator < ScanInterval)
            return;
        _accumulator = 0f;

        var query = EntityQueryEnumerator<LazarusComponent, MobStateComponent, DamageableComponent, MobThresholdsComponent>();
        while (query.MoveNext(out var uid, out var lazarus, out var mobState, out var damageable, out var thresholds))
        {
            // Зона смерти существует только в крите. Вне крита сбрасываем флаг входа.
            if (mobState.CurrentState != MobState.Critical)
            {
                lazarus.InDeathZone = false;
                continue;
            }

            if (!TryGetCritRange(thresholds, out var critThreshold, out var deadThreshold))
                continue;

            var total = damageable.TotalDamage;

            // Доля диапazона "крит → смерть", оставшаяся до гибели.
            // 1.0 — только что в крите; 0.0 — на пороге смерти.
            var range = (deadThreshold - critThreshold).Float();
            if (range <= 0f)
                continue;

            var remaining = (float)((deadThreshold - total).Float() / range);
            var inZone = remaining > 0f && remaining <= lazarus.NearDeathThreshold;

            if (!inZone)
            {
                lazarus.InDeathZone = false;
                continue;
            }

            // Уже внутри зоны — бросок крутится только в момент входа.
            if (lazarus.InDeathZone)
                continue;

            lazarus.InDeathZone = true;

            if (now < lazarus.NextAvailableTime)
                continue;

            var chance = _random.NextFloat(lazarus.MinChance, lazarus.MaxChance);
            if (!_random.Prob(chance))
                continue;

            Trigger(uid, lazarus, now);
        }
    }

    /// <summary>Достаёт пороги перехода в крит и в смерть из <see cref="MobThresholdsComponent"/>.</summary>
    private bool TryGetCritRange(MobThresholdsComponent thresholds, out FixedPoint2 crit, out FixedPoint2 dead)
    {
        crit = FixedPoint2.Zero;
        dead = FixedPoint2.Zero;
        var haveCrit = false;
        var haveDead = false;

        foreach (var (threshold, state) in thresholds.Thresholds)
        {
            switch (state)
            {
                case MobState.Critical:
                    crit = threshold;
                    haveCrit = true;
                    break;
                case MobState.Dead:
                    dead = threshold;
                    haveDead = true;
                    break;
            }
        }

        return haveCrit && haveDead && dead > crit;
    }

    /// <summary>
    /// Момент срабатывания: запускает кинематику/музыку и попап-подводку, ставит
    /// кулдаун и планирует реальное "вставание" через <see cref="LazarusComponent.ReviveDelay"/>.
    /// Само лечение из крита происходит позже, в <see cref="Revive"/> — подстать музыке.
    /// </summary>
    private void Trigger(EntityUid uid, LazarusComponent lazarus, TimeSpan now)
    {
        lazarus.NextAvailableTime = now + lazarus.Cooldown;

        // Внутренний голос — сразу, на затемнении.
        _popup.PopupEntity(Loc.GetString("duty-lazarus-popup-self"), uid, uid, PopupType.LargeCaution);

        // Кинематика проигрывается только у самого игрока.
        if (TryComp<ActorComponent>(uid, out var actor))
        {
            RaiseNetworkEvent(new LazarusTriggeredEvent
            {
                BlackoutFadeIn = lazarus.BlackoutFadeIn,
                BlackoutHold = lazarus.BlackoutHold,
                BlackoutFadeOut = lazarus.BlackoutFadeOut,
                VignetteDuration = lazarus.VignetteDuration,
                VignetteFadeOut = lazarus.VignetteFadeOut,
                Heartbeat = lazarus.Heartbeat,
                HeartbeatVolume = lazarus.HeartbeatVolume,
                LastStand = lazarus.LastStand,
                LastStandVolume = lazarus.LastStandVolume,
                LastStandDelay = lazarus.LastStandDelay,
            }, actor.PlayerSession);
        }

        _pendingRevives.Add((uid, now + lazarus.ReviveDelay));
    }

    /// <summary>
    /// Отложенное "вставание": лечение из крита, инъекция стимуляторов, замедление
    /// и попап для окружающих. Вызывается через <see cref="LazarusComponent.ReviveDelay"/>
    /// после <see cref="Trigger"/>.
    /// </summary>
    private void Revive(EntityUid uid)
    {
        if (!TryComp<LazarusComponent>(uid, out var lazarus) ||
            !TryComp<DamageableComponent>(uid, out var damageable))
            return;

        var critThreshold = FixedPoint2.New(100);
        if (TryComp<MobThresholdsComponent>(uid, out var thresholds)
            && TryGetCritRange(thresholds, out var crit, out _))
        {
            critThreshold = crit;
        }

        HealToTarget(uid, damageable, critThreshold * lazarus.HealToCritFraction);
        InjectReagents(uid, lazarus);
        ApplyMaxHpPenalty(uid, lazarus);

        _movementMod.TryAddMovementSpeedModDuration(
            uid,
            lazarus.SlowdownEffect,
            lazarus.SlowdownDuration,
            lazarus.SlowdownModifier);

        // Окружающие видят, как боец снова приходит в себя.
        _popup.PopupEntity(Loc.GetString("duty-lazarus-popup-others", ("target", Name(uid))), uid,
            Filter.PvsExcept(uid), true, PopupType.MediumCaution);
    }

    /// <summary>
    /// Снижает урон так, чтобы суммарный стал равен <paramref name="target"/>,
    /// сохраняя пропорции по типам урона. Этого достаточно, чтобы выйти из крита.
    /// </summary>
    private void HealToTarget(EntityUid uid, DamageableComponent damageable, FixedPoint2 target)
    {
        var current = damageable.TotalDamage;
        if (current <= target || current <= 0)
            return;

        var factor = (float)(target.Float() / current.Float());
        var scaled = damageable.Damage * factor;
        _damageable.SetDamage((uid, damageable), scaled);
    }

    private void InjectReagents(EntityUid uid, LazarusComponent lazarus)
    {
        if (lazarus.InjectedReagents.Count == 0)
            return;

        var solution = new Solution();
        foreach (var (reagent, quantity) in lazarus.InjectedReagents)
            solution.AddReagent(reagent, quantity);

        _bloodstream.TryAddToChemicals(uid, solution);
    }

    /// <summary>
    /// Цена воскрешения: снижает максимальное здоровье до <see cref="LazarusComponent.MaxHpPenaltyFraction"/>
    /// от исходного, пропорционально понижая пороги крита и смерти. Применяется один раз за жизнь —
    /// повторные срабатывания (через кулдаун) не складываются. Вешает <see cref="LazarusScarComponent"/>,
    /// по которому сканер здоровья показывает соответствующую строку.
    /// </summary>
    private void ApplyMaxHpPenalty(EntityUid uid, LazarusComponent lazarus)
    {
        // Уже был штраф в этой жизни — не складываем.
        if (HasComp<LazarusScarComponent>(uid))
            return;

        var fraction = Math.Clamp(lazarus.MaxHpPenaltyFraction, 0.1f, 1f);
        if (fraction >= 1f)
            return;

        if (!TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return;

        // Считываем текущие пороги до изменения (SetMobStateThreshold правит словарь).
        FixedPoint2? crit = null;
        FixedPoint2? dead = null;
        foreach (var (threshold, state) in thresholds.Thresholds)
        {
            switch (state)
            {
                case MobState.Critical:
                    crit = threshold;
                    break;
                case MobState.Dead:
                    dead = threshold;
                    break;
            }
        }

        if (crit is { } critValue)
            _mobThreshold.SetMobStateThreshold(uid, critValue * fraction, MobState.Critical, thresholds);
        if (dead is { } deadValue)
            _mobThreshold.SetMobStateThreshold(uid, deadValue * fraction, MobState.Dead, thresholds);

        EnsureComp<LazarusScarComponent>(uid);
    }

    // ── Консольная команда для теста ───────────────────────────────────────────

    [AdminCommand(AdminFlags.Admin)]
    private void TestCommand(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError("Использование: duty_lazarus_test <username>");
            return;
        }

        if (!_playerManager.TryGetSessionByUsername(args[0], out var session))
        {
            shell.WriteError($"Игрок '{args[0]}' не найден.");
            return;
        }

        if (session.AttachedEntity is not { } mob)
        {
            shell.WriteError("Игрок не привязан к сущности.");
            return;
        }

        var lazarus = EnsureComp<LazarusComponent>(mob);

        Trigger(mob, lazarus, _timing.CurTime);
        shell.WriteLine($"Эффект Лазаруса запущен у '{args[0]}'.");
    }

    private CompletionResult TestCommandCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromOptions(_playerManager.Sessions.Select(s => new CompletionOption(s.Name)));

        return CompletionResult.Empty;
    }
}
