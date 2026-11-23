// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Cyberware;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Duty.Cyberware;

/// <summary>
/// _Duty: мгновенное вживление набора киберимплантов в 1 клик (на себя или на существо рядом) и увеличение лимита перегруза.
/// </summary>
public sealed class DutyCyberPackImplanterSystem : EntitySystem
{
    [Dependency] private readonly SharedSubdermalImplantSystem _implants = default!;
    [Dependency] private readonly DutyCyberwareSystem _cyberware = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private static readonly HashSet<string> StackableImplants = new()
    {
        "DutySubdermalArmorPlate",
        "DutySubdermalArmorPlateElite",
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyCyberPackImplanterComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<DutyCyberPackImplanterComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnUseInHand(Entity<DutyCyberPackImplanterComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || TerminatingOrDeleted(ent))
            return;

        if (!HasComp<MobStateComponent>(args.User))
            return;

        ApplyPack(ent, args.User, args.User);
        args.Handled = true;
    }

    private void OnAfterInteract(Entity<DutyCyberPackImplanterComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || TerminatingOrDeleted(ent))
            return;

        if (!HasComp<MobStateComponent>(target))
            return;

        ApplyPack(ent, args.User, target);
        args.Handled = true;
    }

    private void ApplyPack(Entity<DutyCyberPackImplanterComponent> ent, EntityUid user, EntityUid target)
    {
        var comp = ent.Comp;
        var existingImplants = CollectExistingImplants(target);
        var implantedCount = 0;

        foreach (var protoId in comp.GuaranteedImplants)
        {
            if (TryImplantUnique(target, protoId, existingImplants))
                implantedCount++;
        }

        if (comp.RandomCount > 0 && comp.RandomPool.Count > 0)
        {
            var candidates = BuildRandomCandidates(comp.RandomPool, existingImplants);
            var picks = Math.Min(comp.RandomCount, candidates.Count);

            for (var i = 0; i < picks; i++)
            {
                var idx = _random.Next(candidates.Count);
                var protoId = candidates[idx];
                candidates.RemoveAt(idx);

                if (TryImplantUnique(target, protoId, existingImplants))
                    implantedCount++;
            }
        }

        if (comp.LoadLimitBonus != 0)
            _cyberware.AddLoadLimitBonus(target, comp.LoadLimitBonus);

        if (comp.InjectSound != null)
            _audio.PlayPvs(comp.InjectSound, target);

        _popup.PopupEntity(
            Loc.GetString("duty-cyber-pack-implanted", ("count", implantedCount), ("bonus", comp.LoadLimitBonus)),
            target,
            user);

        if (comp.OneUse)
            QueueDel(ent);
    }

    private HashSet<string> CollectExistingImplants(EntityUid target)
    {
        var existing = new HashSet<string>();
        if (!TryComp<ImplantedComponent>(target, out var implanted))
            return existing;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (MetaData(implant).EntityPrototype?.ID is { } id)
                existing.Add(id);
        }

        return existing;
    }

    private static List<EntProtoId> BuildRandomCandidates(List<EntProtoId> pool, HashSet<string> existing)
    {
        var seen = new HashSet<string>();
        var candidates = new List<EntProtoId>(pool.Count);

        foreach (var protoId in pool)
        {
            if (!seen.Add(protoId.Id))
                continue;

            if (!StackableImplants.Contains(protoId.Id) && existing.Contains(protoId.Id))
                continue;

            candidates.Add(protoId);
        }

        return candidates;
    }

    private bool TryImplantUnique(EntityUid target, EntProtoId protoId, HashSet<string> existing)
    {
        if (!StackableImplants.Contains(protoId.Id) && existing.Contains(protoId.Id))
            return false;

        if (_implants.AddImplant(target, protoId) == null)
            return false;

        existing.Add(protoId.Id);
        return true;
    }
}
