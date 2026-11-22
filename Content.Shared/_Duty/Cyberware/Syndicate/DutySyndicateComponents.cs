// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Actions;
using Content.Shared.Chemistry.Components;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Radio;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Cyberware.Syndicate;

// Импланты Синдиката и Cybersun. Состояние эффектов — на носителе (свои маркеры), на импланте — только параметры.

#region Пальцы-отмычки

/// <summary>_Duty: «Пальцы-отмычки» — параметры взлома шлюзов и шкафчиков.</summary>
[RegisterComponent]
public sealed partial class DutyLockpickFingersComponent : Component
{
    /// <summary>Сколько секунд занимает аварийное открытие шлюза.</summary>
    [DataField]
    public float DoorDelay = 8f;

    /// <summary>Сколько секунд занимает вскрытие замка шкафчика.</summary>
    [DataField]
    public float LockDelay = 4f;

    /// <summary>Дальность работы пальцев, тайлы.</summary>
    [DataField]
    public float Range = 1.5f;

    [DataField]
    public SoundSpecifier DoorNoise = new SoundCollectionSpecifier("DutyLockpickAlarm");

    [DataField]
    public SoundSpecifier LockNoise = new SoundCollectionSpecifier("DutyLockpickLocker");
}

public sealed partial class DutyLockpickActionEvent : EntityTargetActionEvent
{
}

[Serializable, NetSerializable]
public sealed partial class DutyLockpickDoAfterEvent : SimpleDoAfterEvent
{
}

#endregion

#region Хамелеон-кожа

/// <summary>_Duty: «Хамелеон-кожа» — невидимость при неподвижности.</summary>
[RegisterComponent]
public sealed partial class DutyChameleonSkinComponent : Component
{
    /// <summary>
    /// На сколько в секунду кожа «растворяет» носителя, пока он стоит (ванильный StealthOnMove, отрицательное — прячет).
    /// От полной видимости до почти невидимости — около 4 с.
    /// </summary>
    [DataField]
    public float PassiveVisibilityRate = -0.45f;

    /// <summary>
    /// Насколько каждый пройденный тайл проявляет носителя. Подобрано так, что шагом (~2.5 т/с) он едва проявляется
    /// и остаётся размытым, бегом (~4.5 т/с) — за пару секунд становится виден.
    /// </summary>
    [DataField]
    public float MovementVisibilityRate = 0.2f;
}

/// <summary>_Duty: маркер носителя хамелеон-кожи (состояние стелса, только сервер).</summary>
[RegisterComponent]
public sealed partial class DutyChameleonHostComponent : Component
{
    [ViewVariables]
    public EntityUid Implant;

    [ViewVariables]
    public bool Cloaked;

    /// <summary>Стелс-компонент создали мы, а не раса/одежда, — значит, только мы его и снимаем.</summary>
    [ViewVariables]
    public bool AddedStealth;
}

#endregion

#region Керензиков

/// <summary>_Duty: «Керензиков» — маркер на импланте, параметры живут на носителе.</summary>
[RegisterComponent]
public sealed partial class DutyKerenzikovComponent : Component
{
}

/// <summary>
/// _Duty: носитель «Керензикова» — рефлекторный разгон «треть Сандевистана». Срабатывает сам: от полученного урона
/// или пули, пролетевшей рядом. 8 с носитель быстрее двигается, бьёт и стреляет, потом 20 с контур остывает.
/// Сетевой: на клиенте скорость движения и темп стрельбы предиктятся по флагу <see cref="Active"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class DutyKerenzikovHostComponent : Component
{
    /// <summary>Разгон идёт прямо сейчас.</summary>
    [AutoNetworkedField]
    public bool Active;

    /// <summary>Множитель скорости, темпа атаки и стрельбы (Сандевистан — ×2.4, тут треть прибавки).</summary>
    [DataField, AutoNetworkedField]
    public float Multiplier = 1.47f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(8);

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(20);

    /// <summary>Радиус (тайлы), в котором чужая пуля считается «опасностью рядом».</summary>
    [DataField]
    public float DangerRadius = 2.5f;

    [DataField]
    public SoundSpecifier StartSound = new SoundPathSpecifier("/Audio/ADT/Misc/sande_start.ogg", AudioParams.Default.WithVolume(-6f));

    [DataField]
    public SoundSpecifier EndSound = new SoundPathSpecifier("/Audio/ADT/Misc/sande_end.ogg", AudioParams.Default.WithVolume(-6f));

    [ViewVariables, AutoPausedField]
    public TimeSpan ActiveUntil;

    [ViewVariables, AutoPausedField]
    public TimeSpan CooldownUntil;

    /// <summary>Остывание ещё не объявлено как законченное (чтобы «контур готов» пришёл в чат один раз).</summary>
    [ViewVariables]
    public bool CoolingDown;

    /// <summary>Шлейф добавили мы — только тогда и снимаем.</summary>
    [ViewVariables]
    public bool AddedTrail;
}

