// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client.UserInterface.Controls;
using Content.Shared._Duty.Trauma.UI;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;

namespace Content.Client._Duty.Trauma.UI;

/// <summary>
/// _Duty: радиальное меню «что перетягивать жгутом» — открывается, только когда у игрока разом и
/// артериальное, и обычное кровотечение. Состояния у окна нет: пунктов всегда ровно два, а всё
/// остальное сервер проверяет заново по нажатию.
/// </summary>
[UsedImplicitly]
public sealed class TourniquetChoiceBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private static readonly SpriteSpecifier ArteryIcon =
        new SpriteSpecifier.Rsi(new ResPath("/Textures/_Duty/Interface/StatusAlerts/artery.rsi"), "artery");

    private static readonly SpriteSpecifier BleedIcon =
        new SpriteSpecifier.Rsi(new ResPath("/Textures/Interface/Alerts/bleed.rsi"), "bleed2");

    private SimpleRadialMenu? _menu;

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<SimpleRadialMenu>();
        _menu.SetButtons(new RadialMenuOptionBase[]
        {
            new RadialMenuActionOption<TourniquetChoice>(OnChoicePressed, TourniquetChoice.Artery)
            {
                IconSpecifier = RadialMenuIconSpecifier.With(ArteryIcon),
                ToolTip = Loc.GetString("trauma-tourniquet-choice-artery"),
            },
            new RadialMenuActionOption<TourniquetChoice>(OnChoicePressed, TourniquetChoice.PlainBleeding)
            {
                IconSpecifier = RadialMenuIconSpecifier.With(BleedIcon),
                ToolTip = Loc.GetString("trauma-tourniquet-choice-bleeding"),
            },
        });

        _menu.OpenOverMouseScreenPosition();
    }

    private void OnChoicePressed(TourniquetChoice choice)
    {
        SendMessage(new TourniquetChoiceMessage(choice));
    }
}
