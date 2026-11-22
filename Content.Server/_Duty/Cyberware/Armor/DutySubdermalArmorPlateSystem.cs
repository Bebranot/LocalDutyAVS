// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Cyberware.Armor;
using Content.Shared._Duty.Cyberware.Surgery;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Popups;

namespace Content.Server._Duty.Cyberware.Armor;

/// <summary>
/// _Duty: серверная часть бронепластин — считает стопку на носителе, обновляет строку HUD,
/// сообщает о вживлении и отказывает на пластине сверх лимита.
/// </summary>
public sealed partial class DutySubdermalArmorPlateSystem : EntitySystem
{
    [Dependency] private DutyCyberwareSystem _cyber = default!;
    [Dependency] private DutySubdermalArmorSystem _armor = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private EntityQuery<DutySubdermalArmorPlateComponent> _plateQuery;

    // Буфер для сортировки пластин по тяжести; система однопоточная.
    private readonly List<float> _factors = new();

    private static readonly string[] InstallPopups =
    {
        "duty-armor-plate-installed-1",
        "duty-armor-plate-installed-2",
        "duty-armor-plate-installed-3",
    };

    public override void Initialize()
    {
        base.Initialize();

        _plateQuery = GetEntityQuery<DutySubdermalArmorPlateComponent>();

        SubscribeLocalEvent<DutySubdermalArmorPlateComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<DutySubdermalArmorPlateComponent, ImplantRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<DutySubdermalArmorPlateComponent, DutyCyberImplantAttemptEvent>(OnAttempt);
    }

    private void OnAttempt(Entity<DutySubdermalArmorPlateComponent> ent, ref DutyCyberImplantAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (CountPlates(args.Target) < ent.Comp.MaxStacks)
            return;

        args.Cancelled = true;
        args.Reason = "duty-armor-plate-no-room";
    }

    private void OnImplanted(Entity<DutySubdermalArmorPlateComponent> ent, ref ImplantImplantedEvent args)
    {
        var stacks = Recalculate(args.Implanted);

        if (stacks > 0)
        {
            var index = Math.Min(stacks, InstallPopups.Length) - 1;
            _popup.PopupEntity(Loc.GetString(InstallPopups[index]), args.Implanted, args.Implanted, PopupType.Medium);
        }
    }

    private void OnRemoved(Entity<DutySubdermalArmorPlateComponent> ent, ref ImplantRemovedEvent args)
    {
        // Событие приходит уже после выхода импланта из контейнера: пересчёт его не учтёт.
        Recalculate(args.Implanted);
    }

    private int CountPlates(EntityUid host)
    {
        if (!TryComp<ImplantedComponent>(host, out var implanted))
            return 0;

        var count = 0;
        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (_plateQuery.HasComp(implant))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Пересобрать компонент носителя по пластинам в контейнере. Возвращает число пластин.
    /// </summary>
    private int Recalculate(EntityUid host)
    {
        _factors.Clear();
        var plates = new List<EntityUid>();

        if (TryComp<ImplantedComponent>(host, out var implanted))
        {
            foreach (var implant in implanted.ImplantContainer.ContainedEntities)
            {
                if (!_plateQuery.TryComp(implant, out var plate))
                    continue;

                plates.Add(implant);
                _factors.Add(plate.SlowdownFactor);
            }
        }

        if (plates.Count == 0)
        {
            if (RemComp<DutySubdermalArmorComponent>(host))
                _armor.RefreshSpeed(host);
            return 0;
        }

        var comp = EnsureComp<DutySubdermalArmorComponent>(host);

        // Тяжёлые пластины — на первые (меньшие) позиции шага: порядок вживления не влияет на итог.
        _factors.Sort((a, b) => b.CompareTo(a));
        var slowdown = 0f;
        for (var i = 0; i < _factors.Count; i++)
        {
            var step = comp.SpeedStepByStacks.Count == 0
                ? 0f
                : comp.SpeedStepByStacks[Math.Min(i, comp.SpeedStepByStacks.Count - 1)];
            slowdown += step * _factors[i];
        }

        comp.Stacks = plates.Count;
        comp.SlowdownTotal = slowdown;
        Dirty(host, comp);

        _armor.RefreshSpeed(host);

        // Строка HUD: общий резист на каждой пластине, HUD сам свернёт их в «ПАНЦИРЬ ×N».
        var detail = $"{(int) MathF.Round(DutySubdermalArmorSystem.GetResist(comp) * 100f)}%";
        foreach (var plate in plates)
        {
            _cyber.SetModuleDetail(plate, detail);
        }

        return plates.Count;
    }
}
