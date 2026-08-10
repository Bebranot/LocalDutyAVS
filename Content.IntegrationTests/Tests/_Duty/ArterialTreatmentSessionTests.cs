// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.IntegrationTests.Fixtures;
using Content.Server._Duty.Trauma.Components;
using Content.Server._Duty.Trauma.Systems;
using Content.Shared._Duty.Trauma.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: сессия лечения артерии не должна залипать.
///
/// Сессия висит на лечащем, а условия её жизни — на пациенте, то есть на чужой сущности. Directed-
/// подписок для этого мало: движок не доставляет directed-событие, если компонента уже нет, поэтому
/// при снятии <c>ArterialBleed</c> мимо штатного пути (реджув, смерть пациента, админ-хил) сессия
/// зависала навсегда — вместе со слоудауном −70% и приближённой камерой. Лечится это только
/// проверкой в Update, её и крепим.
/// </summary>
[TestFixture]
[TestOf(typeof(ArterialBleedTreatmentSystem))]
public sealed class ArterialTreatmentSessionTests : GameTest
{
    /// <summary>
    /// Пациент перестал кровить не через завершение лечения — сессия обязана сняться сама.
    /// </summary>
    [Test]
    public async Task SessionDiesWhenPatientStopsBleeding()
    {
        var healer = await Spawn("MobHuman");
        var patient = await Spawn("MobHuman");

        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<ArterialBleedComponent>(patient);

            var session = SEntMan.EnsureComponent<ActiveArterialTreatmentComponent>(healer);
            session.Patient = patient;
        });

        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<ActiveArterialTreatmentComponent>(healer), Is.False,
                "Окно лечения не открыто, значит сессия невалидна и должна была сняться на первом же тике.");
        });
    }

    /// <summary>
    /// Пациента удалили посреди лечения — сессия не должна пережить его и утащить за собой
    /// вечный слоудаун лечащего.
    /// </summary>
    [Test]
    public async Task SessionDiesWhenPatientIsDeleted()
    {
        var healer = await Spawn("MobHuman");
        var patient = await Spawn("MobHuman");

        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<ArterialBleedComponent>(patient);

            var session = SEntMan.EnsureComponent<ActiveArterialTreatmentComponent>(healer);
            session.Patient = patient;

            SEntMan.DeleteEntity(patient);
        });

        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<ActiveArterialTreatmentComponent>(healer), Is.False,
                "Сессия пережила удалённого пациента.");
            Assert.That(SEntMan.Deleted(healer), Is.False,
                "Лечащий не должен пострадать от обрыва сессии.");
        });
    }
}
