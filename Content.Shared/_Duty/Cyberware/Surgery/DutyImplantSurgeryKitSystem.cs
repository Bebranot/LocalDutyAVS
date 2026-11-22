// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics.CodeAnalysis;
using Content.Shared.Examine;
using Robust.Shared.Containers;

namespace Content.Shared._Duty.Cyberware.Surgery;

/// <summary>
/// _Duty: общая для клиента и сервера часть набора — слот с имплантом и текст осмотра.
/// Сами операции живут на сервере.
/// </summary>
public sealed partial class DutyImplantSurgeryKitSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyImplantSurgeryKitComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<DutyCyberwareComponent, ExaminedEvent>(OnImplantExamined);
    }

    /// <summary>
    /// Строка «Как работает» из ключа <c>duty-cyber-usage-&lt;Id прототипа&gt;</c>. Лорное описание отвечает на «что это»,
    /// эта строка — на «что нажать и чего ждать»: без неё импланты из админ-выдачи непонятны.
    /// </summary>
    private void PushUsage(EntityUid implant, ExaminedEvent args)
    {
        if (MetaData(implant).EntityPrototype is not { } proto ||
            !Loc.TryGetString($"duty-cyber-usage-{proto.ID}", out var usage))
            return;

        args.PushMarkup(Loc.GetString("duty-cyber-usage-wrap", ("text", usage)));
    }

    private void OnImplantExamined(Entity<DutyCyberwareComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            PushUsage(ent, args);
    }

    /// <summary>Имплант, лежащий сейчас в слоте набора.</summary>
    public bool TryGetLoadedImplant(EntityUid kit, [NotNullWhen(true)] out EntityUid? implant)
    {
        implant = null;
        if (!_container.TryGetContainer(kit, DutyImplantSurgeryKitComponent.ContainerId, out var container)
            || container.ContainedEntities.Count == 0)
        {
            return false;
        }

        implant = container.ContainedEntities[0];
        return true;
    }

    private void OnExamined(Entity<DutyImplantSurgeryKitComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!TryGetLoadedImplant(ent, out var implant))
        {
            args.PushMarkup(Loc.GetString("duty-surgery-kit-examine-empty"));
            return;
        }

        // Синдикатовское железо без маркировки не опознать на глаз.
        var concealed = TryComp<DutyCyberwareComponent>(implant, out var cyber) && cyber.Concealed;
        var name = concealed
            ? Loc.GetString("duty-surgery-kit-unidentified")
            : MetaData(implant.Value).EntityName;

        args.PushMarkup(Loc.GetString("duty-surgery-kit-examine-loaded", ("implant", name)));
        args.PushMarkup(Loc.GetString("duty-surgery-kit-examine-howto"));
        PushUsage(implant.Value, args);
    }
}
