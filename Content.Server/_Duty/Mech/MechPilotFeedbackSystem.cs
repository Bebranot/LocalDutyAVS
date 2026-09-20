// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx. Отличие: у нас нет общей Vehicle-системы, поэтому подписка
// идёт на свои MechPilotEnteredEvent/MechPilotExitedEvent (см. SharedMechSystem.TryInsert/TryEject)
// вместо их OnVehicleEnteredEvent/OnVehicleExitedEvent.

using Content.Server.Chat.Systems;
using Content.Shared._Duty.Mech;
using Content.Shared.Chat;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Mech.Components;

namespace Content.Server._Duty.Mech;

public sealed partial class MechPilotFeedbackSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly BlindableSystem _blindable = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MechComponent, MechPilotEnteredEvent>(OnEntered);
        SubscribeLocalEvent<MechComponent, MechPilotExitedEvent>(OnExited);
    }

    private void OnEntered(Entity<MechComponent> mech, ref MechPilotEnteredEvent args)
    {
        UpdatePilotVision(mech.Owner, mech.Comp);
        Speak(mech.Owner, "mech-pilot-connected");
    }

    private void OnExited(Entity<MechComponent> mech, ref MechPilotExitedEvent args)
    {
        RemComp<MechPowerBlindnessComponent>(args.Pilot);
        _blindable.UpdateIsBlind(args.Pilot);
        Speak(mech.Owner, "mech-pilot-disconnected", emergency: true);
    }

    public void UpdatePilotVision(EntityUid mechUid, MechComponent? mech = null)
    {
        if (!Resolve(mechUid, ref mech, false) || mech.PilotSlot.ContainedEntity is not { } pilot)
            return;

        if (mech.Energy <= 0)
            EnsureComp<MechPowerBlindnessComponent>(pilot);
        else
            RemComp<MechPowerBlindnessComponent>(pilot);

        _blindable.UpdateIsBlind(pilot);
    }

    private void Speak(EntityUid mech, string message, bool emergency = false)
    {
        if (!TryComp<MechComponent>(mech, out var component))
            return;

        if (component.Energy <= 0)
        {
            if (!emergency)
                return;

            message = "mech-emergency-eject";
        }

        _chat.TrySendInGameICMessage(
            mech,
            Loc.GetString(message),
            InGameICChatType.Speak,
            hideChat: false,
            checkRadioPrefix: false,
            ignoreActionBlocker: true);
    }
}
