// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Trauma.UI;

/// <summary>
/// _Duty: ключ BUI пошагового лечения артериального кровотечения. Окно открывается на пациенте
/// (носителе <see cref="Components.ArterialBleedComponent"/>), актором выступает лечащий —
/// сам пациент или другой игрок.
/// </summary>
[Serializable, NetSerializable]
public enum ArterialTreatmentUiKey : byte
{
    Key,
}

/// <summary>
/// _Duty: этап пошагового лечения артерии. Выполняются строго по порядку. Отдельного этапа
/// «завершено» нет: последнее затягивание сразу снимает кровотечение и закрывает окно.
/// </summary>
[Serializable, NetSerializable]
public enum ArterialTreatmentStep : byte
{
    /// <summary>Прижать рану ладонью. Включает слоудаун + зум камеры лечащего.</summary>
    PalmPress,

    /// <summary>Прижать пальцем.</summary>
    FingerPress,

    /// <summary>Наложить самодельный жгут. Требует 2 ткани ИЛИ жгут в руке.</summary>
    ApplyTourniquet,

    /// <summary>Затянуть жгут. Повторяется несколько раз (см. состояние окна).</summary>
    TightenTourniquet,
}

/// <summary>
/// _Duty: сообщение клиент→сервер «выполнить этап X». Сервер сверяет, что это текущий требуемый
/// этап, проверяет условия (ткань/жгут) и запускает соответствующий DoAfter.
/// </summary>
[Serializable, NetSerializable]
public sealed class ArterialTreatmentStepMessage(ArterialTreatmentStep step) : BoundUserInterfaceMessage
{
    public ArterialTreatmentStep Step = step;
}

/// <summary>
/// _Duty: состояние окна лечения. Клиент по нему включает кнопку только текущего этапа и
/// показывает прогресс затягивания жгута.
/// </summary>
[Serializable, NetSerializable]
public sealed class ArterialTreatmentBuiState(
    ArterialTreatmentStep currentStep,
    int tightenProgress,
    int tightenRequired,
    bool busy) : BoundUserInterfaceState
{
    /// <summary>Текущий требуемый этап (кнопка которого активна).</summary>
    public ArterialTreatmentStep CurrentStep = currentStep;

    /// <summary>Сколько раз жгут уже затянут.</summary>
    public int TightenProgress = tightenProgress;

    /// <summary>Сколько всего затягиваний нужно.</summary>
    public int TightenRequired = tightenRequired;

    /// <summary>Идёт ли сейчас DoAfter (кнопки блокируются, чтобы не спамить).</summary>
    public bool Busy = busy;
}
