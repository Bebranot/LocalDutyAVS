// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Duty.Trauma.Components;

/// <summary>
/// _Duty: маркер существа, которое ВООБЩЕ может получать тяжёлые травмы, и одновременно
/// декларация того, какие <see cref="BodyZone"/> у него есть. В движке (nubody) конечности
/// не являются сущностями, поэтому «наличие части тела» нельзя проверить через сущность —
/// вместо этого доступные зоны перечисляются здесь, в прототипе существа.
///
/// Вид/моб без какой-то конечности (или, наоборот, с лишними) просто переопределяет
/// <see cref="AvailableZones"/> в своём прототипе — это единственная точка расширения
/// набора зон, без правок кода. Роллер травм (server) выбирает зону только из этого набора.
///
/// Дополнительное гейт-условие «только игроки» проверяется в рантайме по наличию
/// ActorComponent и здесь НЕ дублируется (это динамическое состояние, не данные прототипа).
/// </summary>
[RegisterComponent]
public sealed partial class TraumaTargetComponent : Component
{
    /// <summary>
    /// Зоны тела, которые есть у этого существа и потому могут быть травмированы.
    /// По умолчанию — полный набор стандартного гуманоида.
    /// </summary>
    [DataField]
    public HashSet<BodyZone> AvailableZones = new()
    {
        BodyZone.Head,
        BodyZone.Torso,
        BodyZone.LeftArm,
        BodyZone.RightArm,
        BodyZone.LeftLeg,
        BodyZone.RightLeg,
    };
}
