// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Administration.Logs;
using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Mercenary;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Jittering;
using Content.Shared.Mindshield.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Cyberware.Mercenary;

/// <summary>
/// _Duty: «Иглоноготь» (яд при захвате) и «Протокол стирания» (расплавить кибер в плену).
/// Оба — про ближний контакт и плен, поэтому в одной системе.
/// </summary>
public sealed partial class DutyMercCaptureSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private DutyMercContractSystem _contract = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    private static readonly SoundSpecifier DenySound = new SoundCollectionSpecifier("DutyCyberDeny");
    private static readonly SoundSpecifier EraseSound = new SoundCollectionSpecifier("DutyMercErasure");

    private const float UpdateInterval = 1f;
    private float _accumulator;

    private EntityQuery<DutyMercNeedleNailImplantComponent> _needleQuery;
    private EntityQuery<DutyCyberwareComponent> _cyberQuery;

    public override void Initialize()
    {
        base.Initialize();

        _needleQuery = GetEntityQuery<DutyMercNeedleNailImplantComponent>();
        _cyberQuery = GetEntityQuery<DutyCyberwareComponent>();

        SubscribeLocalEvent<DutyMercNeedleNailImplantComponent, ImplantImplantedEvent>(OnNeedleImplanted);
        SubscribeLocalEvent<DutyMercNeedleNailImplantComponent, ImplantRemovedEvent>(OnNeedleRemoved);
        SubscribeLocalEvent<DutyMercNeedleNailHostComponent, PullStartedMessage>(OnPullStarted);
        SubscribeLocalEvent<DutyMercNeedleMarkComponent, ExaminedEvent>(OnMarkExamined);

        SubscribeLocalEvent<DutyMercErasureImplantComponent, DutyMercErasureActionEvent>(OnErase);
    }

    #region Иглоноготь

    private void OnNeedleImplanted(Entity<DutyMercNeedleNailImplantComponent> ent, ref ImplantImplantedEvent args)
    {
        EnsureComp<DutyMercNeedleNailHostComponent>(args.Implanted);
    }

    private void OnNeedleRemoved(Entity<DutyMercNeedleNailImplantComponent> ent, ref ImplantRemovedEvent args)
    {
        // Имплант уже вне контейнера: если других игл нет — снимаем маркер.
        if (FindNeedle(args.Implanted, false) == null)
            RemComp<DutyMercNeedleNailHostComponent>(args.Implanted);
    }

    private EntityUid? FindNeedle(EntityUid host, bool onlineOnly)
    {
        if (!TryComp<ImplantedComponent>(host, out var implanted))
            return null;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (!_needleQuery.HasComp(implant))
                continue;

            if (!onlineOnly || _cyber.IsModuleOnline(implant))
                return implant;
        }

        return null;
    }

    private void OnPullStarted(EntityUid uid, DutyMercNeedleNailHostComponent comp, PullStartedMessage args)
    {
        // Событие приходит и тянущему, и тянущемуся — колем только когда хватает носитель.
        if (args.PullerUid != uid || args.PulledUid == uid)
            return;

        var victim = args.PulledUid;
        if (FindNeedle(uid, true) is not { } needle || !_needleQuery.TryComp(needle, out var needleComp))
            return;

        var solution = new Solution(needleComp.Reagent, needleComp.Amount);
        if (!_bloodstream.TryAddToBloodstream(victim, solution))
            return;

        EnsureComp<DutyMercNeedleMarkComponent>(victim);
        _cyber.SetModuleState(needle, DutyCyberModuleState.Burned, null, Loc.GetString("duty-merc-hud-spent"));

        _popup.PopupEntity(Loc.GetString("duty-merc-needle-user"), uid, uid, PopupType.Small);
        _popup.PopupEntity(Loc.GetString("duty-merc-needle-victim"), victim, victim, PopupType.SmallCaution);
        _adminLog.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(uid):player} ввёл {needleComp.Amount}u {needleComp.Reagent} в {ToPrettyString(victim):target} имплантом «Иглоноготь»");
    }

    private void OnMarkExamined(Entity<DutyMercNeedleMarkComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("duty-merc-needle-mark-examine"));
    }

    #endregion

    #region Протокол стирания

    private void OnErase(Entity<DutyMercErasureImplantComponent> ent, ref DutyMercErasureActionEvent args)
    {
        if (args.Handled)
            return;

        var host = args.Performer;

        if (TryComp<DutyCyberwareComponent>(ent, out var cyber) && cyber.State == DutyCyberModuleState.Burned)
        {
            Deny(host, "duty-merc-erasure-burned");
            return;
        }

        // Майндшилд, вставленный СБ, перехватывает управление железом — окно для допроса.
        if (HasComp<MindShieldComponent>(host))
        {
            Deny(host, "duty-merc-erasure-mindshield");
            return;
        }

        if (!_cyber.IsModuleOnline(ent))
        {
            Deny(host, "duty-merc-module-offline");
            return;
        }

        var now = _timing.CurTime;
        if (ent.Comp.CuffedSince is not { } since || now - since < ent.Comp.CuffTime)
        {
            Deny(host, "duty-merc-erasure-not-ready");
            return;
        }

        args.Handled = true;

        // Плавим всё наёмничье железо, включая сам протокол.
        if (TryComp<ImplantedComponent>(host, out var implanted))
        {
            foreach (var implant in new List<EntityUid>(implanted.ImplantContainer.ContainedEntities))
            {
                if (_cyberQuery.TryComp(implant, out var implantCyber) && implantCyber.Faction == DutyCyberFaction.Mercenary)
                    _cyber.SetModuleState(implant, DutyCyberModuleState.Burned, null, Loc.GetString("duty-merc-hud-melted"), implantCyber);
            }
        }

        if (_contract.TryGetContract(host, out var contract))
        {
            _contract.Terminate(contract, host, "duty-merc-erasure-done", notify: false);
            // Заказчика и цель больше не вытащить даже из списка целей.
            if (contract.Comp.Objective is { } objective)
                _metaData.SetEntityName(objective, Loc.GetString("duty-merc-objective-erased"));
        }

        _damageable.TryChangeDamage(host, ent.Comp.Damage, ignoreResistances: true, origin: host);
        _jitter.DoJitter(host, TimeSpan.FromSeconds(8), true, 16f, 6f);
        _audio.PlayPvs(EraseSound, host);
        _popup.PopupEntity(Loc.GetString("duty-merc-erasure-others", ("user", host)), host, PopupType.LargeCaution);

        _adminLog.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(host):player} активировал «Протокол стирания»");
    }

    #endregion

    private void Deny(EntityUid user, string loc)
    {
        _popup.PopupEntity(Loc.GetString(loc), user, user, PopupType.SmallCaution);
        _audio.PlayGlobal(DenySound, user);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;
        _accumulator = 0f;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<DutyMercErasureImplantComponent, DutyCyberwareComponent, SubdermalImplantComponent>();
        while (query.MoveNext(out var uid, out var erasure, out var cyber, out var sub))
        {
            if (cyber.State == DutyCyberModuleState.Burned || sub.ImplantedEntity is not { } host)
                continue;

            var cuffed = TryComp<CuffableComponent>(host, out var cuffable) && cuffable.CuffedHandCount > 0;
            if (!cuffed)
            {
                if (erasure.CuffedSince != null)
                {
                    erasure.CuffedSince = null;
                    _cyber.SetModuleDetail(uid, string.Empty, cyber);
                }
                continue;
            }

            erasure.CuffedSince ??= now;
            var left = erasure.CuffTime - (now - erasure.CuffedSince.Value);
            var detail = left <= TimeSpan.Zero
                ? Loc.GetString("duty-merc-hud-erasure-ready")
                : $"{(int) left.TotalMinutes}:{left.Seconds:00}";
            _cyber.SetModuleDetail(uid, detail, cyber);
        }
    }
}
