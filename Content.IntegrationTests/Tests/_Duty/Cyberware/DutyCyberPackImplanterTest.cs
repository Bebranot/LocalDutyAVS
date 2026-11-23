// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Duty.Cyberware;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Duty.Cyberware;

/// <summary>
/// _Duty: проверка <see cref="DutyCyberPackImplanterComponent"/> — мгновенное вживление гарантированных и случайных
/// имплантов без дубликатов, увеличение лимита нейронагрузки и удаление одноразового имплантера.
/// </summary>
[TestFixture]
public sealed class DutyCyberPackImplanterTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private static List<string> GetImplantIds(IEntityManager entMan, EntityUid target)
    {
        if (!entMan.TryGetComponent<ImplantedComponent>(target, out var implanted))
            return new List<string>();

        return implanted.ImplantContainer.ContainedEntities
            .Select(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID)
            .Where(id => id != null)
            .Cast<string>()
            .ToList();
    }

    [Test]
    public async Task UseInHandImplantsPackIncreasesLimitAndDeletesOneUse()
    {
        EntityUid implanter = default;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            implanter = SEntMan.SpawnEntity("Syringe", coords);
            var comp = SEntMan.AddComponent<DutyCyberPackImplanterComponent>(implanter);
            comp.GuaranteedImplants = new List<EntProtoId>
            {
                "DutySubdermalArmorPlate",
                "DutyCyberAdrenalineImplant",
            };
            comp.RandomPool = new List<EntProtoId>
            {
                "DutyThreatScannerImplant",
                "DutyReflexBlockImplant",
                "DutyStressAnalyzerImplant",
            };
            comp.RandomCount = 2;
            comp.LoadLimitBonus = 3;
            comp.OneUse = true;

            var ev = new UseInHandEvent(SPlayer);
            SEntMan.EventBus.RaiseLocalEvent(implanter, ev);
            Assert.That(ev.Handled, Is.True, "UseInHandEvent должен обработаться.");
        });

        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(implanter), Is.True, "Одноразовый имплантер должен удаляться после использования.");

            var ids = GetImplantIds(SEntMan, SPlayer);
            Assert.That(ids, Does.Contain("DutySubdermalArmorPlate"));
            Assert.That(ids, Does.Contain("DutyCyberAdrenalineImplant"));
            Assert.That(ids.Count, Is.EqualTo(4), "Должно быть 2 гарантированных + 2 случайных импланта.");
            Assert.That(ids.Distinct().Count(), Is.EqualTo(4), "Случайные импланты не должны дублироваться.");

            var host = SEntMan.GetComponent<DutyCyberHostComponent>(SPlayer);
            Assert.That(host.ExtraLoadLimit, Is.EqualTo(3));
            Assert.That(host.LoadLimit, Is.EqualTo(DutyCyberHostComponent.BaseLoadLimit + 3));
        });
    }

    [Test]
    public async Task AfterInteractSkipsDuplicateNonStackableAndStacksArmor()
    {
        await SpawnTarget("MobHuman");
        await RunTicks(3);

        EntityUid implanter = default;

        await Server.WaitPost(() =>
        {
            var implSys = SEntMan.System<SharedSubdermalImplantSystem>();
            implSys.AddImplant(STarget!.Value, "DutyCyberAdrenalineImplant");
            implSys.AddImplant(STarget!.Value, "DutySubdermalArmorPlate");

            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            implanter = SEntMan.SpawnEntity("Syringe", coords);
            var comp = SEntMan.AddComponent<DutyCyberPackImplanterComponent>(implanter);
            comp.GuaranteedImplants = new List<EntProtoId>
            {
                "DutyCyberAdrenalineImplant",
                "DutySubdermalArmorPlate",
            };
            comp.RandomPool = new List<EntProtoId>
            {
                "DutyCyberAdrenalineImplant",
                "DutyThreatScannerImplant",
            };
            comp.RandomCount = 2;
            comp.LoadLimitBonus = 4;
            comp.OneUse = false;

            var ev = new AfterInteractEvent(SPlayer, implanter, STarget, SEntMan.GetComponent<TransformComponent>(STarget!.Value).Coordinates, true);
            SEntMan.EventBus.RaiseLocalEvent(implanter, ev);
            Assert.That(ev.Handled, Is.True, "AfterInteractEvent должен обработаться.");
        });

        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(implanter), Is.False, "Многоразовый имплантер (OneUse = false) не должен удаляться.");

            var ids = GetImplantIds(SEntMan, STarget!.Value);
            Assert.That(ids.Count(id => id == "DutyCyberAdrenalineImplant"), Is.EqualTo(1),
                "Нестакающийся имплант не должен вживляться повторно.");
            Assert.That(ids.Count(id => id == "DutySubdermalArmorPlate"), Is.EqualTo(2),
                "Бронепластина должна стакаться.");
            Assert.That(ids, Does.Contain("DutyThreatScannerImplant"),
                "Уникальный имплант из пула должен вживиться.");

            var host = SEntMan.GetComponent<DutyCyberHostComponent>(STarget!.Value);
            Assert.That(host.ExtraLoadLimit, Is.EqualTo(4));
            Assert.That(host.LoadLimit, Is.EqualTo(DutyCyberHostComponent.BaseLoadLimit + 4));
        });
    }
}
