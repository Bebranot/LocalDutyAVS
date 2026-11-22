// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Scanning;
using Content.Shared._Duty.Cyberware.Security;
using Content.Shared.ADT.Atmos.Miasma;
using Content.Shared.Access.Components;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.Speech;
using Content.Shared.Speech.Components;
using Content.Shared.Traits.Assorted;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Duty.Cyberware.Security;

/// <summary>
/// _Duty: серверная часть СБ/командных имплантов — рефлекторный блок, «Лазарь», «Бодикам»,
/// «Чёрный ящик капитана», «Нейро-печать», стресс-анализатор и ручной сканер кибернетики.
/// </summary>
public sealed partial class DutySecurityCyberSystem : EntitySystem
{
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private DutyCyberScanSystem _scan = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedRottingSystem _rotting = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly SoundSpecifier DenySound = new SoundCollectionSpecifier("DutyCyberDeny");

    private const float UpdateInterval = 1f;
    private float _accumulator;

    private const int MaxLogEntries = 120;
    private const int MaxRadioLines = 6;
    private const int MaxRadioLineLength = 90;

    private EntityQuery<DutyReflexBlockImplantComponent> _reflexQuery;
    private EntityQuery<DutyStressAnalyzerImplantComponent> _stressQuery;
    private EntityQuery<DutyBodycamImplantComponent> _bodycamQuery;
    private EntityQuery<DutyBlackBoxCaptainImplantComponent> _blackBoxQuery;
    private EntityQuery<DutyNeuroSealImplantComponent> _sealQuery;

    public override void Initialize()
    {
        base.Initialize();

        _reflexQuery = GetEntityQuery<DutyReflexBlockImplantComponent>();
        _stressQuery = GetEntityQuery<DutyStressAnalyzerImplantComponent>();
        _bodycamQuery = GetEntityQuery<DutyBodycamImplantComponent>();
        _blackBoxQuery = GetEntityQuery<DutyBlackBoxCaptainImplantComponent>();
        _sealQuery = GetEntityQuery<DutyNeuroSealImplantComponent>();

        SubscribeLocalEvent<DutyReflexBlockImplantComponent, ImplantImplantedEvent>(OnReflexChanged);
        SubscribeLocalEvent<DutyReflexBlockImplantComponent, ImplantRemovedEvent>(OnReflexRemoved);

        SubscribeLocalEvent<DutyStressAnalyzerImplantComponent, ImplantImplantedEvent>(OnStressChanged);
        SubscribeLocalEvent<DutyStressAnalyzerImplantComponent, ImplantRemovedEvent>(OnStressRemoved);

        SubscribeLocalEvent<DutyBodycamImplantComponent, ImplantImplantedEvent>(OnBodycamImplanted);
        SubscribeLocalEvent<DutyBodycamImplantComponent, ImplantRemovedEvent>(OnBodycamRemoved);
        SubscribeLocalEvent<DutyBodycamImplantComponent, ActivateImplantEvent>(OnBodycamPrint);

        SubscribeLocalEvent<DutyBlackBoxCaptainImplantComponent, ImplantImplantedEvent>(OnBlackBoxImplanted);
        SubscribeLocalEvent<DutyBlackBoxCaptainImplantComponent, ImplantRemovedEvent>(OnBlackBoxRemoved);

        SubscribeLocalEvent<DutyRecorderHostComponent, ListenEvent>(OnRecorderListen);
        SubscribeLocalEvent<DutyRecorderHostComponent, MobStateChangedEvent>(OnRecorderMobState);
        SubscribeLocalEvent<DutyRecorderHostComponent, ComponentShutdown>(OnRecorderShutdown);

        // Доступ: система доступа поднимает это событие на носителе и ищет AccessComponent у перечисленных сущностей.
        // Сам имплант (с AccessComponent) в этот список и добавляем — карту не украсть.
        SubscribeLocalEvent<ImplantedComponent, GetAdditionalAccessEvent>(OnGetAdditionalAccess);

        SubscribeLocalEvent<DutyCyberScannerComponent, AfterInteractEvent>(OnScannerInteract);
        SubscribeLocalEvent<DutyCyberScannerComponent, DutyCyberScanDoAfterEvent>(OnScannerDone);
    }