/// <summary>_Duty: оружие в руках разогнанного носителя «Керензикова» — стреляет быстрее, пока висит маркер.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DutyKerenzikovBoostedGunComponent : Component
{
    [AutoNetworkedField]
    public float Multiplier = 1f;
}

#endregion

#region Голосовой паразит

[Serializable, NetSerializable]
public enum DutyVoiceParasiteUiKey : byte
{
    Key
}

/// <summary>Клиент выбрал канал для подслушивания.</summary>
[Serializable, NetSerializable]
public sealed class DutyVoiceParasiteSelectMessage(ProtoId<RadioChannelPrototype> channel) : BoundUserInterfaceMessage
{
    public ProtoId<RadioChannelPrototype> Channel = channel;
}

public sealed partial class DutyVoiceParasiteActionEvent : InstantActionEvent
{
}

/// <summary>
/// _Duty: «Голосовой паразит». Сетевой атрибут нужен, чтобы клиентское радиальное меню прочитало список каналов.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DutyVoiceParasiteComponent : Component
{
    [DataField]
    public List<ProtoId<RadioChannelPrototype>> Channels = new();

    [DataField]
    public float DurationSeconds = 300f;

    [DataField]
    public float CooldownSeconds = 600f;

    /// <summary>Интервал «помех» у слушателей канала, секунды (мин/макс).</summary>
    [DataField]
    public float NoiseMin = 40f;

    [DataField]
    public float NoiseMax = 90f;

    [DataField]
    public SoundSpecifier NoiseSound = new SoundCollectionSpecifier("DutyVoiceParasiteNoise");

    // Рантайм (только сервер).
    [ViewVariables]
    public ProtoId<RadioChannelPrototype>? ActiveChannel;

    [ViewVariables]
    public TimeSpan EndTime;

    [ViewVariables]
    public TimeSpan CooldownEndTime;

    [ViewVariables]
    public TimeSpan NextNoise;

    [ViewVariables]
    public bool AddedActiveRadio;

    [ViewVariables]
    public bool AddedIntrinsic;

    [ViewVariables]
    public bool AddedChannel;
}

#endregion

#region Подкожный автоинъектор

public sealed partial class DutyAutoInjectActionEvent : InstantActionEvent
{
}

[Serializable, NetSerializable]
public sealed partial class DutyAutoInjectorRefillDoAfterEvent : SimpleDoAfterEvent
{
}

/// <summary>
/// _Duty: подкожный автоинъектор. Раствор хранится прямо тут (не отдельной сущностью-раствором): ему не нужны ни UI, ни смешивание.
/// </summary>
[RegisterComponent]
public sealed partial class DutyAutoInjectorComponent : Component
{
    /// <summary>Объём одной дозы.</summary>
    [DataField]
    public FixedPoint2 DoseVolume = 10;

    /// <summary>Сколько доз влезает.</summary>
    [DataField]
    public int MaxDoses = 3;

    /// <summary>Начальное содержимое (реагент → количество).</summary>
    [DataField]
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> Initial = new();

    /// <summary>Секунд на заправку порта шприцем.</summary>
    [DataField]
    public float RefillDelay = 4f;

    [ViewVariables]
    public Solution Stored = new();
}

/// <summary>_Duty: носитель автоинъектора — нужен для верба заправки и строки осмотра «виден порт».</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DutyAutoInjectorHostComponent : Component
{
}

#endregion

#region «Ошейник»

public sealed partial class DutyFakeTrackerActionEvent : InstantActionEvent
{
}

[RegisterComponent]
public sealed partial class DutyFakeTrackerComponent : Component
{
}

/// <summary>
/// _Duty: носитель «Ошейника». Пока <see cref="Fake"/> задана, суит-сенсоры (монитор экипажа) показывают носителя в этой точке.
/// </summary>
[RegisterComponent]
public sealed partial class DutyFakeTrackerHostComponent : Component
{
    [ViewVariables]
    public EntityUid Implant;

    [ViewVariables]
    public EntityCoordinates? Fake;
}

/// <summary>
/// _Duty: поднимается на владельце суит-сенсора при расчёте координат (правка в SharedSuitSensorSystem и ADT-аналоге).
/// Подписчик может заменить <see cref="Coordinates"/>.
/// </summary>
[ByRefEvent]
public record struct DutyFakeTrackerQueryEvent(EntityCoordinates Coordinates);

#endregion
