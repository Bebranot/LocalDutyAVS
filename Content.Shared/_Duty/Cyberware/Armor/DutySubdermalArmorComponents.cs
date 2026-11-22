// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Cyberware.Armor;

/// <summary>
/// _Duty: маркер бронепластины («Панцирь» / «Чешуя») на самой сущности импланта.
/// Сам по себе ничего не делает: эффекты считаются на носителе (<see cref="DutySubdermalArmorComponent"/>),
/// потому что события урона и скорости до имплантов не релеятся.
/// </summary>
[RegisterComponent]
public sealed partial class DutySubdermalArmorPlateComponent : Component
{
    /// <summary>
    /// Множитель шага замедления этой пластины: «Панцирь» 1.0, «Чешуя» 0.5.
    /// </summary>
    [DataField]
    public float SlowdownFactor = 1f;

    /// <summary>
    /// Общий лимит пластин под кожей (и «Панциря», и «Чешуи» вместе).
    /// </summary>
    [DataField]
    public int MaxStacks = 3;
}

/// <summary>
/// _Duty: суммарный эффект бронепластин на носителе. Создаётся и удаляется сервером по числу пластин.
/// Нужен в Shared и сетевой: скорость и урон предиктятся на клиенте.
/// Пресеты ниже — значения по умолчанию, при баланс-правках менять здесь.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DutySubdermalArmorComponent : Component
{
    /// <summary>Сколько пластин сейчас под кожей (1..MaxStacks).</summary>
    [DataField, AutoNetworkedField]
    public int Stacks;

    /// <summary>
    /// Суммарное замедление (0.295 → скорость ×0.705). Считает сервер по отсортированным пластинам,
    /// чтобы порядок вживления не влиял на итог.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float SlowdownTotal;

    /// <summary>Резист ко всему урону от числа пластин (индекс = Stacks - 1). Не складывается, берётся из таблицы.</summary>
    [DataField]
    public List<float> ResistByStacks = new() { 0.15f, 0.28f, 0.40f };

    /// <summary>Шаг замедления для пластины на позиции i (тяжёлые пластины ставятся на первые позиции).</summary>
    [DataField]
    public List<float> SpeedStepByStacks = new() { 0.10f, 0.12f, 0.15f };

    /// <summary>Шанс отразить пулю/хитскан на максимуме пластин.</summary>
    [DataField]
    public float ReflectChanceAtMax = 0.10f;

    /// <summary>Сколько пластин нужно, чтобы открылось отражение.</summary>
    [DataField]
    public int ReflectMinStacks = 3;

    /// <summary>Разброс отражённой пули/луча.</summary>
    [DataField]
    public Angle ReflectSpread = Angle.FromDegrees(45);

    /// <summary>Типы урона, которые броня под кожей не режет: она не спасает лёгкие, кровь и клетки.</summary>
    [DataField]
    public List<ProtoId<DamageTypePrototype>> ExcludedDamageTypes = new() { "Asphyxiation", "Bloodloss", "Cellular" };

    /// <summary>Металлический «дзынь» при отражении.</summary>
    [DataField]
    public SoundSpecifier RicochetSound = new SoundCollectionSpecifier("DutyArmorPlateRicochet");
}
