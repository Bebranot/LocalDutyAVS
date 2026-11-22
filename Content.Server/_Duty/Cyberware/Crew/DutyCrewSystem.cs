// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using Content.Server.GameTicking;
using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Crew;
using Content.Shared._Duty.Trauma;
using Content.Shared._Duty.Trauma.Components;
using Content.Shared._Duty.Trauma.Events;
using Content.Shared._Duty.Trauma.Systems;
using Content.Shared.Body.Systems;
using Content.Shared.Chat;
using Content.Shared.Clothing;
using Content.Shared.Damage.Systems;
using Content.Shared.Gravity;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Cyberware.Crew;

/// <summary>
/// _Duty: серверная логика «служебных» имплантов NT — нейрочасы, мультитул, гемостат, адреналин,
/// анти-ЭМИ экран, армированный скелет, диктофон, магнитные ступни.
/// Выдача компонентов носителю (мезоны, визоры и т.п.) — в <see cref="DutyCyberGrantSystem"/>.
/// </summary>
public sealed partial class DutyCrewSystem : EntitySystem
{
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedMagbootsSystem _magboots = default!;
    [Dependency] private PaperSystem _paper = default!;

    private static readonly SoundSpecifier DenySound = new SoundCollectionSpecifier("DutyCyberDeny");
    private static readonly SoundSpecifier BootSound = new SoundCollectionSpecifier("DutyCyberBoot");
    private static readonly SoundSpecifier AdrenalineSound = new SoundPathSpecifier("/Audio/Items/hypospray.ogg");
    private static readonly SoundSpecifier ToolSound = new SoundPathSpecifier("/Audio/Effects/Footsteps/ratchet.ogg");

    private static readonly EntProtoId KnockdownEffect = "StatusEffectKnockdown";

    private const float UpdateInterval = 1f;
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyCrewClockComponent, DutyCrewClockEvent>(OnClock);

        SubscribeLocalEvent<DutyCrewMultitoolComponent, DutyCrewMultitoolEvent>(OnMultitool);
        SubscribeLocalEvent<DutyCrewMultitoolComponent, ImplantRemovedEvent>(OnMultitoolRemoved);
        SubscribeLocalEvent<DutyCrewMultitoolComponent, DutyCyberEmpEvent>(OnMultitoolEmp);

        SubscribeLocalEvent<DutyCrewAdrenalineComponent, DutyCrewAdrenalineEvent>(OnAdrenaline);

        SubscribeLocalEvent<DutyCrewEmpShieldComponent, DutyCyberEmpShieldEvent>(OnEmpShield);

