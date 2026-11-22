// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Cyberware.Gorlex;

// Импланты Горлекса: маркер на самом импланте (настройки из прототипа) + маркер на носителе (то, что нужно событиям).
// Носитель-маркеры ставит/снимает серверная система по вживлению/извлечению: так извлечение не сносит чужие компоненты.

#region Кровавый контур

/// <summary>_Duty: «Кровавый контур» — одноразовый подъём из крита с жёстким откатом.</summary>
[RegisterComponent]
public sealed partial class DutyBloodLoopComponent : Component
{
    /// <summary>Сколько носитель держится на ногах.</summary>
    [DataField]
    public TimeSpan ActiveDuration = TimeSpan.FromSeconds(9);

    /// <summary>До какой доли порога крита лечится носитель при подъёме (как у Лазаря).</summary>
    [DataField]
    public float HealToCritFraction = 0.25f;

    /// <summary>Глубина отката: доля пути от порога крита до порога смерти.</summary>
    [DataField]
    public float RollbackDepth = 0.9f;

    /// <summary>Нокдаун при откате.</summary>
    [DataField]
    public TimeSpan RollbackStun = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Успокоительные, гасящие контур: реагент -> сколько единиц в крови нужно.
    /// Галоперидол — лорный «медик NT гасит нервный разгон».
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> Suppressors = new()
    {
        ["Haloperidol"] = 2,
        ["Diphenhydramine"] = 5,
    };
}

public enum DutyBloodLoopPhase : byte
{
    Armed,
    Pending,
    Active,
    Done
}

/// <summary>_Duty: носитель «Кровавого контура». Состояние живёт только на сервере.</summary>
[RegisterComponent]
public sealed partial class DutyBloodLoopHostComponent : Component
{
    [ViewVariables]
    public EntityUid Implant;

    [ViewVariables]
    public DutyBloodLoopPhase Phase = DutyBloodLoopPhase.Armed;

    [ViewVariables]
    public TimeSpan PhaseUntil;
}

#endregion

#region Барабанный демпфер

[RegisterComponent]
public sealed partial class DutyDrumDamperComponent : Component
{
    /// <summary>Во сколько раз режется набор контузии.</summary>
    [DataField]
    public float Reduction = 0.7f;

    /// <summary>Шанс, что реплика шёпота дойдёт до носителя искажённой.</summary>
    [DataField]
    public float WhisperGarbleChance = 0.6f;
}

/// <summary>
/// _Duty: носитель демпфера. Читается и в <c>SharedConcussionSystem.GetProtection</c>, и во флеш-иммунитете,
/// поэтому Shared + networked: клиенту предикт вспышки без рассинхрона.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DutyDrumDamperHostComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Reduction = 0.7f;

    /// <summary>Модуль работает (не под ЭМИ). Под ЭМИ демпфер не защищает, но шёпот всё равно искажён.</summary>
    [DataField, AutoNetworkedField]
    public bool Online = true;

    [DataField]
    public float WhisperGarbleChance = 0.6f;
}

#endregion

#region Смартлинк

[RegisterComponent]
public sealed partial class DutySmartlinkComponent : Component
{
    /// <summary>Множитель разброса/отдачи с работающим смартлинком (&lt;1 — лучше).</summary>
    [DataField]
    public float SpreadMultiplier = 0.55f;

    /// <summary>Множитель под ЭМИ (&gt;1 — хуже обычного).</summary>
    [DataField]
    public float EmpSpreadMultiplier = 1.7f;
}

/// <summary>_Duty: метка оружия, совместимого со смартлинком Горлекса (C-20r, L6 и т.п.). Трофейное оружие NT её не имеет.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DutyGorlexSmartlinkWeaponComponent : Component
{
    /// <summary>Текущий множитель разброса от смартлинка держателя (1 — без эффекта).</summary>
    [DataField, AutoNetworkedField]
    public float Multiplier = 1f;
}

#endregion

#region Термовизор

[RegisterComponent]
public sealed partial class DutyGorlexThermalComponent : Component
{
    [DataField]
    public float Range = 7f;

    /// <summary>Дополнительный расход сытости в секунду, пока режим включён.</summary>
    [DataField]
    public float HungerPerSecond = 0.06f;

    /// <summary>Сколько добавляется к ослеплению от вспышки в активном режиме.</summary>
    [DataField]
    public TimeSpan ExtraFlashDuration = TimeSpan.FromSeconds(6);
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class DutyGorlexThermalHostComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active;

    [DataField, AutoNetworkedField]
    public float Range = 7f;

    [DataField]
    public float HungerPerSecond = 0.06f;

    [DataField]
    public TimeSpan ExtraFlashDuration = TimeSpan.FromSeconds(6);

    [ViewVariables]
    public EntityUid Implant;
}

#endregion

#region Чёрный ящик

[RegisterComponent]
public sealed partial class DutyBlackBoxComponent : Component
{
    /// <summary>Сколько последних «зон» помнит ящик.</summary>
    [DataField]
    public int RouteSize = 8;

    /// <summary>Минимальное смещение (тайлы), после которого пишется новая точка маршрута.</summary>
    [DataField]
    public float MinStep = 4f;

    [DataField]
    public TimeSpan InterrogateTime = TimeSpan.FromSeconds(8);
}

/// <summary>_Duty: носитель ящика. Буфер маршрута и последний обидчик — только сервер.</summary>
[RegisterComponent]
public sealed partial class DutyBlackBoxHostComponent : Component
{
    [ViewVariables]
    public EntityUid Implant;

    [ViewVariables]
    public EntityUid? LastAttacker;

    [ViewVariables]
    public string? LastAttackerName;

    [ViewVariables]
    public List<string> Route = new();

    [ViewVariables]
    public System.Numerics.Vector2 LastPoint;

    [ViewVariables]
    public bool Reported;

    [ViewVariables]
    public bool Interrogated;

    [ViewVariables]
    public int RouteSize = 8;

    [ViewVariables]
    public float MinStep = 4f;

    [ViewVariables]
    public TimeSpan InterrogateTime = TimeSpan.FromSeconds(8);
}

#endregion
