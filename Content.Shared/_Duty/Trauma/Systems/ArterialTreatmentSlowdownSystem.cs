// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma.Components;
using Content.Shared.Movement.Systems;

namespace Content.Shared._Duty.Trauma.Systems;

/// <summary>
/// _Duty: применяет <see cref="ArterialTreatmentSlowdownComponent"/> к скорости на обеих сторонах,
/// чтобы предсказание движения лечащего совпадало с сервером. Вешает и снимает компонент
/// серверная сессия лечения.
/// </summary>
public sealed partial class ArterialTreatmentSlowdownSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArterialTreatmentSlowdownComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnRefreshSpeed(Entity<ArterialTreatmentSlowdownComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.Modifier);
    }

    public void Apply(EntityUid uid, float modifier)
    {
        var comp = EnsureComp<ArterialTreatmentSlowdownComponent>(uid);
        comp.Modifier = modifier;
        Dirty(uid, comp);
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }

    public void Remove(EntityUid uid)
    {
        // RemComp удаляет сразу, так что пересчёт уже не увидит замедления.
        if (RemComp<ArterialTreatmentSlowdownComponent>(uid))
            _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }
}
