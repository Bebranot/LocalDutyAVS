// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Trauma.UI;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Duty.Trauma.UI;

/// <summary>
/// _Duty: клиентский BUI пошагового лечения артерии. Открывает <see cref="ArterialTreatmentWindow"/>,
/// шлёт на сервер нажатый этап и обновляет окно присланным состоянием.
/// </summary>
[UsedImplicitly]
public sealed class ArterialTreatmentBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private ArterialTreatmentWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ArterialTreatmentWindow>();
        _window.OnStepPressed += step => SendMessage(new ArterialTreatmentStepMessage(step));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ArterialTreatmentBuiState treatmentState)
            _window?.Update(treatmentState);
    }
}
