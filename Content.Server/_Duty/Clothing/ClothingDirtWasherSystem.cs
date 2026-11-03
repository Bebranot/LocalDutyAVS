// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx.

using Content.Server.Popups;
using Content.Shared._Duty.Clothing;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Player;

namespace Content.Server._Duty.Clothing;

/// <summary>_Duty: стирка вещей и умывание у раковины.</summary>
public sealed class ClothingDirtWasherSystem : EntitySystem
{
    [Dependency] private readonly ClothingDirtSystem _dirt = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClothingDirtWasherComponent, AfterInteractUsingEvent>(OnUsing);
        SubscribeLocalEvent<ClothingDirtWasherComponent, WashClothingDoAfterEvent>(OnWash);
        SubscribeLocalEvent<ClothingDirtWasherComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<ClothingDirtWasherComponent, WashBodyDoAfterEvent>(OnWashBody);
    }

    private void OnGetVerbs(Entity<ClothingDirtWasherComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract ||
            !_solutions.TryGetDrainableSolution(ent.Owner, out _, out var solution) || solution.Volume <= 0)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Act = () => TryStartBodyWash(ent, user, BodyWashTarget.Hands),
            Text = Loc.GetString("body-dirt-wash-hands-verb"),
            Priority = 2,
        });
        args.Verbs.Add(new AlternativeVerb
        {
            Act = () => TryStartBodyWash(ent, user, BodyWashTarget.Face),
            Text = Loc.GetString("body-dirt-wash-face-verb"),
            Priority = 2,
        });
    }

    public bool TryStartBodyWash(Entity<ClothingDirtWasherComponent> ent, EntityUid user, BodyWashTarget target)
    {
        if (!_solutions.TryGetDrainableSolution(ent.Owner, out _, out var solution) || solution.Volume <= 0)
            return false;

        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, ent.Comp.WashTime,
            new WashBodyDoAfterEvent(target), ent.Owner, target: ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnUsing(Entity<ClothingDirtWasherComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach || !HasComp<ClothingDirtableComponent>(args.Used) ||
            !_solutions.TryGetDrainableSolution(ent.Owner, out _, out var washer) ||
            washer.GetTotalPrototypeQuantity(ent.Comp.CleanerReagent) <= 0)
            return;

        if (!_doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.WashTime,
                new WashClothingDoAfterEvent(), ent.Owner, target: ent.Owner, used: args.Used)
            { BreakOnMove = true, BreakOnDamage = true, NeedHand = true }))
            return;

        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("clothing-dirt-washing-self",
            ("clothing", Identity.Entity(args.Used, EntityManager))), args.User, args.User, PopupType.Medium);
        _popup.PopupEntity(Loc.GetString("clothing-dirt-washing-others",
                ("user", Identity.Entity(args.User, EntityManager)),
                ("clothing", Identity.Entity(args.Used, EntityManager))),
            args.User, Filter.PvsExcept(args.User), true, PopupType.Medium);
    }

    private void OnWash(Entity<ClothingDirtWasherComponent> ent, ref WashClothingDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Used is not { } clothing ||
            !_solutions.TryGetDrainableSolution(ent.Owner, out var washerEnt, out var washer))
            return;

        var amount = FixedPoint2.Min(ent.Comp.Amount, washer.GetTotalPrototypeQuantity(ent.Comp.CleanerReagent));
        if (!_dirt.TryAddCleanerToClothing(clothing, new ReagentId(ent.Comp.CleanerReagent, null), amount))
            return;

        washer.RemoveReagent(ent.Comp.CleanerReagent, amount, ignoreReagentData: true);
        _solutions.UpdateChemicals(washerEnt.Value);
        args.Handled = true;
    }

    private void OnWashBody(Entity<ClothingDirtWasherComponent> ent, ref WashBodyDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled ||
            !_solutions.TryGetDrainableSolution(ent.Owner, out var washerEnt, out var washer))
            return;

        var amount = FixedPoint2.Min(ent.Comp.Amount, washer.Volume);
        if (amount <= 0)
            return;

        // Вода из раковины — такая же «грязь», только моющая: ClothingDirtSystem сам отработает её
        // как чистящее средство на перчатках / маске.
        var applied = washer.SplitSolution(amount);
        var exposure = args.WashTarget == BodyWashTarget.Hands ? DirtExposure.Hands : DirtExposure.Face;
        _dirt.TryDirtyBody(args.Args.User, applied, amount, exposure);
        _solutions.UpdateChemicals(washerEnt.Value);
        args.Handled = true;
    }
}
