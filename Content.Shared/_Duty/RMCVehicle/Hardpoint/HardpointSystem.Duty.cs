// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Popups;
using Content.Shared.Tools.Components;

namespace Content.Shared._RMC14.Vehicle;

// _Duty: замена RMCRepairableSystem.UseFuel — ремонт брони танка жжёт топливо обычной ванильной сварки.
public sealed partial class HardpointSystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _dutySolution = default!;
    [Dependency] private readonly ItemToggleSystem _dutyToggle = default!;

    private bool UseWelderFuel(EntityUid tool, EntityUid user, FixedPoint2 fuelUsed, bool attempt = false)
    {
        if (!TryComp(tool, out WelderComponent? welder))
            return false;

        if (!_dutyToggle.IsActivated(tool))
        {
            _popup.PopupClient(Loc.GetString("welder-component-welder-not-lit-message"), user, PopupType.SmallCaution);
            return false;
        }

        if (!_dutySolution.TryGetSolution(tool, welder.FuelSolutionName, out var solutionEnt, out var solution))
            return false;

        if (solution.GetTotalPrototypeQuantity(welder.FuelReagent) < fuelUsed)
        {
            _popup.PopupClient(Loc.GetString("welder-component-no-fuel-message"), user, PopupType.SmallCaution);
            return false;
        }

        if (!attempt && _net.IsServer)
            _dutySolution.RemoveReagent(solutionEnt.Value, welder.FuelReagent, fuelUsed);

        return true;
    }
}
