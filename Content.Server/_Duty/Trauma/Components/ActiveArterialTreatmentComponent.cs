// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma.UI;

namespace Content.Server._Duty.Trauma.Components;

/// <summary>
/// _Duty: активная сессия лечения артерии, висит на ЛЕЧАЩЕМ (тот, кто открыл окно — сам пациент
/// или другой игрок). Пока компонент есть — лечащий замедлен и его камера приближена. Хранит
/// прогресс по этапам и ссылку на пациента. Снимается при завершении или закрытии окна.
/// </summary>
[RegisterComponent]
public sealed partial class ActiveArterialTreatmentComponent : Component
{
    /// <summary>Пациент, которого лечат (носитель артериального кровотечения).</summary>
    [DataField]
    public EntityUid Patient;

    /// <summary>Текущий требуемый этап.</summary>
    [DataField]
    public ArterialTreatmentStep Step = ArterialTreatmentStep.PalmPress;

    /// <summary>Сколько раз жгут уже затянут (для этапа <see cref="ArterialTreatmentStep.TightenTourniquet"/>).</summary>
    [DataField]
    public int TightenProgress;

    /// <summary>Идёт ли сейчас DoAfter (чтобы не запускать второй параллельно).</summary>
    [DataField]
    public bool Busy;

    /// <summary>Применены ли эффекты лечащего (слоудаун + зум) — чтобы снять их ровно один раз.</summary>
    [DataField]
    public bool EffectsApplied;
}
