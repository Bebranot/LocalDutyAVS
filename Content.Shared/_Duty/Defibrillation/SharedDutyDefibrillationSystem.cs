// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics.CodeAnalysis;
using Content.Shared._Duty.Concussion;
using Content.Shared.ADT.Atmos.Miasma;
using Content.Shared.Armor;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using DoAfterData = Content.Shared.DoAfter.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Medical;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.Timing;
using Content.Shared.Traits.Assorted;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Defibrillation;

/// <summary>
/// Логика LifePak-40s/45-A поверх ванильного дефибриллятора.
/// </summary>
/// <remarks>
/// Разделение ответственности:
/// <list type="bullet">
/// <item>Shared (предсказывается): старт анализа/разряда, кеш анализа, энергия, гель, серия ударов, слабость.</item>
/// <item>Сервер: всё, что слышно и видно в чате (голос, реплики с анти-спамом), решение в середине doafter
/// «заряжать или отменить», рандом. Так нет дублей звука при повторном предсказании и нет рассинхрона
/// там, где клиент не знает всего (SSD чужого игрока).</item>
/// </list>
/// Ванильный <c>SharedDefibrillatorSystem</c> поднимает на аппарате хуки <see cref="DutyDefibStartZapEvent"/>,
/// <see cref="DutyDefibCanZapEvent"/>, <see cref="DutyDefibZapModifyEvent"/>, <see cref="DutyDefibZappedEvent"/>.
/// </remarks>
public abstract partial class SharedDutyDefibrillationSystem : EntitySystem
{
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedConcussionSystem _concussion = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PowerCellSystem _powerCell = default!;
    [Dependency] private SharedRottingSystem _rotting = default!;
    [Dependency] private ItemToggleSystem _toggle = default!;
    [Dependency] private UseDelaySystem _useDelay = default!;

    private const string ChestSlot = "outerClothing";
    private const string BurnType = "Heat";
    protected const string NoShockDamageReason = "duty-defib-reason-damage";
    protected const string LowEnergyReason = "duty-defib-reason-low-energy";
    protected const string UnresponsiveReason = "duty-defib-reason-unresponsive";

    private static readonly string[] PhysicalDamageTypes = { "Blunt", "Slash", "Piercing" };
    private const float BodyArmorCoefficient = 0.85f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyDefibProfileComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<DutyDefibProfileComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<DutyDefibProfileComponent, GetVerbsEvent<AlternativeVerb>>(OnAlternativeVerbs);
        SubscribeLocalEvent<DutyDefibProfileComponent, GetVerbsEvent<UtilityVerb>>(OnUtilityVerbs);

        SubscribeLocalEvent<DutyDefibProfileComponent, DutyDefibStartZapEvent>(OnStartZap);
        SubscribeLocalEvent<DutyDefibProfileComponent, DutyDefibCanZapEvent>(OnCanZap);
        SubscribeLocalEvent<DutyDefibProfileComponent, DutyDefibZapModifyEvent>(OnZapModify);
        SubscribeLocalEvent<DutyDefibProfileComponent, DutyDefibZappedEvent>(OnZapped);
        SubscribeLocalEvent<DutyDefibProfileComponent, DutyDefibAnalyzeDoAfterEvent>(OnAnalyzeDoAfter);
        SubscribeLocalEvent<DutyDefibProfileComponent, DutyDefibRhythmCheckDoAfterEvent>(OnRhythmCheckDoAfter);

        SubscribeLocalEvent<DutyDefibGelComponent, AfterInteractEvent>(OnGelAfterInteract);
        SubscribeLocalEvent<DutyDefibGelComponent, DutyDefibGelDoAfterEvent>(OnGelDoAfter);
        SubscribeLocalEvent<DutyDefibGelComponent, ExaminedEvent>(OnGelExamined);

