// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx. Не переносили TryWashBody (нет частей тела у нас) — душ моет
// только надетую одежду, не «голую кожу».

using Content.Server.Fluids.EntitySystems;
using Content.Server.Popups;
using Content.Shared._Duty.Clothing;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;

namespace Content.Server._Duty.Clothing;

public sealed partial class ShowerSystem : EntitySystem
{
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly ClothingDirtSystem _dirt = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly PuddleSystem _puddle = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    private readonly HashSet<EntityUid> _entities = [];

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShowerComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ShowerComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<ShowerComponent, ExaminedEvent>(OnExamined);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<ShowerComponent>();
        while (query.MoveNext(out var uid, out var shower))
        {
            if (!shower.Enabled || (shower.WashAccumulator += frameTime) < shower.WashInterval)
                continue;
            shower.WashAccumulator %= shower.WashInterval;
            WashArea(uid, shower);
            _puddle.TrySpillAt(uid, new Solution(shower.CleanerReagent, shower.PuddleAmount), out _, sound: false);
        }
    }

    private void OnStartup(Entity<ShowerComponent> ent, ref ComponentStartup args)
        => _appearance.SetData(ent.Owner, ShowerVisuals.Enabled, ent.Comp.Enabled);

    private void OnGetVerbs(Entity<ShowerComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!CanToggle(ent, args.CanAccess, args.CanInteract))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Act = () => TryToggle(ent, user),
            Text = Loc.GetString(ent.Comp.Enabled ? "shower-verb-disable" : "shower-verb-enable"),
            Priority = 2,
        });
    }

    public bool TryToggle(Entity<ShowerComponent> ent, EntityUid user)
    {
        if (!CanToggle(ent, true, true))
            return false;

        ent.Comp.Enabled = !ent.Comp.Enabled;
        ent.Comp.WashAccumulator = 0;
        Dirty(ent);
        _appearance.SetData(ent, ShowerVisuals.Enabled, ent.Comp.Enabled);
        _popup.PopupEntity(Loc.GetString(ent.Comp.Enabled ? "shower-component-switched-on" : "shower-component-switched-off"),
            ent, user, PopupType.Small);
        return true;
    }

    private bool CanToggle(Entity<ShowerComponent> ent, bool canAccess, bool canInteract)
        => canAccess && canInteract && !Deleted(ent);

    private void OnExamined(Entity<ShowerComponent> ent, ref ExaminedEvent args)
        => args.PushMarkup(Loc.GetString(ent.Comp.Enabled ? "shower-component-examine-on" : "shower-component-examine-off"));

    private void WashArea(EntityUid uid, ShowerComponent shower)
    {
        _entities.Clear();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, shower.WashRange, _entities, LookupFlags.Dynamic);
        foreach (var wearer in _entities)
        {
            if (!_inventory.TryGetContainerSlotEnumerator(wearer, out var enumerator, shower.TargetSlots))
                continue;
            while (enumerator.NextItem(out var item))
                _dirt.TryAddCleanerToClothing(item, new ReagentId(shower.CleanerReagent, null), shower.WashAmount);
        }
    }
}