    #region Host recalculation

    private void OnReflexChanged(Entity<DutyReflexBlockImplantComponent> ent, ref ImplantImplantedEvent args) => RecalcReflex(args.Implanted);
    private void OnReflexRemoved(Entity<DutyReflexBlockImplantComponent> ent, ref ImplantRemovedEvent args) => RecalcReflex(args.Implanted);
    private void OnStressChanged(Entity<DutyStressAnalyzerImplantComponent> ent, ref ImplantImplantedEvent args) => RecalcStress(args.Implanted);
    private void OnStressRemoved(Entity<DutyStressAnalyzerImplantComponent> ent, ref ImplantRemovedEvent args) => RecalcStress(args.Implanted);
    private void OnBodycamImplanted(Entity<DutyBodycamImplantComponent> ent, ref ImplantImplantedEvent args) => RecalcRecorder(args.Implanted);
    private void OnBodycamRemoved(Entity<DutyBodycamImplantComponent> ent, ref ImplantRemovedEvent args) => RecalcRecorder(args.Implanted);
    private void OnBlackBoxImplanted(Entity<DutyBlackBoxCaptainImplantComponent> ent, ref ImplantImplantedEvent args) => RecalcRecorder(args.Implanted);
    private void OnBlackBoxRemoved(Entity<DutyBlackBoxCaptainImplantComponent> ent, ref ImplantRemovedEvent args) => RecalcRecorder(args.Implanted);

    // Событие извлечения приходит после выхода из контейнера, поэтому пересчёт по контейнеру уже без удалённого импланта.
    private void RecalcReflex(EntityUid host)
    {
        var best = 1f;
        var found = false;
        foreach (var implant in Implants(host))
        {
            if (!_reflexQuery.TryComp(implant, out var reflex))
                continue;

            found = true;
            best = MathF.Min(best, reflex.StaminaCoefficient);
        }

        if (!found)
        {
            RemComp<DutyReflexBlockHostComponent>(host);
            return;
        }

        var comp = EnsureComp<DutyReflexBlockHostComponent>(host);
        comp.Coefficient = best;
        Dirty(host, comp);
    }

    private void RecalcStress(EntityUid host)
    {
        foreach (var implant in Implants(host))
        {
            if (_stressQuery.HasComp(implant))
            {
                EnsureComp<DutyStressAnalyzerHostComponent>(host);
                return;
            }
        }

        RemComp<DutyStressAnalyzerHostComponent>(host);
    }

    private void RecalcRecorder(EntityUid host)
    {
        var keep = TimeSpan.Zero;
        var blackBox = false;
        var bodycam = false;
        foreach (var implant in Implants(host))
        {
            if (_bodycamQuery.TryComp(implant, out var cam))
            {
                bodycam = true;
                var camKeep = TimeSpan.FromMinutes(Math.Max(cam.KeepMinutes, 1));
                if (camKeep > keep)
                    keep = camKeep;
            }

            if (_blackBoxQuery.TryComp(implant, out var box))
            {
                blackBox = true;
                if (box.Window > keep)
                    keep = box.Window;
            }
        }

        if (!bodycam && !blackBox)
        {
            RemComp<DutyRecorderHostComponent>(host);
            return;
        }

        var rec = EnsureComp<DutyRecorderHostComponent>(host);
        rec.Retention = keep;
        rec.BlackBox = blackBox;

        // Слушатель нужен, чтобы «подслушивать» речь вокруг; чужой компонент не трогаем и не сносим.
        if (!HasComp<ActiveListenerComponent>(host))
        {
            AddComp<ActiveListenerComponent>(host);
            rec.AddedListener = true;
        }
    }

