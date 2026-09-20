// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx.

using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared._Duty.Clothing;

/// <summary>
/// _Duty: одежда/предмет пачкается — хранит раствор «грязи» (reagent solution), которая
/// со временем подсыхает (EvaporationSpeed реагентов) и может быть смыта средством с эффектом
/// CleanDirt (стирка, душ). Цвет пятна считается по составу раствора.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), Access(typeof(ClothingDirtSystem))]
public sealed partial class ClothingDirtableComponent : Component
{
    [DataField]
    public string Solution = ClothingDirtSystem.DefaultSolutionName;

    [DataField]
    public FixedPoint2 Capacity = FixedPoint2.New(20);

    [DataField]
    public FixedPoint2 MaxReagentAmount = FixedPoint2.New(10);

    [DataField]
    public float MinVisualCoverage = 0.08f;

    [AutoNetworkedField]
    public Color? DirtColor;

    [DataField]
    public float DryInterval = 30f;

    public float DryAccumulator;
}