        SubscribeLocalEvent<DutyCrewRecorderComponent, DutyCrewRecorderEvent>(OnRecorder);
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);

        SubscribeLocalEvent<DutyMagFeetImplantComponent, DutyCyberGrantChangedEvent>(OnMagFeetChanged);

        // Скелет гасит перелом уже после того, как FractureSystem его записал.
        SubscribeLocalEvent<TraumaRolledEvent>(OnTraumaRolled, after: new[] { typeof(FractureSystem) });
    }

    #region Нейрочасы

    private void OnClock(Entity<DutyCrewClockComponent> ent, ref DutyCrewClockEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;

        if (!CheckOnline(ent.Owner, args.Performer))
            return;

        var shift = _ticker.RoundDuration();
        // Время станции — полдень плюс время смены: единой «станционной даты» в сборке нет.
        var station = TimeSpan.FromHours(12) + shift;

        var text = Loc.GetString("duty-crew-clock-popup",
            ("shift", shift.ToString("hh\\:mm\\:ss")),
            ("station", $"{station.Hours % 24:00}:{station.Minutes:00}"));
        _popup.PopupEntity(text, args.Performer, args.Performer, PopupType.Medium);
        _audio.PlayPvs(BootSound, args.Performer);
    }

    #endregion

    #region Мультитул

    private void OnMultitool(Entity<DutyCrewMultitoolComponent> ent, ref DutyCrewMultitoolEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;

        var user = args.Performer;

        if (ent.Comp.Spawned is { } tool && !TerminatingOrDeleted(tool))
        {
            Del(tool);
            ent.Comp.Spawned = null;
            _popup.PopupEntity(Loc.GetString("duty-crew-multitool-retract"), user, user);
            _audio.PlayPvs(ToolSound, user);
            return;
        }

        if (!CheckOnline(ent.Owner, user))
            return;

        var spawned = Spawn(ent.Comp.Tool, Transform(user).Coordinates);
        if (!_hands.TryPickupAnyHand(user, spawned))
        {
            Del(spawned);
            _popup.PopupEntity(Loc.GetString("duty-crew-multitool-hands-full"), user, user, PopupType.SmallCaution);
            return;
        }

        ent.Comp.Spawned = spawned;
        _popup.PopupEntity(Loc.GetString("duty-crew-multitool-extend"), user, user);
        _audio.PlayPvs(ToolSound, user);
    }

    private void OnMultitoolRemoved(Entity<DutyCrewMultitoolComponent> ent, ref ImplantRemovedEvent args)
    {
        RetractTool(ent);
    }

    private void OnMultitoolEmp(Entity<DutyCrewMultitoolComponent> ent, ref DutyCyberEmpEvent args)
    {
        RetractTool(ent);
    }

    private void RetractTool(Entity<DutyCrewMultitoolComponent> ent)
    {
        if (ent.Comp.Spawned is { } tool && !TerminatingOrDeleted(tool))
            Del(tool);
        ent.Comp.Spawned = null;
    }

    #endregion

    #region Адреналин

    private void OnAdrenaline(Entity<DutyCrewAdrenalineComponent> ent, ref DutyCrewAdrenalineEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;

        var user = args.Performer;
        if (!CheckOnline(ent.Owner, user))
            return;

        // Стан-брейк: снимаем оглушение и нокдаун, поднимаем на ноги, снимаем усталость и даём короткий разгон.
        _status.TryRemoveStatusEffect(user, SharedStunSystem.StunId);
        _status.TryRemoveStatusEffect(user, KnockdownEffect);
        _stun.TryUnstun(user);
        _stun.ForceStandUp(user);
        _stamina.TakeStaminaDamage(user, -ent.Comp.StaminaHeal);

        var boost = EnsureComp<DutyAdrenalineBoostComponent>(user);
        boost.Multiplier = ent.Comp.SpeedMultiplier;
        boost.EndTime = _timing.CurTime + ent.Comp.BoostDuration;
        Dirty(user, boost);
        _speed.RefreshMovementSpeedModifiers(user);

        _popup.PopupEntity(Loc.GetString("duty-crew-adrenaline-popup"), user, user, PopupType.LargeCaution);
        _audio.PlayPvs(AdrenalineSound, user);

        // Железа одноразовая.
        _cyber.SetModuleState(ent.Owner, DutyCyberModuleState.Burned);
    }

    #endregion

    #region Анти-ЭМИ

    private void OnEmpShield(Entity<DutyCrewEmpShieldComponent> ent, ref DutyCyberEmpShieldEvent args)
    {
        if (args.Cancelled)
            return;

        args.Cancelled = true;
        _popup.PopupEntity(Loc.GetString("duty-crew-emp-shield-popup"), args.Host, args.Host, PopupType.Small);
    }

    #endregion

    #region Скелет

    private void OnTraumaRolled(TraumaRolledEvent args)
    {
        if (args.Type != TraumaType.Fracture || args.Zone is not { } zone)
            return;

        if (!TryComp<FractureComponent>(args.Target, out var fracture) || !fracture.Zones.TryGetValue(zone, out var state))
            return;

        var chance = 0f;
        if (TryComp<ImplantedComponent>(args.Target, out var implanted))
        {
            foreach (var implant in implanted.ImplantContainer.ContainedEntities)
            {
                if (TryComp<DutyCrewSkeletonComponent>(implant, out var skeleton) && _cyber.IsModuleOnline(implant))
                    chance = MathF.Max(chance, skeleton.AbsorbChance);
            }
        }

        if (chance <= 0f || !_random.Prob(chance))
            return;

        // Трещина гасится целиком, более тяжёлый перелом смягчается на один тир.
        if (state.Tier <= FractureTier.Crack)
        {
            fracture.Zones.Remove(zone);
        }
        else
        {
            state.Tier = (FractureTier) ((byte) state.Tier - 1);
            fracture.Zones[zone] = state;
        }

        if (fracture.Zones.Count == 0)
            RemComp<FractureComponent>(args.Target);
        else
            Dirty(args.Target, fracture);

        _speed.RefreshMovementSpeedModifiers(args.Target);
        _popup.PopupEntity(Loc.GetString("duty-crew-skeleton-popup"), args.Target, args.Target, PopupType.Medium);
    }

    #endregion

    #region Диктофон

    private void OnRecorder(Entity<DutyCrewRecorderComponent> ent, ref DutyCrewRecorderEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;

        var user = args.Performer;

        if (ent.Comp.Recording)
        {
            ent.Comp.Recording = false;
            PrintRecording(ent, user);
            return;
        }

        if (!CheckOnline(ent.Owner, user))
            return;

        ent.Comp.Recording = true;
        ent.Comp.Started = _timing.CurTime;
        ent.Comp.Lines.Clear();
        _cyber.SetModuleState(ent.Owner, DutyCyberModuleState.Active);
        _popup.PopupEntity(Loc.GetString("duty-crew-recorder-start"), user, user);
        _audio.PlayPvs(BootSound, user);
    }

    private void OnSpoke(EntitySpokeEvent args)
    {
        // Пишем только речь носителя. Перебор всех диктофонов на каждую реплику дешевле, чем держать слушателей.
        var query = EntityQueryEnumerator<DutyCrewRecorderComponent>();
        while (query.MoveNext(out var uid, out var rec))
        {
            if (!rec.Recording || rec.Lines.Count >= rec.MaxLines)
                continue;

            if (!_cyber.TryGetHost(uid, out var host) || host != args.Source || !_cyber.IsModuleOnline(uid))
                continue;

            var at = _timing.CurTime - rec.Started;
            rec.Lines.Add($"[{(int) at.TotalMinutes:00}:{at.Seconds:00}] {Name(args.Source)}: {args.Message}");
        }
    }

    private void PrintRecording(Entity<DutyCrewRecorderComponent> ent, EntityUid user)
    {
        _cyber.SetModuleState(ent.Owner, DutyCyberModuleState.Online);

        var sb = new StringBuilder();
        sb.AppendLine(Loc.GetString("duty-crew-recorder-header", ("time", _ticker.RoundDuration().ToString("hh\\:mm\\:ss"))));
        foreach (var line in ent.Comp.Lines)
        {
            sb.AppendLine(line);
        }
        ent.Comp.Lines.Clear();

        var paper = Spawn("Paper", Transform(user).Coordinates);
        _paper.SetContent(paper, sb.ToString());
        _hands.TryPickupAnyHand(user, paper);

        _popup.PopupEntity(Loc.GetString("duty-crew-recorder-stop"), user, user);
        _audio.PlayPvs(BootSound, user);
    }

    #endregion

    #region Магнитные ступни

    private void OnMagFeetChanged(Entity<DutyMagFeetImplantComponent> ent, ref DutyCyberGrantChangedEvent args)
    {
        // Движок отключает «выдувание» давлением и обновляет невесомость только в UpdateMagbootEffects;
        // компонент Magboots на теле без предмета сам этого не сделает. Временный компонент нужен лишь ради ProtoId алерта.
        _magboots.UpdateMagbootEffects(args.Host, (args.Host, new MagbootsComponent()), args.Granted);
        _speed.RefreshMovementSpeedModifiers(args.Host);
    }

    #endregion

    private bool CheckOnline(EntityUid implant, EntityUid user)
    {
        if (_cyber.IsModuleOnline(implant))
            return true;

        _popup.PopupEntity(Loc.GetString("duty-crew-offline"), user, user, PopupType.SmallCaution);
        _audio.PlayPvs(DenySound, user);
        return false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;
        var dt = _accumulator;
        _accumulator = 0f;

        var now = _timing.CurTime;

        // Гемостат: закрывает кровотечение у носителя, пока модуль онлайн.
        var hemostats = EntityQueryEnumerator<DutyCrewHemostatComponent>();
        while (hemostats.MoveNext(out var uid, out var hemostat))
        {
            if (!_cyber.TryGetHost(uid, out var host) || !_cyber.IsModuleOnline(uid, null))
                continue;

            _bloodstream.TryModifyBleedAmount(host, -hemostat.ReductionPerSecond * dt);
        }

        // Адреналин: разгон кончился — снимаем.
        var boosts = EntityQueryEnumerator<DutyAdrenalineBoostComponent>();
        while (boosts.MoveNext(out var uid, out var boost))
        {
            if (boost.EndTime > now)
                continue;

            RemCompDeferred<DutyAdrenalineBoostComponent>(uid);
            _speed.RefreshMovementSpeedModifiers(uid);
        }
    }
}
