// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Cyberware.Crew;

/// <summary>
/// _Duty: имплант «выдаёт» носителю набор компонентов (HUD, мезоны, защита глаз и т.п.) на время работы.
/// Свои компоненты на носителе нельзя просто класть в implantComponents: у моба такой компонент мог быть от расы,
/// и извлечение импланта снесло бы его. Поэтому здесь добавляются только отсутствующие компоненты,
/// запоминаются и снимаются только они (и при ЭМИ-сбое, и при извлечении).
/// </summary>
[RegisterComponent]
public sealed partial class DutyCyberGrantComponent : Component
{
    /// <summary>Что выдавать носителю (формат как у implantComponents).</summary>
    [DataField(required: true)]
    public ComponentRegistry Components = new();

    /// <summary>Есть ли у импланта переключатель (действие <see cref="DutyCyberGrantToggleEvent"/>).</summary>
    [DataField]
    public bool Toggleable;

    /// <summary>Включён ли модуль сейчас (для переключаемых).</summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>Слепота носителя при ЭМИ-сбое (мезонный глаз захлёбывается статикой). Ноль — без слепоты.</summary>
    [DataField]
    public TimeSpan EmpBlind;

    /// <summary>Какие компоненты добавил именно этот имплант (только сервер).</summary>
    public readonly HashSet<string> Added = new();
}

/// <summary>Маркер «Магнитных ступней» на импланте: после выдачи Magboots донастраивает давление/гравитацию носителя.</summary>
[RegisterComponent]
public sealed partial class DutyMagFeetImplantComponent : Component;

/// <summary>
/// _Duty: замедление носителя магнитными ступнями. Выдаётся имплантом через <see cref="DutyCyberGrantComponent"/>.
/// Сетевой: скорость предиктится на клиенте.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DutyMagFeetSlowComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Multiplier = 0.8f;
}

/// <summary>_Duty: «Нейрочасы NT» — действие показывает время смены.</summary>
[RegisterComponent]
public sealed partial class DutyCrewClockComponent : Component;

/// <summary>_Duty: «Встроенный мультитул» — рука раскрывается в инструмент.</summary>
[RegisterComponent]
public sealed partial class DutyCrewMultitoolComponent : Component
{
    [DataField]
    public EntProtoId Tool = "DutyCyberOmnitool";

    /// <summary>Раскрытый сейчас инструмент (только сервер).</summary>
    public EntityUid? Spawned;
}

/// <summary>_Duty: «Гемостат-железа» — пассивно закрывает кровотечение.</summary>
[RegisterComponent]
public sealed partial class DutyCrewHemostatComponent : Component
{
    /// <summary>Сколько кровотечения снимается в секунду.</summary>
    [DataField]
    public float ReductionPerSecond = 0.25f;
}

/// <summary>_Duty: «Адреналин-железа» — одноразовый стан-брейк.</summary>
[RegisterComponent]
public sealed partial class DutyCrewAdrenalineComponent : Component
{
    [DataField]
    public TimeSpan BoostDuration = TimeSpan.FromSeconds(8);

    [DataField]
    public float SpeedMultiplier = 1.3f;

    /// <summary>Сколько стамина-урона снимается разом.</summary>
    [DataField]
    public float StaminaHeal = 60f;
}

/// <summary>
/// _Duty: временный разгон носителя после адреналина. Сетевой: скорость предиктится.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class DutyAdrenalineBoostComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Multiplier = 1.3f;

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;
}

/// <summary>_Duty: «Анти-ЭМИ экран» — гасит ЭМИ-сбой для всех кибер-модулей тела.</summary>
[RegisterComponent]
public sealed partial class DutyCrewEmpShieldComponent : Component;

/// <summary>_Duty: «Армированный скелет» — часть переломов не случается, остальные мягче.</summary>
[RegisterComponent]
public sealed partial class DutyCrewSkeletonComponent : Component
{
    [DataField]
    public float AbsorbChance = 0.6f;
}

/// <summary>_Duty: «Внутренний диктофон» — запись речи носителя в бумагу.</summary>
[RegisterComponent]
public sealed partial class DutyCrewRecorderComponent : Component
{
    [DataField]
    public int MaxLines = 60;

    public bool Recording;

    public TimeSpan Started;

    public readonly List<string> Lines = new();
}
