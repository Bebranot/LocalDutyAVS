// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Administration.Logs;
using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Mercenary;
using Content.Shared.Body;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Implants;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Cyberware.Mercenary;

/// <summary>
/// _Duty: «Мимикрия» [упрощено]. 10 с вплотную сканирует лицо → 3 минуты носитель выглядит как цель и зовётся её именем.
/// Голос и ID-карта свои: агент с чужим лицом и своим голосом палится на рации и у турникета.
/// Только для того же вида — кожу ящера на человеке не натянешь (и откат внешности между видами ненадёжен).
/// </summary>
public sealed partial class DutyMercMimicrySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedVisualBodySystem _visualBody = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    private static readonly SoundSpecifier DenySound = new SoundCollectionSpecifier("DutyCyberDeny");
    private static readonly SoundSpecifier ShiftSound = new SoundCollectionSpecifier("DutyMercMimicryShift");

    private const float UpdateInterval = 1f;
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyMercMimicryImplantComponent, DutyMercMimicryActionEvent>(OnAction);
        SubscribeLocalEvent<DutyMercMimicryImplantComponent, DutyMercMimicryDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<DutyMercMimicryImplantComponent, DutyCyberEmpEvent>(OnEmp);
        SubscribeLocalEvent<DutyMercMimicryImplantComponent, ImplantRemovedEvent>(OnRemoved);
    }

    private void OnAction(Entity<DutyMercMimicryImplantComponent> ent, ref DutyMercMimicryActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        var target = args.Target;

        if (!_cyber.IsModuleOnline(ent))
        {
            Deny(user, "duty-merc-module-offline");
            return;
        }

        if (ent.Comp.ActiveUntil != null)
        {
            Deny(user, "duty-merc-mimicry-already");
            return;
        }

        if (TryComp<DutyCyberwareComponent>(ent, out var cyber) && cyber.State == DutyCyberModuleState.Cooldown)
        {
            Deny(user, "duty-merc-module-cooldown");
            return;
        }

        if (target == user
            || !TryComp<HumanoidProfileComponent>(target, out var targetProfile)
            || !TryComp<HumanoidProfileComponent>(user, out var userProfile))
        {
            Deny(user, "duty-merc-mimicry-bad-target");
            return;
        }

        if (targetProfile.Species != userProfile.Species)
        {
            Deny(user, "duty-merc-mimicry-species");
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, user, ent.Comp.ScanTime, new DutyMercMimicryDoAfterEvent(), ent, target, ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            DistanceThreshold = 1.5f,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("duty-merc-mimicry-scan-start"), user, user, PopupType.Small);
        // Контрплей: цель чувствует, что на неё слишком долго и близко смотрят.
        _popup.PopupEntity(Loc.GetString("duty-merc-mimicry-scan-target"), target, target, PopupType.SmallCaution);
    }

    private void OnDoAfter(Entity<DutyMercMimicryImplantComponent> ent, ref DutyMercMimicryDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        args.Handled = true;
        var host = args.User;

        if (!_cyber.IsModuleOnline(ent) || ent.Comp.ActiveUntil != null)
            return;

        if (!_visualBody.TryGatherMarkingsData(host, null, out var profiles, out _, out var applied))
        {
            Deny(host, "duty-merc-mimicry-bad-target");
            return;
        }

        // Снимок своей внешности — глубокой копией: словари маркировок живые и перезапишутся копированием.
        ent.Comp.OriginalProfiles = new Dictionary<ProtoId<OrganCategoryPrototype>, OrganProfileData>(profiles);
        var markings = new Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>>();
        foreach (var (category, layers) in applied)
        {
            var copy = new Dictionary<HumanoidVisualLayers, List<Marking>>();
            foreach (var (layer, list) in layers)
            {
                copy[layer] = new List<Marking>(list);
            }
            markings[category] = copy;
        }
        ent.Comp.OriginalMarkings = markings;
        ent.Comp.OriginalName = Name(host);
        ent.Comp.Host = host;

        _visualBody.CopyAppearanceFrom(target, host);
        _metaData.SetEntityName(host, Name(target));

        var until = _timing.CurTime + ent.Comp.Duration;
        ent.Comp.ActiveUntil = until;
        _cyber.SetModuleState(ent, DutyCyberModuleState.Active, until);

        _audio.PlayPvs(ShiftSound, host);
        _popup.PopupEntity(Loc.GetString("duty-merc-mimicry-on", ("target", target)), host, host, PopupType.Medium);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(host):player} принял облик {ToPrettyString(target):target} имплантом «Мимикрия»");
    }

    private void OnEmp(Entity<DutyMercMimicryImplantComponent> ent, ref DutyCyberEmpEvent args)
    {
        // ЭМИ срывает маску — контрплей СБ против подозрительного «двойника».
        Restore(ent, "duty-merc-mimicry-emp");
    }

    private void OnRemoved(Entity<DutyMercMimicryImplantComponent> ent, ref ImplantRemovedEvent args)
    {
        Restore(ent, null);
    }

    private void Restore(Entity<DutyMercMimicryImplantComponent> ent, string? popup)
    {
        if (ent.Comp.ActiveUntil == null)
            return;

        ent.Comp.ActiveUntil = null;

        if (ent.Comp.Host is { } host && !TerminatingOrDeleted(host))
        {
            if (ent.Comp.OriginalProfiles != null)
                _visualBody.ApplyProfiles(host, ent.Comp.OriginalProfiles);
            if (ent.Comp.OriginalMarkings != null)
                _visualBody.ApplyMarkings(host, ent.Comp.OriginalMarkings);
            if (ent.Comp.OriginalName != null)
                _metaData.SetEntityName(host, ent.Comp.OriginalName);

            _audio.PlayPvs(ShiftSound, host);
            _popup.PopupEntity(Loc.GetString(popup ?? "duty-merc-mimicry-off"), host, host, PopupType.Medium);
        }

        ent.Comp.Host = null;
        ent.Comp.OriginalName = null;
        ent.Comp.OriginalProfiles = null;
        ent.Comp.OriginalMarkings = null;

        _cyber.SetModuleState(ent, DutyCyberModuleState.Cooldown, _timing.CurTime + ent.Comp.Cooldown);
    }

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
        var query = EntityQueryEnumerator<DutyMercMimicryImplantComponent, DutyCyberwareComponent>();
        while (query.MoveNext(out var uid, out var comp, out var cyber))
        {
            if (comp.ActiveUntil is { } until)
            {
                if (until <= now)
                    Restore((uid, comp), null);
                continue;
            }

            if (cyber.State == DutyCyberModuleState.Cooldown && cyber.CooldownEnd is { } end && end <= now)
                _cyber.SetModuleState(uid, DutyCyberModuleState.Online, null, null, cyber);
        }
    }
}
