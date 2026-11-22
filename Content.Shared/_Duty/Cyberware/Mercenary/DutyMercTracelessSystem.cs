// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Forensics;
using Content.Shared.Forensics.Components;
using Content.Shared.Forensics.Systems;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Robust.Shared.Timing;

namespace Content.Shared._Duty.Cyberware.Mercenary;

/// <summary>
/// _Duty: «Без следа» в форензике. Отпечатки и волокна ставятся в Shared (с предиктом), поэтому и глушим здесь же:
/// иначе клиент на мгновение «увидит» отпечаток, который сервер не поставил.
/// Включение/выключение шагов и ЭМИ — в серверной системе.
/// </summary>
public sealed partial class DutyMercTracelessSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyMercTracelessComponent, TryAccessFingerprintEvent>(OnTryAccessFingerprint);
        // Волокна оставляют перчатки внутри ApplyEvidence — отменить нельзя, поэтому снимаем их сразу после.
        SubscribeLocalEvent<DutyMercTracelessComponent, ContactInteractionEvent>(OnContact, after: new[] { typeof(ForensicsSystem) });
    }

    /// <summary>Работает ли сейчас маскировка (не сбита ЭМИ).</summary>
    public bool IsMasking(DutyMercTracelessComponent comp)
    {
        return comp.ExposedUntil is not { } until || until <= _timing.CurTime;
    }

    private void OnTryAccessFingerprint(EntityUid uid, DutyMercTracelessComponent comp, TryAccessFingerprintEvent args)
    {
        if (args.Cancelled || !IsMasking(comp))
            return;

        args.Blocker = uid;
        args.Cancel();
    }

    private void OnContact(EntityUid uid, DutyMercTracelessComponent comp, ContactInteractionEvent args)
    {
        if (!IsMasking(comp) || !TryComp<ForensicsComponent>(args.Other, out var forensics))
            return;

        if (!_inventory.TryGetSlotEntity(uid, "gloves", out var gloves)
            || !TryComp<FiberComponent>(gloves, out var fiber)
            || string.IsNullOrEmpty(fiber.FiberMaterial))
            return;

        // Та же строка, что собирает ForensicsSystem.ApplyEvidence.
        var text = string.IsNullOrEmpty(fiber.FiberColor)
            ? Loc.GetString("forensic-fibers", ("material", fiber.FiberMaterial))
            : Loc.GetString("forensic-fibers-colored", ("color", fiber.FiberColor), ("material", fiber.FiberMaterial));

        if (forensics.Fibers.Remove(text))
            Dirty(args.Other, forensics);
    }
}