        SubscribeLocalEvent<DutyRevivalWeaknessComponent, RefreshMovementSpeedModifiersEvent>(OnWeaknessSpeed);
        SubscribeLocalEvent<DutyRevivalWeaknessComponent, ComponentStartup>((uid, _, _) => _movement.RefreshMovementSpeedModifiers(uid));
        SubscribeLocalEvent<DutyRevivalWeaknessComponent, ComponentShutdown>((uid, _, _) => _movement.RefreshMovementSpeedModifiers(uid));
    }

    #region Энергия

    public DutyDefibEnergyLevel? GetLevel(DutyDefibProfileComponent comp)
    {
        if (comp.Energies.Count == 0)
            return null;

        return comp.Energies[Math.Clamp(comp.CurrentEnergy, 0, comp.Energies.Count - 1)];
    }

    /// <summary>Звук зарядки текущего уровня (или аппарата без уровней).</summary>
    public SoundSpecifier? GetChargeSound(DutyDefibProfileComponent comp)
    {
        return GetLevel(comp) is { } level ? level.ChargeSound : comp.ChargeSound;
    }

    private void OnMapInit(Entity<DutyDefibProfileComponent> ent, ref MapInitEvent args)
    {
        ApplyEnergy(ent);
    }

    /// <summary>
    /// Переписывает значения текущего уровня в ванильные компоненты: время зарядки, лечение, шок и расход
    /// батареи берёт ванильный Zap. Все поля там сетевые, поэтому клиенту хватает Dirty.
    /// </summary>
    private void ApplyEnergy(Entity<DutyDefibProfileComponent> ent)
    {
        if (GetLevel(ent.Comp) is not { } level)
            return;

        if (TryComp<DefibrillatorComponent>(ent, out var defib))
        {
            defib.DoAfterDuration = level.DoAfter;
            defib.ZapHeal = new DamageSpecifier(level.Heal);
            defib.ZapDamage = level.ZapDamage;
            Dirty(ent, defib);
        }

        if (TryComp<PowerCellDrawComponent>(ent, out var draw))
        {
            draw.UseCharge = level.Charge;
            Dirty(ent, draw);
        }
    }

    private void CycleEnergy(Entity<DutyDefibProfileComponent> ent, EntityUid user)
    {
        if (ent.Comp.Energies.Count < 2 || Timing.CurTime < ent.Comp.NextEnergySwitch)
            return;

        // Смена энергии посреди зарядки дала бы удар нового уровня за время и звук старого.
        if (IsDeviceBusy(ent, user))
        {
            _popup.PopupClient(Loc.GetString("duty-defib-busy"), ent, user);
            return;
        }

        ent.Comp.CurrentEnergy = (ent.Comp.CurrentEnergy + 1) % ent.Comp.Energies.Count;
        ent.Comp.NextEnergySwitch = Timing.CurTime + ent.Comp.EnergySwitchInterval;
        Dirty(ent);
        ApplyEnergy(ent);

        // Возврат High → Low звучит длиннее: ручку прокручивают через весь диапазон.
        var wrapped = ent.Comp.CurrentEnergy == 0;
        _audio.PlayPredicted(wrapped ? ent.Comp.EnergyWrapSound : ent.Comp.EnergyStepSound, ent, user);
        _popup.PopupClient(Loc.GetString("duty-defib-energy-set",
                ("level", Loc.GetString(ent.Comp.Energies[ent.Comp.CurrentEnergy].Name))),
            ent, user);
    }

    private void OnAlternativeVerbs(Entity<DutyDefibProfileComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || GetLevel(ent.Comp) is not { } level || ent.Comp.Energies.Count < 2)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("duty-defib-verb-energy", ("level", Loc.GetString(level.Name))),
            Act = () => CycleEnergy(ent, user),
            Priority = 1,
        });
    }

    private void OnExamined(Entity<DutyDefibProfileComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (GetLevel(ent.Comp) is { } level)
            args.PushMarkup(Loc.GetString("duty-defib-examine-energy", ("level", Loc.GetString(level.Name))));

        if (ent.Comp.RequireAnalysis)
            args.PushMarkup(Loc.GetString("duty-defib-examine-two-step"));
    }

    #endregion

    #region Анализ

    /// <summary>
    /// Что надето на груди цели с точки зрения электродов.
    /// </summary>
    public DutyDefibChestCover GetChestCover(EntityUid target)
    {
        if (!_inventory.TryGetSlotEntity(target, ChestSlot, out var outer))
            return DutyDefibChestCover.None;

        if (TryComp<DutyDefibChestCoverComponent>(outer, out var cover))
            return cover.Cover;

        if (!HasComp<ArmorComponent>(outer) || !TryComp<InventoryComponent>(target, out var inventory))
            return DutyDefibChestCover.None;

        // ArmorComponent есть и у небоевой одежды (халаты — от кислоты, плащи — чуть от порезов),
        // поэтому бронёй считаем только заметную физическую защиту. Коэффициенты — штатным запросом брони.
        var query = new CoefficientQueryEvent(SlotFlags.OUTERCLOTHING);
        _inventory.RelayEvent((target, inventory), query);

        foreach (var type in PhysicalDamageTypes)
        {
            if (query.DamageModifiers.Coefficients.TryGetValue(type, out var coefficient) && coefficient <= BodyArmorCoefficient)
                return DutyDefibChestCover.Reduce;
        }

        return DutyDefibChestCover.None;
    }

    /// <summary>Номер следующего удара в серии (0 — первый).</summary>
    private int GetShotIndex(EntityUid target, DutyDefibProfileComponent profile)
    {
        if (!TryComp<DutyDefibHistoryComponent>(target, out var history)
            || history.Count == 0
            || Timing.CurTime - history.LastShot >= profile.SpamResetTime)
            return 0;

        return history.Count;
    }

    private static float Pick(List<float> list, int index)
    {
        return list.Count == 0 ? 1f : list[Math.Min(index, list.Count - 1)];
    }

    /// <summary>
    /// Множители лечения и шока для удара по цели прямо сейчас: серия ударов, одежда, гель.
    /// </summary>
    private void GetModifiers(DutyDefibProfileComponent profile, EntityUid target,
        out float heal, out float shock, out bool gelled)
    {
        var shot = GetShotIndex(target, profile);
        heal = Pick(profile.SpamHealMultipliers, shot);
        shock = Pick(profile.SpamShockMultipliers, shot);

        switch (GetChestCover(target))
        {
            case DutyDefibChestCover.Block when !profile.SuitBlocks:
                heal *= profile.SuitHealMultiplier;
                shock *= profile.SuitShockMultiplier;
                break;
            case DutyDefibChestCover.Reduce:
                heal *= profile.ArmorHealMultiplier;
                break;
        }

        gelled = HasComp<DutyDefibGelledComponent>(target);
        if (gelled)
            heal *= profile.GelHealMultiplier;
    }

    /// <summary>
    /// Поднимет ли разряд тело в крит: суммарный урон после лечения (с гелем, одеждой, штрафом за серию,
    /// текущей энергией и худшим ожогом без геля) ниже порога Dead. Без побочных эффектов.
    /// <paramref name="deficit"/> — на сколько итог выше порога (≤ 0, если разряд поможет).
    /// </summary>
    public bool WouldRevive(Entity<DutyDefibProfileComponent> ent, EntityUid target, out FixedPoint2 deficit)
    {
        deficit = FixedPoint2.Zero;

        if (!TryComp<DefibrillatorComponent>(ent, out var defib)
            || !TryComp<DamageableComponent>(target, out var damageable)
            || !_mobThreshold.TryGetThresholdForState(target, MobState.Dead, out var threshold))
            return false;

        GetModifiers(ent.Comp, target, out var healMultiplier, out _, out var gelled);
        var heal = defib.ZapHeal * healMultiplier;

        var total = FixedPoint2.Zero;
        foreach (var (type, value) in damageable.Damage.DamageDict)
        {
            var after = value;
            if (heal.DamageDict.TryGetValue(type, out var delta))
                after += delta;

            if (after > FixedPoint2.Zero)
                total += after;
        }

        if (!gelled)
            total += ent.Comp.BurnMax;

        deficit = total - threshold.Value;
        return total < threshold.Value;
    }

    /// <summary>
    /// Автоматический анализ ритма. Без побочных эффектов: звук, текст и кеш — у вызывающего.
    /// </summary>
    /// <param name="ignorePresence">Врач уже получил предупреждение про SSD и повторил запрос.</param>
    public DutyDefibAnalysis TryAnalyze(Entity<DutyDefibProfileComponent> ent, EntityUid target, bool ignorePresence = false)
    {
        if (!TryComp<MobStateComponent>(target, out var mobState))
            return new DutyDefibAnalysis(DutyDefibAnalysisKind.Blocked);

        if (!_mobState.IsDead(target, mobState))
            return new DutyDefibAnalysis(DutyDefibAnalysisKind.Alive, Arrhythmia: HasComp<DutyArrhythmiaComponent>(target));

        // Те же проверки и фразы, что у ванильного Zap.
        if (_rotting.IsRotten(target))
            return new DutyDefibAnalysis(DutyDefibAnalysisKind.Blocked, "defibrillator-rotten");

        if (HasComp<EmbalmedComponent>(target))
            return new DutyDefibAnalysis(DutyDefibAnalysisKind.Blocked, "defibrillator-embalmed");

        if (TryComp<UnrevivableComponent>(target, out var unrevivable))
            return new DutyDefibAnalysis(DutyDefibAnalysisKind.Blocked, unrevivable.ReasonMessage);

        if (GetLevel(ent.Comp) is { Cardioversion: true })
            return new DutyDefibAnalysis(DutyDefibAnalysisKind.NoShock, LowEnergyReason);

        if (!WouldRevive(ent, target, out var deficit))
            return new DutyDefibAnalysis(DutyDefibAnalysisKind.NoShock, NoShockDamageReason, deficit);

        // Последним: предупреждать про SSD имеет смысл, только если разряд вообще поможет.
        if (!ignorePresence && !IsPatientPresent(target))
            return new DutyDefibAnalysis(DutyDefibAnalysisKind.SoftBlocked, UnresponsiveReason);

        return new DutyDefibAnalysis(DutyDefibAnalysisKind.ShockAdvised);
    }

    /// <summary>
    /// В игре ли пациент. Клиент не видит сессию чужого игрока, поэтому приближение — «есть разум»;
    /// сервер уточняет по сессии владельца разума (как ванильный Zap).
    /// </summary>
    protected virtual bool IsPatientPresent(EntityUid target)
    {
        return TryComp<MindContainerComponent>(target, out var mindContainer) && mindContainer.HasMind;
    }

    /// <summary>Действует ли разрешение на принудительный разряд после предупреждения про SSD.</summary>
    protected bool IsSoftWarned(DutyDefibProfileComponent comp, EntityUid target)
    {
        return comp.SoftWarnedTarget == target && Timing.CurTime < comp.SoftWarnedUntil;
    }

    protected void SetSoftWarned(Entity<DutyDefibProfileComponent> ent, EntityUid target)
    {
        ent.Comp.SoftWarnedTarget = target;
        ent.Comp.SoftWarnedUntil = Timing.CurTime + ent.Comp.SoftWarnWindow;
        Dirty(ent);
    }

    /// <summary>
    /// Идущий doafter этим аппаратом у пользователя (анализ, разряд или анализ ритма).
    /// </summary>
    protected bool TryGetDeviceDoAfter(EntityUid device, EntityUid user, [NotNullWhen(true)] out DoAfterData? doAfter)
    {
        doAfter = null;
        if (!TryComp<DoAfterComponent>(user, out var doAfters))
            return false;

        foreach (var candidate in doAfters.DoAfters.Values)
        {
            if (candidate.Cancelled || candidate.Completed || candidate.Args.Used != device)
                continue;

            doAfter = candidate;
            return true;
        }

        return false;
    }

    protected bool IsDeviceBusy(EntityUid device, EntityUid user)
    {
        return TryGetDeviceDoAfter(device, user, out _);
    }

    private bool StartDeviceDoAfter(EntityUid device, EntityUid target, EntityUid user, TimeSpan duration, DoAfterEvent ev)
    {
        // Отойти от пациента дальше DistanceThreshold — прервать. MultiplyDelay выключен: doafter
        // синхронизирован со звуками зарядки, модификаторы скорости действий его бы рассинхронизировали.
        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, duration, ev, device, target, device)
        {
            NeedHand = true,
            MultiplyDelay = false,
        });
    }

    /// <summary>Голосовой отказ: «разряд не рекомендован» + причина текстом.</summary>
    protected void ReportNoShock(Entity<DutyDefibProfileComponent> ent, DutyDefibAnalysis analysis)
    {
        var reason = analysis.Kind == DutyDefibAnalysisKind.Alive
            ? Loc.GetString("duty-defib-pulse-detected")
            : analysis.Reason is { } key ? Loc.GetString(key) : null;

        var text = Loc.GetString("duty-defib-voice-no-shock");
        Announce(ent, ent.Comp.VoiceNoShockAdvised, reason == null ? text : $"{text} {reason}");
    }

    #endregion

    #region Старт анализа и разряда

    /// <summary>
    /// Применение аппарата на пациента. Решения, которые сопровождаются голосом, принимает сервер
    /// в середине doafter (<see cref="ScheduleCheck"/>): так живой пациент получает «разряд не рекомендован»
    /// с озвучкой, а не молчаливый отказ.
    /// </summary>
    private void OnStartZap(Entity<DutyDefibProfileComponent> ent, ref DutyDefibStartZapEvent args)
    {
        args.Handled = true;
        var target = args.Target;
        var user = args.User;

        // Повторный клик, пока аппарат занят, молча игнорируем: иначе второй doafter и повтор голоса.
        // Прервать можно, отойдя от пациента.
        if (IsDeviceBusy(ent, user) || Timing.CurTime < ent.Comp.RetryAfter)
        {
            args.Result = true;
            return;
        }

        if (!_toggle.IsActivated(ent.Owner))
        {
            // Выключенный аппарат не говорит — только подсказка врачу.
            _popup.PopupClient(Loc.GetString("defibrillator-not-on"), ent, user);
            return;
        }

        if (!HasComp<MobStateComponent>(target) || !TryComp<DefibrillatorComponent>(ent, out var defib))
            return;

        if (ent.Comp.SuitBlocks && GetChestCover(target) == DutyDefibChestCover.Block)
        {
            ReportNoShock(ent, new DutyDefibAnalysis(DutyDefibAnalysisKind.Blocked, "duty-defib-no-contact"));
            SetRetryCooldown(ent);
            args.Result = true;
            return;
        }

        // 40s, шаг 1: анализ без заряда. Пульс проверяется после «Analyzing» — живого отменяем там же.
        if (ent.Comp.RequireAnalysis && !HasFreshAdvice(ent.Comp, target))
        {
            args.Result = StartDeviceDoAfter(ent, target, user, ent.Comp.AnalysisDuration, new DutyDefibAnalyzeDoAfterEvent());
            if (args.Result)
            {
                QueueSound(ent, ent.Comp.VoiceAnalyzing);
                Announce(ent, null, Loc.GetString("duty-defib-voice-analyzing"));
                ScheduleCheck(ent, user, target, ent.Comp.AnalysisLead, DutyDefibCheck.AnalysisLead);
            }

            return;
        }

        // Разряд: 40s по свежему «разряд рекомендован» или 45-A (анализ — во вступлении этого же doafter).
        if (TryComp<UseDelayComponent>(ent, out var useDelay) && _useDelay.IsDelayed((ent.Owner, useDelay), defib.DelayId))
        {
            Announce(ent, null, Loc.GetString("duty-defib-not-ready"));
            SetRetryCooldown(ent);
            args.Result = true;
            return;
        }

        // Проверка батареи сама показывает ванильную подсказку о разряженной ячейке.
        if (!_powerCell.HasActivatableCharge(ent.Owner, user: user, predicted: true))
        {
            args.Result = true;
            return;
        }

        var lead = ent.Comp.ZapLead;
        if (ent.Comp.ZapDuration > lead + defib.DoAfterDuration)
            lead = ent.Comp.ZapDuration - defib.DoAfterDuration;

        args.Result = StartDeviceDoAfter(ent, target, user, lead + defib.DoAfterDuration,
            new DefibrillatorZapDoAfterEvent());
        if (!args.Result)
            return;

        if (ent.Comp.RequireAnalysis)
        {
            QueueSound(ent, ent.Comp.VoiceStandClear);
            Announce(ent, null, Loc.GetString("duty-defib-voice-stand-clear"));
        }
        else
        {
            QueueSound(ent, ent.Comp.VoiceAnalyzing);
            Announce(ent, null, Loc.GetString("duty-defib-voice-analyzing"));
        }

        ScheduleCheck(ent, user, target, lead, DutyDefibCheck.ZapLead);
    }

    /// <summary>
    /// Пауза после отказа: пока она идёт, новые применения игнорируются — от спама doafter-ами и репликами.
    /// </summary>
    protected void SetRetryCooldown(Entity<DutyDefibProfileComponent> ent)
    {
        ent.Comp.RetryAfter = Timing.CurTime + ent.Comp.RetryCooldown;
        Dirty(ent);
    }

    private bool HasFreshAdvice(DutyDefibProfileComponent comp, EntityUid target)
    {
        return comp.ShockAdvised && comp.LastAnalyzedTarget == target && Timing.CurTime < comp.AnalysisExpires;
    }

    #endregion

    #region Хуки ванильного дефибриллятора

    /// <summary>
    /// Страховка на момент удара (ванильный OnDoAfter и Zap): состояние могло измениться за время зарядки.
    /// Отказ озвучивает сервер; повторы гасит анти-спам <see cref="Announce"/>.
    /// </summary>
    private void OnCanZap(Entity<DutyDefibProfileComponent> ent, ref DutyDefibCanZapEvent args)
    {
        if (!TryComp<MobStateComponent>(args.Target, out var mobState))
            return;

        var dead = _mobState.IsDead(args.Target, mobState);

        if (GetLevel(ent.Comp) is { Cardioversion: true })
        {
            if (dead)
            {
                ReportNoShock(ent, new DutyDefibAnalysis(DutyDefibAnalysisKind.NoShock, LowEnergyReason));
                args.Cancelled = true;
                return;
            }

            if (!HasComp<DutyArrhythmiaComponent>(args.Target))
            {
                ReportNoShock(ent, new DutyDefibAnalysis(DutyDefibAnalysisKind.Alive));
                args.Cancelled = true;
                return;
            }

            args.AllowAlive = true;
        }
        else if (!dead && !args.TargetCanBeAlive)
        {
            ReportNoShock(ent, new DutyDefibAnalysis(DutyDefibAnalysisKind.Alive));
            args.Cancelled = true;
            return;
        }

        if (ent.Comp.SuitBlocks && GetChestCover(args.Target) == DutyDefibChestCover.Block)
        {
            ReportNoShock(ent, new DutyDefibAnalysis(DutyDefibAnalysisKind.Blocked, "duty-defib-no-contact"));
            args.Cancelled = true;
        }
    }

    private void OnZapModify(Entity<DutyDefibProfileComponent> ent, ref DutyDefibZapModifyEvent args)
    {
        if (GetLevel(ent.Comp) is { Cardioversion: true } && !_mobState.IsDead(args.Target))
            args.SkipRevive = true;

        GetModifiers(ent.Comp, args.Target, out var heal, out var shock, out var gelled);
        args.HealMultiplier *= heal;
        args.ShockMultiplier *= shock;

        if (gelled || args.SkipRevive || ent.Comp.BurnMax <= 0)
            return;

        // Zap предсказывается, поэтому «рандом» ожога — детерминированный от цели и тика: одинаков на клиенте и сервере.
        var seed = unchecked((int) GetNetEntity(args.Target).Id * 397 ^ (int) Timing.CurTick.Value);
        var burn = new System.Random(seed).Next(ent.Comp.BurnMin, ent.Comp.BurnMax + 1);
        args.ExtraDamage = new DamageSpecifier
        {
            DamageDict = { { BurnType, burn } },
        };
    }

    private void OnZapped(Entity<DutyDefibProfileComponent> ent, ref DutyDefibZappedEvent args)
    {
        var target = args.Target;
        RemComp<DutyDefibGelledComponent>(target);

        var history = EnsureComp<DutyDefibHistoryComponent>(target);
        if (Timing.CurTime - history.LastShot >= ent.Comp.SpamResetTime)
            history.Count = 0;
        history.Count++;
        history.LastShot = Timing.CurTime;
        Dirty(target, history);

        // Кеш анализа и разрешение на SSD — одноразовые: следующий удар требует нового решения.
        ent.Comp.LastZap = Timing.CurTime;
        ent.Comp.ShockAdvised = false;
        ent.Comp.LastAnalyzedTarget = null;
        ent.Comp.SoftWarnedTarget = null;
        Dirty(ent);

        if (args.Cardioversion)
        {
            RemComp<DutyArrhythmiaComponent>(target);
            Announce(ent, null, Loc.GetString("duty-defib-voice-rhythm-restored"));
        }

        if (args.Revived)
            ApplyRevivalWeakness(ent.Comp, target);

        QueueSound(ent, ent.Comp.ChargeDone);
        QueueSound(ent, ent.Comp.VoiceCheckPulse, 1.2f);
        Announce(ent, null, Loc.GetString("duty-defib-voice-check-pulse"));

        OnZappedServer(ent, args, history.Count);
    }

    #endregion

    #region Двухшаговый анализ (40s) и анализ ритма (45-A)

    private void OnAnalyzeDoAfter(Entity<DutyDefibProfileComponent> ent, ref DutyDefibAnalyzeDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        args.Handled = true;

        // Кеш считается и на клиенте (предсказание шага 2), SSD клиент видит приближённо — сервер поправит состоянием.
        var analysis = TryAnalyze(ent, target, IsSoftWarned(ent.Comp, target));
        ent.Comp.LastAnalyzedTarget = target;
        ent.Comp.AnalysisExpires = Timing.CurTime + ent.Comp.AnalysisWindow;
        // SSD: разряд разрешён повторным нажатием — предупреждение звучит сейчас, удар следующим применением.
        ent.Comp.ShockAdvised = analysis.Kind is DutyDefibAnalysisKind.ShockAdvised or DutyDefibAnalysisKind.SoftBlocked;
        Dirty(ent);

        switch (analysis.Kind)
        {
            case DutyDefibAnalysisKind.ShockAdvised:
                // «Разряд рекомендован» → тон готовности → «нажмите кнопку разряда», один раз, без повторов.
                QueueSound(ent, ent.Comp.VoiceShockAdvised);
                QueueSound(ent, ent.Comp.ReadyTone, 1.4f);
                QueueSound(ent, ent.Comp.VoicePushToShock, 3.4f);
                Announce(ent, null, Loc.GetString("duty-defib-voice-push-to-shock"));
                break;
            default:
                // В том числе SSD: предупреждение сейчас, разряд — следующим применением.
                ReportNoShock(ent, analysis);
                break;
        }
    }

    private void OnUtilityVerbs(Entity<DutyDefibProfileComponent> ent, ref GetVerbsEvent<UtilityVerb> args)
    {
        if (!ent.Comp.RhythmCheckVerb || !args.CanAccess || !args.CanInteract || !HasComp<MobStateComponent>(args.Target))
            return;

        var user = args.User;
        var target = args.Target;
        args.Verbs.Add(new UtilityVerb
        {
            Text = Loc.GetString("duty-defib-verb-rhythm-check"),
            Act = () => StartRhythmCheck(ent, target, user),
            Priority = 1,
        });
    }

    private void StartRhythmCheck(Entity<DutyDefibProfileComponent> ent, EntityUid target, EntityUid user)
    {
        if (!_toggle.IsActivated(ent.Owner))
        {
            _popup.PopupClient(Loc.GetString("defibrillator-not-on"), ent, user);
            return;
        }

        if (IsDeviceBusy(ent, user))
            return;

        if (StartDeviceDoAfter(ent, target, user, ent.Comp.RhythmCheckDuration, new DutyDefibRhythmCheckDoAfterEvent()))
            QueueSound(ent, ent.Comp.VoiceAnalyzing);
    }

    private void OnRhythmCheckDoAfter(Entity<DutyDefibProfileComponent> ent, ref DutyDefibRhythmCheckDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        args.Handled = true;

        // Результат — только голос и реплика аппарата, их выдаёт сервер: клиенту считать незачем.
        if (_net.IsClient)
            return;

        var analysis = TryAnalyze(ent, target);
        switch (analysis.Kind)
        {
            case DutyDefibAnalysisKind.Alive:
                // Кардиоверсия по «Аритмии» — тот же вывод, что в анализаторе здоровья.
                Announce(ent, null, Loc.GetString(analysis.Arrhythmia
                    ? "duty-defib-rhythm-alive-arrhythmia"
                    : "duty-defib-rhythm-alive"));
                break;
            case DutyDefibAnalysisKind.ShockAdvised:
                Announce(ent, ent.Comp.VoiceShockAdvised, Loc.GetString("duty-defib-rhythm-advised"));
                break;
            case DutyDefibAnalysisKind.NoShock when analysis.Reason == NoShockDamageReason:
                // Разряд поможет, когда итог станет строго ниже порога.
                var amount = Math.Max(1, (int) MathF.Floor(analysis.Deficit.Float()) + 1);
                Announce(ent, ent.Comp.VoiceNoShockAdvised, Loc.GetString("duty-defib-rhythm-deficit",
                    ("amount", amount),
                    ("type", GetDominantDamageName(target))));
                break;
            default:
                ReportNoShock(ent, analysis);
                break;
        }
    }

    private string GetDominantDamageName(EntityUid target)
    {
        if (!TryComp<DamageableComponent>(target, out var damageable))
            return string.Empty;

        ProtoId<DamageTypePrototype>? best = null;
        var bestValue = FixedPoint2.Zero;
        foreach (var (type, value) in damageable.Damage.DamageDict)
        {
            if (value <= bestValue)
                continue;

            best = type;
            bestValue = value;
        }

        return best is { } id && _proto.TryIndex(id, out var proto)
            ? proto.LocalizedName
            : string.Empty;
    }

    #endregion

    #region Гель

    private void OnGelAfterInteract(Entity<DutyDefibGelComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !HasComp<MobStateComponent>(target))
            return;

        args.Handled = true;

        if (GetChestCover(target) == DutyDefibChestCover.Block)
        {
            _popup.PopupClient(Loc.GetString("duty-defib-gel-no-contact"), target, args.User);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.DoAfter,
            new DutyDefibGelDoAfterEvent(), ent, target, ent)
        {
            NeedHand = true,
            BreakOnMove = true,
        });
    }

    private void OnGelDoAfter(Entity<DutyDefibGelComponent> ent, ref DutyDefibGelDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target || ent.Comp.Uses <= 0)
            return;

        args.Handled = true;

        if (GetChestCover(target) == DutyDefibChestCover.Block)
        {
            _popup.PopupClient(Loc.GetString("duty-defib-gel-no-contact"), target, args.User);
            return;
        }

        var gelled = EnsureComp<DutyDefibGelledComponent>(target);
        gelled.EndTime = Timing.CurTime + ent.Comp.Duration;
        Dirty(target, gelled);

        ent.Comp.Uses--;
        Dirty(ent);

        _popup.PopupClient(Loc.GetString("duty-defib-gel-applied"), target, args.User);

        if (ent.Comp.Uses <= 0 && _net.IsServer)
            QueueDel(ent);
    }

    private void OnGelExamined(Entity<DutyDefibGelComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushMarkup(Loc.GetString("duty-defib-gel-examine", ("uses", ent.Comp.Uses)));
    }

    #endregion

    #region Аритмия и слабость

    public void AddArrhythmia(EntityUid target, TimeSpan duration)
    {
        var arrhythmia = EnsureComp<DutyArrhythmiaComponent>(target);
        var end = Timing.CurTime + duration;
        if (arrhythmia.EndTime < end)
            arrhythmia.EndTime = end;
        Dirty(target, arrhythmia);
    }

    private void ApplyRevivalWeakness(DutyDefibProfileComponent profile, EntityUid target)
    {
        var weakness = EnsureComp<DutyRevivalWeaknessComponent>(target);
        weakness.EndTime = Timing.CurTime + profile.RevivalWeaknessDuration;
        Dirty(target, weakness);
        _movement.RefreshMovementSpeedModifiers(target);

        if (TryComp<ConcussionComponent>(target, out var concussion))
            _concussion.AddRaw(target, profile.RevivalConcussion, concussion);
    }

    private void OnWeaknessSpeed(Entity<DutyRevivalWeaknessComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        // При снятии компонента пересчёт идёт из ComponentShutdown — он ещё висит, но уже не должен действовать.
        if (ent.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        args.ModifySpeed(ent.Comp.WalkModifier, ent.Comp.SprintModifier);
    }

    #endregion

    #region Серверные точки расширения (на клиенте — пусто)

    /// <summary>
    /// Реплика аппарата: голос (если есть) + текст в IC-чат. На сервере с анти-спамом: одинаковая реплика
    /// одного аппарата не повторяется чаще кулдауна, сколько бы раз ни кликали.
    /// </summary>
    protected virtual void Announce(Entity<DutyDefibProfileComponent> ent, SoundSpecifier? voice, string text)
    {
    }

    /// <summary>
    /// Звук аппарата с задержкой. <paramref name="guardUser"/> — звук отменяется, если к моменту проигрывания
    /// у этого пользователя больше не идёт doafter аппарата (врач отошёл).
    /// </summary>
    protected virtual void QueueSound(EntityUid device, SoundSpecifier? sound, float delay = 0f, EntityUid? guardUser = null)
    {
    }

    /// <summary>Проверка в середине doafter; решение с голосом принимает сервер.</summary>
    protected virtual void ScheduleCheck(EntityUid device, EntityUid user, EntityUid target, TimeSpan delay, DutyDefibCheck check)
    {
    }

    /// <summary>Серверные последствия удара: рандом (аритмия) и лужи.</summary>
    protected virtual void OnZappedServer(Entity<DutyDefibProfileComponent> ent, DutyDefibZappedEvent args, int shotInSeries)
    {
    }

    #endregion
}
