// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx без изменений — все зависимые API совпадают.

using Content.Shared.Chat;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Random;

namespace Content.Shared._Duty.Gambling;

public sealed partial class CoinFlipperSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedChatSystem _chatSystem = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _power = default!;
    [Dependency] private readonly SharedStackSystem _stackSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CoinFlipperComponent, ActivateInWorldEvent>(OnInteractHandEvent);
        SubscribeLocalEvent<CoinFlipperComponent, CoinFlipperDoAfterEvent>(OnCoinFlipperDoAfter);
    }

    private void OnInteractHandEvent(EntityUid uid, CoinFlipperComponent comp, ActivateInWorldEvent args)
    {
        if (comp.IsSpinning || !_power.IsPowered(uid))
            return;

        if (!_itemSlots.TryGetSlot(uid, "money", out var slot)
            || slot.Item == null
            || !TryComp<StackComponent>(slot.Item.Value, out var stack))
        {
            _popupSystem.PopupPredicted(Loc.GetString("coinflipper-no-money"), uid, uid, PopupType.Small);
            return;
        }

        comp.PrizeAmount = 0;
        var doAfter =
         new DoAfterArgs(EntityManager, uid, TimeSpan.FromSeconds(comp.DoAfterTime), new CoinFlipperDoAfterEvent(), uid)
         {
             BreakOnMove = false,
             BreakOnDamage = false,
         };

        comp.PrizeAmount = _stackSystem.GetCount(stack.Owner);
        _stackSystem.SetCount(stack.Owner, 0, stack);
        comp.IsSpinning = true;

        if (_net.IsServer)
        {
            _audio.PlayPvs(comp.SpinSound, uid);
            _doAfter.TryStartDoAfter(doAfter);
        }
    }

    private void OnCoinFlipperDoAfter(EntityUid uid, CoinFlipperComponent comp, CoinFlipperDoAfterEvent args)
    {
        if (args.Cancelled)
        {
            comp.IsSpinning = false;
            Dirty(uid, comp);
            return;
        }

        if (args.Handled || !_itemSlots.TryGetSlot(uid, "money", out var slot))
            return;

        comp.IsSpinning = false;
        Dirty(uid, comp);

        StackComponent? stack = null;
        if (slot.Item != null)
            TryComp<StackComponent>(slot.Item.Value, out stack);

        if (_random.Prob(.5f))
        {
            _audio.PlayPredicted(comp.WinSound, uid, args.User);
            comp.PrizeAmount *= 2;
            if (stack == null)
            {
                var newStack = Spawn("SpaceCash", Transform(uid).Coordinates);
                if (TryComp<StackComponent>(newStack, out var newStackComp))
                    _stackSystem.SetCount(newStack, comp.PrizeAmount, newStackComp);
            }
            else
            {
                _stackSystem.SetCount(stack.Owner, comp.PrizeAmount, stack);
            }

            _chatSystem.TrySendInGameICMessage(uid, Loc.GetString("coinflipper-win", ("amount", comp.PrizeAmount)), InGameICChatType.Speak, hideChat: false, hideLog: true, checkRadioPrefix: false);
            return;
        }

        _audio.PlayPredicted(comp.LoseSound, uid, args.User);
    }
}
