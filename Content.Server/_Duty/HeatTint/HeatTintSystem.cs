// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx, изначально взято оттуда из Goob-Station (AGPL-3.0-or-later).

using Content.Shared._Duty.HeatTint;
using Content.Shared.Temperature;
using Content.Shared.Temperature.Components;

namespace Content.Server._Duty.HeatTint;

public sealed partial class HeatTintSystem : SharedHeatTintSystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HeatTintComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<HeatTintComponent, OnTemperatureChangeEvent>(OnTemperatureChanged);
    }

    private void OnMapInit(Entity<HeatTintComponent> ent, ref MapInitEvent args)
    {
        if (TryComp<TemperatureComponent>(ent, out var temp))
            _appearance.SetData(ent, HeatTintVisuals.Temperature, temp.CurrentTemperature);
    }

    private void OnTemperatureChanged(Entity<HeatTintComponent> ent, ref OnTemperatureChangeEvent args)
    {
        _appearance.SetData(ent, HeatTintVisuals.Temperature, args.CurrentTemperature);
    }
}
