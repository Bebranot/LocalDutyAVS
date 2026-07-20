// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Trauma.Components;

/// <summary>Тир тяжести перелома. Эскалирует при повторных сильных тупых ударах по зоне.</summary>
public enum FractureTier : byte
{
    /// <summary>Трещина — лёгкий штраф, заживает сама (медленно).</summary>
    Crack = 1,

    /// <summary>Полный перелом — ощутимый штраф, нужна шина.</summary>
    Full = 2,

    /// <summary>Открытый/оскольчатый — тяжёлый штраф + доп. кровотечение.</summary>
    Open = 3,
}

/// <summary>Состояние перелома одной зоны тела.</summary>
[Serializable, NetSerializable]
public struct FractureZoneState
{
    /// <summary>Текущий тир тяжести.</summary>
    public FractureTier Tier;

    /// <summary>Наложена ли шина (стабилизирует, ускоряет сращивание).</summary>
    public bool Splinted;

    /// <summary>Серверное: время следующего шага пассивного заживления.</summary>
    public TimeSpan NextHeal;

    /// <summary>
    /// Функциональный тир с учётом шины — единая точка для всех систем-потребителей
    /// (движение/атака/выносливость/кашель), чтобы правило «шина снижает тяжесть на один,
    /// трещина в шине эффекта не даёт» не расходилось между ними.
    /// </summary>
    public FractureTier? GetEffectiveTier()
    {
        if (!Splinted)
            return Tier;

        return Tier <= FractureTier.Crack ? null : (FractureTier)((byte)Tier - 1);
    }
}

/// <summary>
/// _Duty: переломы существа по зонам тела. Состояние на мобе (не сущности-части-тела): словарь
/// зона → <see cref="FractureZoneState"/>. Отсутствие ключа = зона цела. Логика — в
/// <c>FractureSystem</c> (применение/эскалация/пассивное заживление/эффекты) и системе лечения
/// (шинирование). Сетевой, т.к. клиент показывает переломы в осмотре и применяет предсказанные
/// эффекты движения/атаки.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FractureComponent : Component
{
    /// <summary>Сломанные зоны и их состояние.</summary>
    [AutoNetworkedField]
    public Dictionary<BodyZone, FractureZoneState> Zones = new();

    /// <summary>Серверное: позиция на прошлом тике эффектов (для урона «при ходьбе»).</summary>
    [ViewVariables]
    public Vector2 LastPosition;

    /// <summary>Серверное: время следующего тика функциональных эффектов.</summary>
    [ViewVariables]
    public TimeSpan NextEffectTick;
}
