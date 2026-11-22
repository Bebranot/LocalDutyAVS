// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Cyberware.Surgery;

/// <summary>
/// _Duty: операционный набор для киберимплантов. Ванильный имплантер колет одним уколом —
/// железо под кожей так не ставится: этапы (разрез, вживление, шов) идут цепочкой doafter-ов,
/// пациент должен лежать, а бросивший операцию оставляет рану открытой.
/// Логика — на сервере (<c>DutyImplantSurgeryServerSystem</c>), здесь только данные.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DutyImplantSurgeryKitComponent : Component
{
    /// <summary>Имплант, которым набор заряжается в слот на MapInit (сервер).</summary>
    [DataField]
    public EntProtoId? Implant;

    /// <summary>Этапы вживления. Имплант ложится в тело по окончании предпоследнего этапа (перед швом).</summary>
    [DataField]
    public List<DutySurgeryStage> InsertStages = new();

    /// <summary>Этапы извлечения. Имплант вынимается по окончании предпоследнего этапа (перед швом).</summary>
    [DataField]
    public List<DutySurgeryStage> RemoveStages = new();

    /// <summary>По умолчанию оперировать себя нельзя.</summary>
    [DataField]
    public bool AllowSelf;

    /// <summary>
    /// Множитель времени этапов, если во второй руке хирургический инструмент (тег <c>SurgeryTool</c>:
    /// скальпель, гемостат, каутер…).
    /// </summary>
    [DataField]
    public float ToolSpeedup = 0.7f;

    public const string ContainerId = "duty_surgery_kit_implant";
}

/// <summary>Один этап операции. Побочные эффекты применяются по его завершении.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class DutySurgeryStage
{
    [DataField]
    public float Delay = 3f;

    /// <summary>Попап от третьего лица, аргументы <c>$user</c> и <c>$target</c>. Показывается в начале этапа.</summary>
    [DataField(required: true)]
    public LocId Popup;

    /// <summary>Звук в начале этапа.</summary>
    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public DamageSpecifier? Damage;

    /// <summary>Добавка к кровотечению (отрицательная — закрывает рану).</summary>
    [DataField]
    public float Bleed;

    /// <summary>Если цель в сознании: крик и попап боли. Под наркозом (сон, обморок, крит) — ничего.</summary>
    [DataField]
    public bool Pain;
}

/// <summary>
/// _Duty: поднимается на сущности ИМПЛАНТА перед первым этапом вживления (и повторно перед самой вставкой).
/// Отмена — отказ с попапом <see cref="Reason"/> (пусто — общая причина).
/// </summary>
[ByRefEvent]
public record struct DutyCyberImplantAttemptEvent(EntityUid Target, EntityUid User, bool Cancelled = false, LocId? Reason = null);

/// <summary>Визуал набора: заряжен или пуст.</summary>
[Serializable, NetSerializable]
public enum DutyImplantSurgeryKitVisuals : byte
{
    Loaded
}