    private void OnRecorderShutdown(Entity<DutyRecorderHostComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.AddedListener && !TerminatingOrDeleted(ent))
            RemComp<ActiveListenerComponent>(ent);
    }

    private IReadOnlyList<EntityUid> Implants(EntityUid host)
    {
        if (!TryComp<ImplantedComponent>(host, out var implanted))
            return Array.Empty<EntityUid>();

        return implanted.ImplantContainer.ContainedEntities;
    }

    #endregion

    #region Recorder (Bodycam / BlackBox)

    private void AddLog(Entity<DutyRecorderHostComponent> ent, string text)
    {
        var now = _timing.CurTime;
        var log = ent.Comp.Log;
        log.Add(new DutyRecorderEntry(now, text));

        var cutoff = now - ent.Comp.Retention;
        log.RemoveAll(e => e.Time < cutoff);
        if (log.Count > MaxLogEntries)
            log.RemoveRange(0, log.Count - MaxLogEntries);
    }

    private void OnRecorderListen(Entity<DutyRecorderHostComponent> ent, ref ListenEvent args)
    {
        // Бодикам — обычная электроника, под ЭМИ пишет помехи; чёрный ящик закалён.
        if (!ent.Comp.BlackBox && _cyber.IsHostEmped(ent))
            return;

        var name = Identity.Name(args.Source, EntityManager);
        var text = FormattedMessage.EscapeText(args.Message);
        AddLog(ent, Loc.GetString("duty-sec-rec-speech", ("name", name), ("text", text)));
    }

    private void OnRecorderMobState(Entity<DutyRecorderHostComponent> ent, ref MobStateChangedEvent args)
    {
        var key = args.NewMobState switch
        {
            MobState.Critical => "duty-sec-rec-crit",
            MobState.Dead => "duty-sec-rec-dead",
            _ => null,
        };

        if (key != null)
            AddLog(ent, Loc.GetString(key));

        if (args.NewMobState != MobState.Dead || args.OldMobState == MobState.Dead || !ent.Comp.BlackBox)
            return;

        SendBlackBox(ent);
    }

    private void SendBlackBox(Entity<DutyRecorderHostComponent> ent)
    {
        var window = TimeSpan.FromSeconds(30);
        var channelId = "Command";
        foreach (var implant in Implants(ent))
        {
            if (!_blackBoxQuery.TryComp(implant, out var box))
                continue;

            window = box.Window;
            channelId = box.Channel;
            break;
        }

        if (!_proto.TryIndex<RadioChannelPrototype>(channelId, out var channel))
            return;

        var now = _timing.CurTime;
        var lines = new List<string>();
        foreach (var entry in ent.Comp.Log)
        {
            if (now - entry.Time > window)
                continue;
            lines.Add(entry.Text);
        }

        var pos = _transform.GetMapCoordinates(ent);
        var sb = new StringBuilder();
        sb.Append(Loc.GetString("duty-sec-blackbox-header",
            ("name", Identity.Name(ent, EntityManager)),
            ("x", (int) pos.X),
            ("y", (int) pos.Y),
            ("seconds", (int) window.TotalSeconds)));

        var start = Math.Max(0, lines.Count - MaxRadioLines);
        for (var i = start; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Length > MaxRadioLineLength)
                line = line.Substring(0, MaxRadioLineLength) + "…";
            sb.Append(" | ").Append(line);
        }

        if (lines.Count == 0)
            sb.Append(' ').Append(Loc.GetString("duty-sec-blackbox-silence"));

        _radio.SendRadioMessage(ent, sb.ToString(), channel, ent);
    }

    private void OnBodycamPrint(Entity<DutyBodycamImplantComponent> ent, ref ActivateImplantEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;

        var host = args.Performer;
        var now = _timing.CurTime;

        if (!_cyber.IsModuleOnline(ent))
        {
            Deny(host, "duty-sec-bodycam-offline");
            return;
        }

        if (TryComp<DutyCyberwareComponent>(ent, out var cyber)
            && cyber.State == DutyCyberModuleState.Cooldown
            && cyber.CooldownEnd is { } end && end > now)
        {
            Deny(host, "duty-sec-bodycam-cooldown");
            return;
        }

        if (!TryComp<DutyRecorderHostComponent>(host, out var rec) || rec.Log.Count == 0)
        {
            Deny(host, "duty-sec-bodycam-empty");
            return;
        }

        var roundNow = _ticker.RoundDuration();
        var keep = TimeSpan.FromMinutes(Math.Max(ent.Comp.KeepMinutes, 1));
        var sb = new StringBuilder();
        sb.AppendLine(Loc.GetString("duty-sec-bodycam-paper-header", ("name", Identity.Name(host, EntityManager))));
        sb.AppendLine();
        foreach (var entry in rec.Log)
        {
            var age = now - entry.Time;
            if (age > keep)
                continue;

            var stamp = roundNow - age;
            if (stamp < TimeSpan.Zero)
                stamp = TimeSpan.Zero;
            sb.AppendLine($"[{stamp:hh\\:mm\\:ss}] {entry.Text}");
        }

        var paper = Spawn("Paper", _transform.GetMapCoordinates(host));
        _paper.SetContent(paper, sb.ToString());
        _meta.SetEntityName(paper, Loc.GetString("duty-sec-bodycam-paper-name"));
        _hands.PickupOrDrop(host, paper);

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/printer.ogg"), host);
        _popup.PopupEntity(Loc.GetString("duty-sec-bodycam-printed"), host, host);
        _cyber.SetModuleState(ent, DutyCyberModuleState.Cooldown, now + ent.Comp.PrintCooldown);
    }

    private void Deny(EntityUid host, string locKey)
    {
        _popup.PopupEntity(Loc.GetString(locKey), host, host, PopupType.SmallCaution);
        _audio.PlayPvs(DenySound, host);
    }

    #endregion

    #region Access

    private void OnGetAdditionalAccess(Entity<ImplantedComponent> ent, ref GetAdditionalAccessEvent args)
    {
        foreach (var implant in ent.Comp.ImplantContainer.ContainedEntities)
        {
            // Под ЭМИ или выгоревшая печать доступа не даёт.
            if (_sealQuery.HasComp(implant) && _cyber.IsModuleOnline(implant))
                args.Entities.Add(implant);
        }
    }

    #endregion

    #region Scanner

    private void OnScannerInteract(Entity<DutyCyberScannerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !HasComp<MobStateComponent>(target))
            return;

        var doArgs = new DoAfterArgs(EntityManager, args.User, ent.Comp.ScanTime, new DutyCyberScanDoAfterEvent(), ent, target: target, used: ent)
        {
            BreakOnMove = true,
            NeedHand = true,
        };

        if (!_doAfter.TryStartDoAfter(doArgs))
            return;

        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("duty-sec-scan-start", ("user", args.User), ("target", target)), target, args.User);
    }

    private void OnScannerDone(Entity<DutyCyberScannerComponent> ent, ref DutyCyberScanDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        args.Handled = true;
        var user = args.User;
        _audio.PlayPvs(ent.Comp.Sound, ent);

        var lines = _scan.GetImplantLines(new Entity<ImplantedComponent?>(target, null));
        string message;
        if (lines.Count == 0)
        {
            message = Loc.GetString("duty-sec-scan-clean", ("target", target));
        }
        else
        {
            message = Loc.GetString("duty-sec-scan-result", ("target", target), ("modules", string.Join(", ", lines)));
            if (TryComp<DutyCyberHostComponent>(target, out var host))
            {
                message += "\n" + Loc.GetString("duty-sec-scan-load", ("load", host.Load), ("limit", host.LoadLimit));
                if (_cyber.IsHostEmped(target))
                    message += "\n" + Loc.GetString("duty-sec-scan-emp");
            }
        }

        _popup.PopupEntity(Loc.GetString(lines.Count == 0 ? "duty-sec-scan-popup-clean" : "duty-sec-scan-popup-found", ("count", lines.Count)), target, user);
        if (TryComp<ActorComponent>(user, out var actor))
            _chat.DispatchServerMessage(actor.PlayerSession, message);
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;
        _accumulator = 0f;

        var now = _timing.CurTime;

        var lazarus = EntityQueryEnumerator<DutyLazarusImplantComponent, DutyCyberwareComponent>();
        while (lazarus.MoveNext(out var uid, out var comp, out var cyber))
        {
            if (cyber.State == DutyCyberModuleState.Burned)
                continue;

            if (!_cyber.TryGetHost(uid, out var host))
            {
                comp.ReviveAt = null;
                continue;
            }

            if (!_mobState.IsDead(host))
            {
                // Подняли своими силами (или откачал медик) — отсчёт сброшен, модуль снова в готовности.
                if (comp.ReviveAt != null)
                {
                    comp.ReviveAt = null;
                    _cyber.SetModuleState(uid, DutyCyberModuleState.Online, null, string.Empty, cyber);
                }

                continue;
            }

            if (comp.ReviveAt == null)
            {
                comp.ReviveAt = now + comp.Delay;
                _cyber.SetModuleState(uid, DutyCyberModuleState.Active, comp.ReviveAt, Loc.GetString("duty-sec-lazarus-detail-active"), cyber);
                continue;
            }

            // ЭМИ откладывает запуск, но не сжигает имплант.
            if (now < comp.ReviveAt || !_cyber.IsModuleOnline(uid, cyber))
                continue;

            Revive((uid, comp), host);
        }

        var bodycams = EntityQueryEnumerator<DutyBodycamImplantComponent, DutyCyberwareComponent>();
        while (bodycams.MoveNext(out var uid, out _, out var cyber))
        {
            if (cyber.State == DutyCyberModuleState.Cooldown && cyber.CooldownEnd is { } end && end <= now)
                _cyber.SetModuleState(uid, DutyCyberModuleState.Online, null, null, cyber);
        }
    }

    private void Revive(Entity<DutyLazarusImplantComponent> ent, EntityUid host)
    {
        // Те же запреты, что у ванильного дефибриллятора: гниль, бальзамирование, «невозродимые».
        var blocked = _rotting.IsRotten(host) || HasComp<EmbalmedComponent>(host) || HasComp<UnrevivableComponent>(host);
        var revived = false;

        if (!blocked)
        {
            _damageable.TryChangeDamage(host, ent.Comp.Heal, true, origin: host);

            // Если после лечения урон всё ещё за порогом смерти — тело слишком разбито, Лазарь бессилен.
            if (TryComp<MobThresholdsComponent>(host, out var thresholds)
                && _mobThreshold.TryGetThresholdForState(host, MobState.Dead, out var dead, thresholds)
                && _damageable.GetTotalDamage(host) < dead)
            {
                var state = MobState.Critical;
                if (_mobThreshold.TryGetThresholdForState(host, MobState.SoftCritical, out _, thresholds)
                    && _mobThreshold.TryGetThresholdForState(host, MobState.Critical, out var crit, thresholds)
                    && _damageable.GetTotalDamage(host) < crit)
                    state = MobState.SoftCritical;

                _mobState.ChangeMobState(host, state, origin: host);
                revived = true;
            }
        }

        ent.Comp.ReviveAt = null;
        _cyber.SetModuleState(ent, DutyCyberModuleState.Burned, null,
            Loc.GetString(revived ? "duty-sec-lazarus-detail-burned" : "duty-sec-lazarus-detail-failed"));

        if (revived)
        {
            _audio.PlayPvs(ent.Comp.Sound, host);
            _popup.PopupEntity(Loc.GetString("duty-sec-lazarus-revived", ("target", host)), host, PopupType.LargeCaution);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("duty-sec-lazarus-failed"), host, PopupType.MediumCaution);
        }
    }
}

/// <summary>_Duty: одна запись «бодикама» / чёрного ящика.</summary>
public readonly record struct DutyRecorderEntry(TimeSpan Time, string Text);

/// <summary>
/// _Duty: журнал речи и событий вокруг носителя бодикама / чёрного ящика. Только сервер, не сохраняется.
/// </summary>
[RegisterComponent]
public sealed partial class DutyRecorderHostComponent : Component
{
    public readonly List<DutyRecorderEntry> Log = new();
    public TimeSpan Retention = TimeSpan.FromSeconds(30);
    public bool BlackBox;
    public bool AddedListener;
}
