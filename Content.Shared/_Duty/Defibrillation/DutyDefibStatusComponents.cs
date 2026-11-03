// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Defibrillation;

/// <summary>Тюбик электродного геля.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DutyDefibGelComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Uses = 5;

    [DataField]
    public TimeSpan DoAfter = TimeSpan.FromSeconds(2);

    /// <summary>Сколько гель держится на коже, если по цели так и не ударили.</summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(90);
}

/// <summary>На груди цели нанесён гель: лечение от разряда выше, ожога нет. Снимается первым ударом.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class DutyDefibGelledComponent : Component
{
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;
}

/// <summary>
/// История ударов по цели — для штрафа за частые разряды. Сетевой, потому что множители
/// считаются в предсказываемом Zap и на клиенте.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class DutyDefibHistoryComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Count;

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan LastShot;
}

/// <summary>
/// «Аритмия» после разряда на высокой энергии. Пострадавшему не показывается ни алертом, ни значком —
/// её видят только медики в анализаторе здоровья и в анализе ритма LifePak-45-A.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class DutyArrhythmiaComponent : Component
{
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;

    /// <summary>
    /// Добавка удушья в крите раз в секунду. Базовое накопление в крите ≈ столько же,
    /// поэтому итог — примерно вдвое быстрее.
    /// </summary>
    [DataField]
    public DamageSpecifier CritDamage = new()
    {
        DamageDict = new() { { "Asphyxiation", 1 } },
    };

    /// <summary>Шанс «перебоя» в секунду, пока тело живо.</summary>
    [DataField]
    public float SkipChance = 0.02f;

    [DataField]
    public float SkipStaminaDamage = 15f;
}

/// <summary>Послереанимационная слабость: замедление после подъёма дефибриллятором.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class DutyRevivalWeaknessComponent : Component
{
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;

    [DataField, AutoNetworkedField]
    public float WalkModifier = 0.7f;

    [DataField, AutoNetworkedField]
    public float SprintModifier = 0.6f;
}

public enum DutyDefibChestCover : byte
{
    None,

    /// <summary>Броня: 40s теряет часть лечения, 45-A не замечает.</summary>
    Reduce,

    /// <summary>Скафандр: электроды не достают до кожи.</summary>
    Block,
}

/// <summary>
/// Маркер на верхней одежде: как она мешает электродам. Броня без маркера определяется по
/// <c>ArmorComponent</c> как <see cref="DutyDefibChestCover.Reduce"/>; скафандры — только маркером
/// (<c>PressureProtectionComponent</c> серверный, в Shared его нет).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DutyDefibChestCoverComponent : Component
{
    [DataField]
    public DutyDefibChestCover Cover = DutyDefibChestCover.Block;
}
