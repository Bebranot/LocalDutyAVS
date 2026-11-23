// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Cyberware;

/// <summary>
/// _Duty: чьё это железо. От фракции зависят скин HUD киберсистемы и то, как имплант видят сканеры.
/// Cybersun лорно — член Синдиката, но со своим (дорогим, бело-золотым) видом.
/// </summary>
[Serializable, NetSerializable]
public enum DutyCyberFaction : byte
{
    NanoTrasen,
    Gorlex,
    Syndicate,
    Cybersun,
    Mercenary
}

/// <summary>
/// _Duty: состояние модуля для HUD. Offline — ЭМИ или ручное отключение, Burned — одноразовый модуль уже сработал.
/// </summary>
[Serializable, NetSerializable]
public enum DutyCyberModuleState : byte
{
    Online,
    Active,
    Cooldown,
    Offline,
    Burned
}

/// <summary>
/// _Duty: состояние Сандевистана для HUD киберсистемы.
/// </summary>
[Serializable, NetSerializable]
public enum DutySandevistanHudStatus : byte
{
    Ready,
    Active,
    Cooldown,
    Crash,
    Overload
}

/// <summary>
/// _Duty: общий маркер киберимпланта (вешается на сущность импланта рядом с SubdermalImplant).
/// Даёт нейронагрузку, реакцию на ЭМИ и строку «МОДУЛЬ ::» в HUD киберсистемы.
/// Собственная механика импланта живёт в его компоненте; здесь только то, что общее для всех.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DutyCyberwareComponent : Component
{
    [DataField, AutoNetworkedField]
    public DutyCyberFaction Faction = DutyCyberFaction.NanoTrasen;

    /// <summary>Нейронагрузка модуля (1–4). Сумма по телу сравнивается с <see cref="DutyCyberHostComponent.LoadLimit"/>.</summary>
    [DataField]
    public int Load = 1;

    // _Duty: бонус к лимиту нейронагрузки носителя (например, +4 для Сандевистана: 6 + 4 = 10).
    [DataField]
    public int LoadLimitBonus;

    /// <summary>Короткое имя модуля капсом для HUD («ПАНЦИРЬ», «МЕЗОН»).</summary>
    [DataField(required: true)]
    public LocId ModuleName;

    /// <summary>Пассивное железо (бронепластины) ЭМИ не выключает.</summary>
    [DataField]
    public bool EmpSensitive = true;

    /// <summary>
    /// Синдикат и наёмники спиливают маркировку: анализатор показывает «неопознанный модуль».
    /// </summary>
    [DataField]
    public bool Concealed;

    [DataField, AutoNetworkedField]
    public DutyCyberModuleState State = DutyCyberModuleState.Online;

    /// <summary>Конец кулдауна (CurTime) для обратного отсчёта в HUD.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? CooldownEnd;

    /// <summary>Хвост строки HUD после имени («40%», «ЦЕЛЬ 12м»). Пусто — показывается статус.</summary>
    [DataField, AutoNetworkedField]
    public string Detail = string.Empty;
}

/// <summary>
/// _Duty: носитель кибера. Создаётся и удаляется автоматически по первому/последнему импланту
/// с <see cref="DutyCyberwareComponent"/>. Модули дублируются сюда записями: клиенту не нужно
/// видеть сами импланты (они в контейнере и не обязаны доезжать по PVS).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class DutyCyberHostComponent : Component
{
    [AutoNetworkedField]
    public List<DutyCyberModuleEntry> Modules = new();

    [AutoNetworkedField]
    public int Load;

    // _Duty: базовый порог нейронагрузки без модификаторов от имплантов.
    public const int BaseLoadLimit = 6;

    /// <summary>Мягкий потолок: выше — глюки, тремор, боль, но не смерть.</summary>
    [DataField, AutoNetworkedField]
    public int LoadLimit = BaseLoadLimit;

    // _Duty: постоянный бонус к лимиту нейронагрузки от имплантеров-наборов.
    [DataField, AutoNetworkedField]
    public int ExtraLoadLimit;

    /// <summary>Фракция железа, которого в теле больше всего, — она красит HUD.</summary>
    [AutoNetworkedField]
    public DutyCyberFaction Skin = DutyCyberFaction.NanoTrasen;

    /// <summary>До какого момента ЭМИ держит модули выключенными.</summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan? EmpUntil;

    /// <summary>Следующий «тик» эффектов перегруза (только сервер).</summary>
    [DataField, AutoPausedField]
    public TimeSpan NextOverloadTick;

    // _Duty: сетевое состояние Сандевистана и иммуноблокаторов для HUD киберсистемы.
    [AutoNetworkedField]
    public bool SandevistanPresent;

    [AutoNetworkedField]
    public int SandevistanUses;

    [AutoNetworkedField]
    public int SandevistanSafeLimit = 3;

    [AutoNetworkedField]
    public DutySandevistanHudStatus SandevistanStatus = DutySandevistanHudStatus.Ready;

    [AutoNetworkedField, AutoPausedField]
    public TimeSpan? SandevistanCooldownEnd;

    [AutoNetworkedField]
    public bool ImmunoblockerActive;
}

/// <summary>
/// _Duty: строка модуля в HUD. Одинаковые модули (три пластины «Панциря») сворачиваются в одну с <see cref="Count"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed record DutyCyberModuleEntry(
    string NameLoc,
    DutyCyberFaction Faction,
    DutyCyberModuleState State,
    TimeSpan? CooldownEnd,
    string Detail,
    int Count,
    int Load,
    bool EmpSensitive);
