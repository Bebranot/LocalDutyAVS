// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics.CodeAnalysis;
using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Syndicate;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Utility;

namespace Content.Server._Duty.Cyberware.Syndicate;

/// <summary>
/// _Duty: «Подкожный автоинъектор». Действие вводит одну дозу прямо в кровь носителя.
/// Заправка — шприцем/инъектором по носителю (верб), порт при этом виден при осмотре (см. DutyKerenzikovSystem).
/// </summary>
public sealed partial class DutyAutoInjectorSystem : EntitySystem
{
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private EntityQuery<DutyAutoInjectorComponent> _injectorQuery;

    public override void Initialize()
    {
        base.Initialize();

        _injectorQuery = GetEntityQuery<DutyAutoInjectorComponent>();

        SubscribeLocalEvent<DutyAutoInjectorComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<DutyAutoInjectorComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<DutyAutoInjectorComponent, ImplantRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<DutyAutoInjectorComponent, DutyAutoInjectActionEvent>(OnAction);
        SubscribeLocalEvent<DutyAutoInjectorHostComponent, GetVerbsEvent<InteractionVerb>>(OnGetVerbs);
        SubscribeLocalEvent<DutyAutoInjectorHostComponent, DutyAutoInjectorRefillDoAfterEvent>(OnRefill);
    }

    private void OnInit(Entity<DutyAutoInjectorComponent> ent, ref ComponentInit args)
    {
        ent.Comp.Stored = new Solution { MaxVolume = ent.Comp.DoseVolume * ent.Comp.MaxDoses };
        foreach (var (reagent, qty) in ent.Comp.Initial)
        {
            ent.Comp.Stored.AddReagent(reagent.Id, qty);
        }
    }

    private void OnImplanted(Entity<DutyAutoInjectorComponent> ent, ref ImplantImplantedEvent args)
    {
        EnsureComp<DutyAutoInjectorHostComponent>(args.Implanted);
        UpdateDetail(ent);
    }

    private void OnRemoved(Entity<DutyAutoInjectorComponent> ent, ref ImplantRemovedEvent args)
    {
        if (_cyber.CountImplants(args.Implanted, e => _injectorQuery.HasComp(e)) == 0)
            RemComp<DutyAutoInjectorHostComponent>(args.Implanted);
    }

    private int Doses(DutyAutoInjectorComponent comp)
    {
        return comp.DoseVolume <= 0 ? 0 : (int) (comp.Stored.Volume / comp.DoseVolume).Float();
    }

    private void UpdateDetail(Entity<DutyAutoInjectorComponent> ent)
    {
        _cyber.SetModuleDetail(ent, "×" + Doses(ent.Comp));
    }

    private void OnAction(Entity<DutyAutoInjectorComponent> ent, ref DutyAutoInjectActionEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;

        var user = args.Performer;
        if (!_cyber.IsModuleOnline(ent))
        {
            Deny(user, "duty-syndicate-offline");
            return;
        }

        if (ent.Comp.Stored.Volume <= 0)
        {
            Deny(user, "duty-autoinjector-empty");
            return;
        }

        // Последняя неполная доза всё равно вводится целиком: остаток в порту никому не нужен.
        var dose = ent.Comp.Stored.SplitSolution(FixedPoint2.Min(ent.Comp.DoseVolume, ent.Comp.Stored.Volume));
        if (!_bloodstream.TryAddToBloodstream(user, dose))
        {
            // Кровь не приняла (мёртв/нет кровотока) — возвращаем дозу в порт.
            ent.Comp.Stored.AddSolution(dose, null);
            Deny(user, "duty-autoinjector-fail");
            return;
        }

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Items/hypospray.ogg"), user);
        _popup.PopupEntity(Loc.GetString("duty-autoinjector-used"), user, user);
        UpdateDetail(ent);
    }

    #region Заправка

    private void OnGetVerbs(Entity<DutyAutoInjectorHostComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Using is not { } used || !HasComp<Content.Shared.Chemistry.Components.InjectorComponent>(used))
            return;

        if (!TryFindInjector(ent, out _))
            return;

        var user = args.User;
        var target = ent.Owner;
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("duty-autoinjector-verb-refill"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/plus.svg.192dpi.png")),
            Act = () => StartRefill(user, target, used),
        });
    }

    private void StartRefill(EntityUid user, EntityUid host, EntityUid syringe)
    {
        if (!TryFindInjector(host, out var injector))
            return;

        var args = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(injector.Value.Comp.RefillDelay),
            new DutyAutoInjectorRefillDoAfterEvent(), host, target: host, used: syringe)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(args))
            _popup.PopupEntity(Loc.GetString("duty-autoinjector-refill-start"), host, user);
    }

    private void OnRefill(Entity<DutyAutoInjectorHostComponent> ent, ref DutyAutoInjectorRefillDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Used is not { } syringe)
            return;
        args.Handled = true;

        if (!TryComp<Content.Shared.Chemistry.Components.InjectorComponent>(syringe, out var injectorComp)
            || !TryFindInjector(ent, out var injector)
            || !_solutions.ResolveSolution(syringe, injectorComp.SolutionName, ref injectorComp.Solution, out var source))
            return;

        var free = injector.Value.Comp.Stored.MaxVolume - injector.Value.Comp.Stored.Volume;
        if (free <= 0)
        {
            _popup.PopupEntity(Loc.GetString("duty-autoinjector-full"), ent, args.User, PopupType.SmallCaution);
            return;
        }

        var amount = FixedPoint2.Min(free, source.Volume);
        if (amount <= 0 || injectorComp.Solution is not { } sourceEnt)
        {
            _popup.PopupEntity(Loc.GetString("duty-autoinjector-syringe-empty"), ent, args.User, PopupType.SmallCaution);
            return;
        }

        var taken = _solutions.SplitSolution(sourceEnt, amount);
        injector.Value.Comp.Stored.AddSolution(taken, null);
        UpdateDetail(injector.Value);
        _popup.PopupEntity(Loc.GetString("duty-autoinjector-refilled"), ent, args.User);
    }

    private bool TryFindInjector(EntityUid host, [NotNullWhen(true)] out Entity<DutyAutoInjectorComponent>? injector)
    {
        injector = null;
        if (!TryComp<ImplantedComponent>(host, out var implanted))
            return false;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (_injectorQuery.TryComp(implant, out var comp))
            {
                injector = (implant, comp);
                return true;
            }
        }

        return false;
    }

    #endregion

    private void Deny(EntityUid user, string locId)
    {
        _popup.PopupEntity(Loc.GetString(locId), user, user, PopupType.SmallCaution);
        _audio.PlayPvs(new SoundCollectionSpecifier("DutyCyberDeny"), user);
    }
}
