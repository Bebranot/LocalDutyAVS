// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Cyberware.Security;

/// <summary>
/// _Duty: «Рефлекторный блок» на самом импланте. Эффект живёт в <see cref="DutyReflexBlockHostComponent"/> на носителе:
/// события стамина-урона до имплантов не релеятся.
/// </summary>
[RegisterComponent]
public sealed partial class DutyReflexBlockImplantComponent : Component
{
    /// <summary>Множитель стамина-урона (0.7 = −30%).</summary>
    [DataField]
    public float StaminaCoefficient = 0.7f;
}

/// <summary>
/// _Duty: эффект рефлекторного блока на носителе. Свой компонент, а не ванильный StaminaResistance:
/// у моба тот может уже быть от расы, и извлечение импланта снесло бы его.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DutyReflexBlockHostComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Coefficient = 1f;
}

/// <summary>_Duty: «Лазарь» — одноразовая авто-реанимация через <see cref="Delay"/> после смерти носителя.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class DutyLazarusImplantComponent : Component
{
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Лечение при запуске. Как у дефибриллятора (задыхание/кровопотеря), плюс немного грубой регенерации:
    /// тело, убитое тяжёлым уроном, всё равно не поднимется — порог смерти проверяется после лечения.
    /// </summary>
    [DataField(required: true)]
    public DamageSpecifier Heal = default!;

    [DataField]
    public SoundSpecifier Sound = new SoundCollectionSpecifier("DutyLazarusImplantZap");

    /// <summary>Когда носитель умер и запущен отсчёт (только сервер).</summary>
    [AutoPausedField]
    public TimeSpan? ReviveAt;
}

/// <summary>_Duty: «Стресс-анализатор» на импланте.</summary>
[RegisterComponent]
public sealed partial class DutyStressAnalyzerImplantComponent : Component;

/// <summary>_Duty: носитель стресс-анализатора. Сетится: осмотр собирается и на клиенте.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DutyStressAnalyzerHostComponent : Component;

/// <summary>_Duty: «Бодикам» — запись событий вокруг офицера; действие печатает её на бумагу.</summary>
[RegisterComponent]
public sealed partial class DutyBodycamImplantComponent : Component
{
    [DataField]
    public TimeSpan PrintCooldown = TimeSpan.FromSeconds(45);

    /// <summary>Сколько последних минут записи попадает в распечатку.</summary>
    [DataField]
    public int KeepMinutes = 10;
}

/// <summary>_Duty: «Чёрный ящик капитана» — при смерти носителя шлёт в командный канал лог последних секунд.</summary>
[RegisterComponent]
public sealed partial class DutyBlackBoxCaptainImplantComponent : Component
{
    [DataField]
    public TimeSpan Window = TimeSpan.FromSeconds(30);

    [DataField]
    public string Channel = "Command";
}

/// <summary>_Duty: «Нейро-печать» — встроенный доступ. Теги берутся из AccessComponent самого импланта.</summary>
[RegisterComponent]
public sealed partial class DutyNeuroSealImplantComponent : Component;

/// <summary>_Duty: ручной сканер кибернетики СБ.</summary>
[RegisterComponent]
public sealed partial class DutyCyberScannerComponent : Component
{
    [DataField]
    public TimeSpan ScanTime = TimeSpan.FromSeconds(2.5);

    [DataField]
    public SoundSpecifier Sound = new SoundPathSpecifier("/Audio/Items/Medical/healthscanner.ogg");
}
