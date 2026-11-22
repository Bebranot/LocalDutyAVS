// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Server.Antag;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.GameTicking.Rules;
using Content.Server.Station.Systems;
using Content.Shared._Duty.Cyberware.Mercenary;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Implants;
using Content.Shared.Inventory;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Duty.Cyberware.Mercenary;

/// <summary>
/// _Duty: правило контракта наёмника. Выбор игрока и гост-роль — ванильный AntagSelection (как у ниндзя),
/// здесь только: точка в космосе у станции цели, «упаковка» тела (скафандр, баллоны, импланты) и привязка цели.
/// </summary>
public sealed partial class DutyMercenaryRuleSystem : GameRuleSystem<DutyMercenaryRuleComponent>
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private SharedSubdermalImplantSystem _implants = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private InternalsSystem _internals = default!;
    [Dependency] private GasTankSystem _gasTank = default!;
    [Dependency] private AntagSelectionSystem _antag = default!;
    [Dependency] private DutyMercContractSystem _contract = default!;

    private static readonly Color BriefingColor = Color.FromHex("#7CFF9B");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyMercenaryRuleComponent, AntagSelectLocationEvent>(OnSelectLocation);
        SubscribeLocalEvent<DutyMercenaryRuleComponent, AfterAntagEntitySelectedEvent>(OnAfterAntagSelected);
    }

    /// <summary>
    /// Привязать цель и выбрать точку спавна. Зовётся командой между AddGameRule и StartGameRule:
    /// гост-роль ставится на старте правила и уже должна знать, где висеть.
    /// </summary>
    public void SetupContract(EntityUid rule, EntityUid targetMind, EntityUid targetBody, string targetName)
    {
        if (!RuleQuery.TryComp(rule, out var comp))
            return;

        comp.TargetMind = targetMind;
        comp.TargetName = targetName;
        comp.Coords = PickSpawn(targetBody, comp.SpawnDistance);
    }

    private MapCoordinates PickSpawn(EntityUid body, float extraDistance)
    {
        var bodyXform = Transform(body);

        // Станция цели, а не случайная: наёмник прилетает туда, где цель.
        EntityUid? grid = null;
        if (_station.GetOwningStation(body, bodyXform) is { } station)
            grid = _station.GetLargestGrid(station);
        grid ??= bodyXform.GridUid;

        var direction = RobustRandom.NextAngle().ToVec();

        if (grid is { } gridUid && TryComp<MapGridComponent>(gridUid, out var gridComp))
        {
            var gridXform = Transform(gridUid);
            var center = Vector2.Transform(gridComp.LocalAABB.Center, _transform.GetWorldMatrix(gridUid));
            var distance = gridComp.LocalAABB.Size.Length() / 2f + extraDistance;
            return new MapCoordinates(center + direction * distance, gridXform.MapID);
        }

        var bodyPos = _transform.GetMapCoordinates(body, bodyXform);
        return new MapCoordinates(bodyPos.Position + direction * (extraDistance + 15f), bodyPos.MapId);
    }

    private void OnSelectLocation(Entity<DutyMercenaryRuleComponent> ent, ref AntagSelectLocationEvent args)
    {
        if (ent.Comp.Coords is { } coords)
            args.Coordinates.Add(coords);
    }

    private void OnAfterAntagSelected(Entity<DutyMercenaryRuleComponent> ent, ref AfterAntagEntitySelectedEvent args)
    {
        var mob = args.EntityUid;
        ent.Comp.Mercenary = mob;

        SuitUp(mob);

        EntityUid? contract = null;
        foreach (var proto in ent.Comp.Implants)
        {
            if (_implants.AddImplant(mob, proto) is not { } implant)
                continue;

            if (contract == null && HasComp<DutyMercContractImplantComponent>(implant))
                contract = implant;
        }

        if (contract is { } contractImplant && ent.Comp.TargetMind is { } targetMind)
            _contract.AssignContract(contractImplant, mob, targetMind, ent.Comp.TargetName, ent.Comp.Objective);

        _antag.SendBriefing(args.Session,
            Loc.GetString("duty-merc-briefing", ("target", ent.Comp.TargetName)),
            BriefingColor,
            null);
    }

    /// <summary>
    /// Наёмник появляется в открытом космосе: шлем скафандра поднят, баллон подключён.
    /// Иначе первые секунды раунда для него — удушье, а не работа.
    /// </summary>
    private void SuitUp(EntityUid mob)
    {
        if (_inventory.TryGetSlotEntity(mob, "outerClothing", out var suit)
            && TryComp<ToggleableClothingComponent>(suit, out var toggleable)
            && toggleable.ClothingUid is { } helmet
            && toggleable.Container is { } container
            && container.Contains(helmet))
        {
            RaiseLocalEvent(suit.Value, new ToggleClothingEvent { Performer = mob });
        }

        if (_internals.FindBestGasTank(mob) is { } tank)
            _gasTank.ConnectToInternals(tank, mob);
    }
}
