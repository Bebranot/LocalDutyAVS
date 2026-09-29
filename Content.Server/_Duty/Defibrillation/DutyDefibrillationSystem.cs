// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Body.Components;
using Content.Shared._Duty.Defibrillation;
using Content.Shared.Chat;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using DoAfterData = Content.Shared.DoAfter.DoAfter;
using Content.Shared.Electrocution;
using Content.Shared.Fluids.Components;
using Content.Shared.GameTicking;
using Content.Shared.Medical;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Duty.Defibrillation;

/// <summary>
/// Серверная часть LifePak: решение «заряжать или отменить» в середине doafter, голос и реплики
/// с анти-спамом, рандом (аритмия, лужи) и таймеры статусов.
/// </summary>
public sealed class DutyDefibrillationSystem : SharedDutyDefibrillationSystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedChatSystem _chat = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedElectrocutionSystem _electrocution = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly SharedStaminaSystem _stamina = default!;

    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private const float WetShockChance = 0.15f;
    private const float PuddleSearchRange = 0.4f;
    private const float WetShockRange = 1.5f;
    private static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(1);

    /// <summary>Одинаковая реплика одного аппарата — не чаще этого.</summary>
    private static readonly TimeSpan AnnounceCooldown = TimeSpan.FromSeconds(4);

    /// <summary>История ударов нужна только для штрафа за серию (сброс через секунды) — дальше её незачем держать и синхронизировать.</summary>
    private static readonly TimeSpan HistoryLifetime = TimeSpan.FromMinutes(1);

    private readonly List<QueuedSound> _soundQueue = new();
    private readonly List<PendingCheck> _checks = new();
    private readonly List<PendingCheck> _newChecks = new();
    private readonly Dictionary<(EntityUid Device, string Text), TimeSpan> _announced = new();
    private readonly List<(EntityUid, string)> _announcedExpired = new();
    private readonly List<EntityUid> _toRemove = new();
    private readonly HashSet<Entity<MobStateComponent>> _nearbyMobs = new();
    private readonly HashSet<Entity<PuddleComponent>> _puddles = new();
    private TimeSpan _nextStatusUpdate;
    private EntityQuery<RespiratorComponent> _respiratorQuery;

    private readonly record struct QueuedSound(EntityUid Device, SoundSpecifier Sound, TimeSpan PlayAt, EntityUid? GuardUser);

    private sealed class PendingCheck
    {
        public EntityUid Device;
        public EntityUid User;
        public EntityUid Target;
        public DutyDefibCheck Check;
        public TimeSpan CheckAt;

        /// <summary>Идёт зарядка: следим за doafter, чтобы заглушить звук, если его прервут.</summary>
        public bool Charging;
        public TimeSpan ChargeStarted;
        public EntityUid? ChargeAudio;
    }

    public override void Initialize()
    {
        base.Initialize();

        _respiratorQuery = GetEntityQuery<RespiratorComponent>();

        // Отложенные голоса и проверки ссылаются на аппараты прошлого раунда.
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _soundQueue.Clear();
            _checks.Clear();
            _announced.Clear();
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_soundQueue.Count > 0)
            ProcessSoundQueue();

        if (_checks.Count > 0)
            ProcessChecks();

        if (Timing.CurTime < _nextStatusUpdate)
            return;

        _nextStatusUpdate = Timing.CurTime + StatusInterval;
        UpdateArrhythmia();
        RemoveExpired<DutyRevivalWeaknessComponent>(c => c.EndTime);
        RemoveExpired<DutyDefibGelledComponent>(c => c.EndTime);
        RemoveExpired<DutyDefibHistoryComponent>(c => c.LastShot + HistoryLifetime);
        PruneAnnounced();
    }

    #region Голос и реплики

    protected override void Announce(Entity<DutyDefibProfileComponent> ent, SoundSpecifier? voice, string text)
    {
        var key = (ent.Owner, text);
        if (_announced.TryGetValue(key, out var until) && Timing.CurTime < until)
            return;

        _announced[key] = Timing.CurTime + AnnounceCooldown;

        if (voice != null)
            _audio.PlayPvs(voice, ent);

        _chat.TrySendInGameICMessage(ent, text, InGameICChatType.Speak, hideChat: false, ignoreActionBlocker: true);
    }

    private void PruneAnnounced()
    {
        if (_announced.Count == 0)
            return;

        _announcedExpired.Clear();
        foreach (var (key, until) in _announced)
        {
            if (Timing.CurTime >= until)
                _announcedExpired.Add(key);
        }

        foreach (var key in _announcedExpired)
        {
            _announced.Remove(key);
        }
    }

    protected override void QueueSound(EntityUid device, SoundSpecifier? sound, float delay = 0f, EntityUid? guardUser = null)
    {
        if (sound == null)
            return;

        if (delay <= 0f)
        {
            _audio.PlayPvs(sound, device);
            return;
        }

        _soundQueue.Add(new QueuedSound(device, sound, Timing.CurTime + TimeSpan.FromSeconds(delay), guardUser));
    }

    private void ProcessSoundQueue()
    {
        for (var i = _soundQueue.Count - 1; i >= 0; i--)
        {
            var queued = _soundQueue[i];
            if (Timing.CurTime < queued.PlayAt)
                continue;

            _soundQueue.RemoveAt(i);
            if (TerminatingOrDeleted(queued.Device))
                continue;

            // Фраза посреди процедуры не звучит, если врач её уже прервал.
            if (queued.GuardUser is { } user && !IsDeviceBusy(queued.Device, user))
                continue;

            _audio.PlayPvs(queued.Sound, queued.Device);
        }
    }

    #endregion

    #region Решение в середине doafter

    protected override void ScheduleCheck(EntityUid device, EntityUid user, EntityUid target, TimeSpan delay, DutyDefibCheck check)
    {
        _checks.Add(new PendingCheck
        {
            Device = device,
            User = user,
            Target = target,
            Check = check,
            CheckAt = Timing.CurTime + delay,
        });
    }

    private void ProcessChecks()
    {
        for (var i = _checks.Count - 1; i >= 0; i--)
        {
            var pending = _checks[i];

            if (pending.Charging)
            {
                if (IsDeviceBusy(pending.Device, pending.User))
                    continue;

                // Doafter закончился. Удара с начала зарядки не было — значит, прервали: глушим зарядку.
                if (!TryComp<DutyDefibProfileComponent>(pending.Device, out var charged) || charged.LastZap < pending.ChargeStarted)
                    _audio.Stop(pending.ChargeAudio);

                _checks.RemoveAt(i);
                continue;
            }

            if (Timing.CurTime < pending.CheckAt)
                continue;

            _checks.RemoveAt(i);

            if (!TryComp<DutyDefibProfileComponent>(pending.Device, out var profile)
                || !TryGetDeviceDoAfter(pending.Device, pending.User, out var doAfter))
                continue;

            var ent = (pending.Device, profile);
            switch (pending.Check)
            {
                case DutyDefibCheck.AnalysisLead:
                    RunAnalysisLead(ent, pending, doAfter);
                    break;
                case DutyDefibCheck.ZapLead:
                    RunZapLead(ent, pending, doAfter);
                    break;
            }
        }

        if (_newChecks.Count == 0)
            return;

        _checks.AddRange(_newChecks);
        _newChecks.Clear();
    }

    /// <summary>Анализ 40s после «Analyzing»: живого — отменяем с голосом, иначе «Stand clear».</summary>
    private void RunAnalysisLead(Entity<DutyDefibProfileComponent> ent, PendingCheck pending, DoAfterData doAfter)
    {
        var analysis = TryAnalyze(ent, pending.Target, IsSoftWarned(ent.Comp, pending.Target));
        if (analysis.Kind == DutyDefibAnalysisKind.Alive)
        {
            Refuse(ent, doAfter, analysis);
            return;
        }

        _audio.PlayPvs(ent.Comp.VoiceStandClear, ent);
        Announce(ent, null, Loc.GetString("duty-defib-voice-stand-clear"));
    }

    /// <summary>
    /// Разряд после вступления: 40s перепроверяет пульс (анализ уже был), 45-A анализирует сейчас.
    /// Годится — зарядка, нет — отмена doafter с голосом до траты заряда.
    /// </summary>
    private void RunZapLead(Entity<DutyDefibProfileComponent> ent, PendingCheck pending, DoAfterData doAfter)
    {
        var target = pending.Target;

        if (ent.Comp.RequireAnalysis)
        {
            if (!_mobState.IsDead(target))
            {
                Refuse(ent, doAfter, new DutyDefibAnalysis(DutyDefibAnalysisKind.Alive));
                return;
            }

            StartCharge(ent, pending);
            return;
        }

        var analysis = TryAnalyze(ent, target, IsSoftWarned(ent.Comp, target));
        var cardioversion = analysis is { Kind: DutyDefibAnalysisKind.Alive, Arrhythmia: true }
                            && GetLevel(ent.Comp) is { Cardioversion: true };

        if (analysis.Kind != DutyDefibAnalysisKind.ShockAdvised && !cardioversion)
        {
            // SSD: запоминаем предупреждение — повторное применение в окне разрядит принудительно.
            if (analysis.Kind == DutyDefibAnalysisKind.SoftBlocked)
                SetSoftWarned(ent, target);

            Refuse(ent, doAfter, analysis);
            return;
        }

        Announce(ent, ent.Comp.VoiceShockAdvised, Loc.GetString(cardioversion
            ? "duty-defib-voice-cardioversion"
            : "duty-defib-voice-shock-advised"));
        StartCharge(ent, pending);

        // «Stand clear» ближе к концу зарядки — чтобы не наложиться на «Shock advised».
        if (TryComp<DefibrillatorComponent>(ent, out var defib))
        {
            var standClearAt = (defib.DoAfterDuration - ent.Comp.StandClearBeforeShock).TotalSeconds;
            if (standClearAt > 0)
                QueueSound(ent, ent.Comp.VoiceStandClear, (float) standClearAt, pending.User);
        }
    }

    private void StartCharge(Entity<DutyDefibProfileComponent> ent, PendingCheck pending)
    {
        var audio = _audio.PlayPvs(GetChargeSound(ent.Comp), ent);
        _newChecks.Add(new PendingCheck
        {
            Device = pending.Device,
            User = pending.User,
            Target = pending.Target,
            Check = pending.Check,
            Charging = true,
            ChargeStarted = Timing.CurTime,
            ChargeAudio = audio?.Entity,
        });
    }

    private void Refuse(Entity<DutyDefibProfileComponent> ent, DoAfterData doAfter, DutyDefibAnalysis analysis)
    {
        _doAfter.Cancel(doAfter.Id);
        ReportNoShock(ent, analysis);
        SetRetryCooldown(ent);
    }

    /// <summary>
    /// В игре ли пациент: разум есть и его владелец подключён (как проверяет ванильный Zap).
    /// Вышедший в призрака игрок подключён — это не SSD.
    /// </summary>
    protected override bool IsPatientPresent(EntityUid target)
    {
        return _mind.TryGetMind(target, out _, out var mind)
               && _player.TryGetSessionById(mind.UserId, out _);
    }

    #endregion

    #region Последствия удара

    protected override void OnZappedServer(Entity<DutyDefibProfileComponent> ent, DutyDefibZappedEvent args, int shotInSeries)
    {
        if (!args.Cardioversion && GetLevel(ent.Comp) is { } level)
        {
            var chance = shotInSeries <= 1 ? level.ArrhythmiaChance : level.ArrhythmiaRepeatChance;
            if (chance > 0f && _random.Prob(chance))
                AddArrhythmia(args.Target, ent.Comp.ArrhythmiaDuration);
        }

        TryWetShock(ent, args);
    }

    /// <summary>
    /// Мокрый пол: если цель или врач стоит в воде, с шансом бьёт током всех, кто стоит в воде рядом.
    /// Изоляция учитывается. Ванильный удар по «interacters» работает отдельно.
    /// </summary>
    private void TryWetShock(EntityUid device, DutyDefibZappedEvent args)
    {
        // Шанс — первым: поиск луж дороже броска.
        if (!_random.Prob(WetShockChance) || !TryComp<DefibrillatorComponent>(device, out var defib))
            return;

        EntityUid origin;
        if (IsInWater(args.Target))
            origin = args.Target;
        else if (IsInWater(args.User))
            origin = args.User;
        else
            return;

        _nearbyMobs.Clear();
        _lookup.GetEntitiesInRange(Transform(origin).Coordinates, WetShockRange, _nearbyMobs);
        foreach (var mob in _nearbyMobs)
        {
            if (mob.Owner == args.Target || !IsInWater(mob))
                continue;

            _electrocution.TryDoElectrocution(mob, device, defib.ZapDamage, defib.WritheDuration, true);
        }
    }

    private bool IsInWater(EntityUid uid)
    {
        _puddles.Clear();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, PuddleSearchRange, _puddles);
        foreach (var puddle in _puddles)
        {
            if (_solution.TryGetSolution(puddle.Owner, puddle.Comp.SolutionName, out _, out var solution)
                && solution.ContainsPrototype(WaterReagent))
                return true;
        }

        return false;
    }

    #endregion

    #region Статусы

    private void UpdateArrhythmia()
    {
        _toRemove.Clear();

        var query = EntityQueryEnumerator<DutyArrhythmiaComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var arrhythmia, out var mobState))
        {
            if (Timing.CurTime >= arrhythmia.EndTime)
            {
                _toRemove.Add(uid);
                continue;
            }

            if (_mobState.IsCritical(uid, mobState))
            {
                // Удвоение удушья имеет смысл только у тех, кто вообще задыхается в крите.
                if (_respiratorQuery.HasComp(uid))
                    _damageable.TryChangeDamage(uid, arrhythmia.CritDamage, true, interruptsDoAfters: false);
                continue;
            }

            if (!_mobState.IsAlive(uid, mobState) || !_random.Prob(arrhythmia.SkipChance))
                continue;

            _popup.PopupEntity(Loc.GetString("duty-defib-arrhythmia-skip"), uid, uid, PopupType.SmallCaution);
            _stamina.TakeStaminaDamage(uid, arrhythmia.SkipStaminaDamage, visual: false);
        }

        foreach (var uid in _toRemove)
        {
            RemComp<DutyArrhythmiaComponent>(uid);
        }
    }

    private void RemoveExpired<T>(Func<T, TimeSpan> endTime) where T : IComponent
    {
        _toRemove.Clear();

        var query = EntityQueryEnumerator<T>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (Timing.CurTime >= endTime(comp))
                _toRemove.Add(uid);
        }

        foreach (var uid in _toRemove)
        {
            RemComp<T>(uid);
        }
    }

    #endregion
}
