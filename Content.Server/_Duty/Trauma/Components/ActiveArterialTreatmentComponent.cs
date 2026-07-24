// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma.UI;
using Content.Shared.DoAfter;

namespace Content.Server._Duty.Trauma.Components;

/// <summary>
/// _Duty: активная сессия лечения артерии, висит на ЛЕЧАЩЕМ (тот, кто открыл окно — сам пациент
/// или другой игрок). Пока компонент есть — лечащий замедлен и его камера приближена. Хранит
/// прогресс по этапам и ссылку на пациента. Эффекты гарантированно снимаются в ComponentShutdown,
/// поэтому любой путь удаления сессии (завершение, закрытие окна, смерть/дисконнект лечащего)
/// корректно откатывает зум и скорость.
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

    /// <summary>Предмет-материал, зафиксированный на этапе наложения жгута (расходуется по завершении).</summary>
    [ViewVariables]
    public EntityUid? TourniquetItem;

    /// <summary>
    /// Многоразовый жгут-предмет, спрятанный из рук лечащего на время наложения/затягивания —
    /// возвращается в руку по завершении лечения или при отмене сессии. Ткань сюда не попадает
    /// (она расходуется безвозвратно через <see cref="TourniquetItem"/>).
    /// </summary>
    [ViewVariables]
    public EntityUid? StashedTourniquet;

    /// <summary>Текущий активный DoAfter — чтобы отменить его при отмене/закрытии окна.</summary>
    [ViewVariables]
    public DoAfterId? CurrentDoAfter;
}
