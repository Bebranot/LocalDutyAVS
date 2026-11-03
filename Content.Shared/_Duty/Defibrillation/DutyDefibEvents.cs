// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Defibrillation;

/// <summary>Анализ ритма в двухшаговом режиме (40s, первое применение).</summary>
[Serializable, NetSerializable]
public sealed partial class DutyDefibAnalyzeDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Точный анализ ритма verb-ом (45-A).</summary>
[Serializable, NetSerializable]
public sealed partial class DutyDefibRhythmCheckDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Нанесение геля.</summary>
[Serializable, NetSerializable]
public sealed partial class DutyDefibGelDoAfterEvent : SimpleDoAfterEvent;

// Хуки из ванильного SharedDefibrillatorSystem. Поднимаются на сам аппарат, поэтому
// подписка на пару (DutyDefibProfileComponent, событие) ни с чем не пересекается.

/// <summary>
/// Из <c>CanZap</c>: дополнительные отказы (скафандр, живая цель) и разрешение бить живого при кардиоверсии.
/// Срабатывает до списания заряда.
/// </summary>
[ByRefEvent]
public struct DutyDefibCanZapEvent
{
    public readonly EntityUid Target;
    public readonly EntityUid? User;
    public readonly bool TargetCanBeAlive;
    public bool Cancelled;
    public bool AllowAlive;

    public DutyDefibCanZapEvent(EntityUid target, EntityUid? user, bool targetCanBeAlive)
    {
        Target = target;
        User = user;
        TargetCanBeAlive = targetCanBeAlive;
        Cancelled = false;
        AllowAlive = false;
    }
}

/// <summary>
/// Из <c>TryStartZap</c> до <c>CanZap</c>: профиль сам проверяет готовность и запускает анализ или doafter разряда
/// с голосом. <see cref="Handled"/> — ванильный старт пропускается, <c>TryStartZap</c> вернёт <see cref="Result"/>.
/// </summary>
[ByRefEvent]
public struct DutyDefibStartZapEvent
{
    public readonly EntityUid Target;
    public readonly EntityUid User;
    public bool Handled;
    public bool Result;

    public DutyDefibStartZapEvent(EntityUid target, EntityUid user)
    {
        Target = target;
        User = user;
        Handled = false;
        Result = false;
    }
}

/// <summary>Из <c>Zap</c> перед шоком и лечением: множители и дополнительный урон (ожог).</summary>
[ByRefEvent]
public struct DutyDefibZapModifyEvent
{
    public readonly EntityUid Target;
    public readonly EntityUid User;
    public float HealMultiplier;
    public float ShockMultiplier;
    public DamageSpecifier? ExtraDamage;

    /// <summary>Кардиоверсия живого: ванильный блок оживления пропускается.</summary>
    public bool SkipRevive;

    public DutyDefibZapModifyEvent(EntityUid target, EntityUid user)
    {
        Target = target;
        User = user;
        HealMultiplier = 1f;
        ShockMultiplier = 1f;
        ExtraDamage = null;
        SkipRevive = false;
    }
}

/// <summary>Из <c>Zap</c> в самом конце: удар состоялся.</summary>
[ByRefEvent]
public readonly record struct DutyDefibZappedEvent(EntityUid Target, EntityUid User, bool Revived, bool Cardioversion);

public enum DutyDefibAnalysisKind : byte
{
    ShockAdvised,
    NoShock,
    Alive,
    Blocked,

    /// <summary>Разряд поможет, но пациент не в игре (SSD / нет разума): нужен повторный запрос.</summary>
    SoftBlocked,
}

/// <summary>Серверная проверка в середине doafter.</summary>
public enum DutyDefibCheck : byte
{
    /// <summary>Анализ 40s: после «Analyzing» — отмена по пульсу или «Stand clear».</summary>
    AnalysisLead,

    /// <summary>Разряд: после вступления — зарядка или отмена с голосом.</summary>
    ZapLead,
}

/// <summary>
/// Результат анализа ритма. <see cref="Reason"/> — Loc-ключ причины для NoShock/Blocked,
/// <see cref="Deficit"/> — на сколько ещё нужно снизить урон, чтобы разряд помог.
/// </summary>
public readonly record struct DutyDefibAnalysis(
    DutyDefibAnalysisKind Kind,
    string? Reason = null,
    FixedPoint2 Deficit = default,
    bool Arrhythmia = false);
