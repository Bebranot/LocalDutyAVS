// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Server.Chat.Systems;
using Content.Shared._Duty.Cyberware;
using Content.Shared._Duty.Cyberware.Gorlex;
using Content.Shared.Chat;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Robust.Shared.Random;

namespace Content.Server._Duty.Cyberware.Gorlex;

/// <summary>
/// _Duty: «Барабанный демпфер». Маркер на носителе даёт −70% к контузии и иммунитет к вспышке (см. Shared-часть),
/// цена — часть реплик шёпота доходит до носителя искажённой (как будто он слышит их издалека).
/// </summary>
public sealed partial class DutyDrumDamperSystem : EntitySystem
{
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private IRobustRandom _random = default!;

    private EntityQuery<DutyDrumDamperComponent> _damperQuery;
    private EntityQuery<DutyDrumDamperHostComponent> _hostQuery;

    public override void Initialize()
    {
        base.Initialize();

        _damperQuery = GetEntityQuery<DutyDrumDamperComponent>();
        _hostQuery = GetEntityQuery<DutyDrumDamperHostComponent>();

        SubscribeLocalEvent<DutyDrumDamperComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<DutyDrumDamperComponent, ImplantRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<DutyDrumDamperComponent, DutyCyberEmpEvent>(OnEmp);
        SubscribeLocalEvent<DutyDrumDamperComponent, DutyCyberEmpEndedEvent>(OnEmpEnded);

        // Широковещательное событие: готовый хук, в который ADT уже складывает список получателей,
        // поэтому правки апстрим-чата не нужны. Шёпот зовёт его с дальностью WhisperMuffledRange.
        SubscribeLocalEvent<ExpandICChatRecipientsEvent>(OnExpandRecipients);
    }

    private void OnImplanted(Entity<DutyDrumDamperComponent> ent, ref ImplantImplantedEvent args)
    {
        var host = EnsureComp<DutyDrumDamperHostComponent>(args.Implanted);
        host.Reduction = ent.Comp.Reduction;
        host.WhisperGarbleChance = ent.Comp.WhisperGarbleChance;
        host.Online = !_cyber.IsHostEmped(args.Implanted);
        Dirty(args.Implanted, host);
    }

    private void OnRemoved(Entity<DutyDrumDamperComponent> ent, ref ImplantRemovedEvent args)
    {
        // Событие приходит уже после выхода импланта из контейнера: вторая такая же железка в теле остаётся.
        if (_cyber.CountImplants(args.Implanted, e => _damperQuery.HasComp(e)) == 0)
            RemComp<DutyDrumDamperHostComponent>(args.Implanted);
    }

    private void OnEmp(Entity<DutyDrumDamperComponent> ent, ref DutyCyberEmpEvent args)
    {
        SetOnline(args.Host, false);
    }

    private void OnEmpEnded(Entity<DutyDrumDamperComponent> ent, ref DutyCyberEmpEndedEvent args)
    {
        SetOnline(args.Host, true);
    }

    private void SetOnline(EntityUid host, bool online)
    {
        if (!_hostQuery.TryComp(host, out var comp) || comp.Online == online)
            return;

        comp.Online = online;
        Dirty(host, comp);
    }

    private void OnExpandRecipients(ExpandICChatRecipientsEvent args)
    {
        if (args.VoiceRange > SharedChatSystem.WhisperMuffledRange)
            return;

        foreach (var (session, data) in args.Recipients.ToArray())
        {
            if (data.Observer || data.Muffled)
                continue;

            if (session.AttachedEntity is not { } listener || listener == args.Source)
                continue;

            if (!_hostQuery.TryComp(listener, out var host) || !_random.Prob(host.WhisperGarbleChance))
                continue;

            // Muffled у ADT значит «слышит обрывки»: слова приходят с тильдами.
            args.Recipients[session] = data with { Muffled = true };
        }
    }
}
