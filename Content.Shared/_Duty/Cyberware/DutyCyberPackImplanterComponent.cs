// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Cyberware;

/// <summary>
/// _Duty: одноразовый (или многоразовый) имплантер-набор для быстрого вживления комплекта киберимплантов в 1 клик.
/// </summary>
[RegisterComponent]
public sealed partial class DutyCyberPackImplanterComponent : Component
{
    /// <summary>Импланты, которые вживляются гарантированно.</summary>
    [DataField]
    public List<EntProtoId> GuaranteedImplants = new();

    /// <summary>Пул имплантов, из которого случайно выбирается до <see cref="RandomCount"/> штук без повторений.</summary>
    [DataField]
    public List<EntProtoId> RandomPool = new();

    /// <summary>Сколько случайных имплантов выбрать из <see cref="RandomPool"/>.</summary>
    [DataField]
    public int RandomCount;

    /// <summary>Бонус к лимиту нейронагрузки носителя (<see cref="DutyCyberHostComponent.ExtraLoadLimit"/>).</summary>
    [DataField]
    public int LoadLimitBonus;

    /// <summary>Удалять ли имплантер после использования.</summary>
    [DataField]
    public bool OneUse = true;

    /// <summary>Звук при вживлении набора.</summary>
    [DataField]
    public SoundSpecifier? InjectSound = new SoundPathSpecifier("/Audio/Items/hypospray.ogg");
}
