// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client.UserInterface.Controls;
using Content.Shared._Duty.Cyberware.Neuro;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;

namespace Content.Client._Duty.Cyberware.Neuro;

/// <summary>
/// _Duty: радиальное меню «Навигатора». Точки берутся из прототипа импланта (компонент есть и на клиенте),
/// так что состояния у окна нет — сервер по индексу сам ищет маяк.
/// </summary>
[UsedImplicitly]
public sealed class DutyNavigatorBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private static readonly SpriteSpecifier DefaultIcon =
        new SpriteSpecifier.Rsi(new ResPath("/Textures/Interface/Actions/pai-map.rsi"), "icon");

    private static readonly SpriteSpecifier OffIcon =
        new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/close.svg.192dpi.png"));

    private SimpleRadialMenu? _menu;

    protected override void Open()
    {
        base.Open();

        if (!EntMan.TryGetComponent(Owner, out DutyNavigatorImplantComponent? navigator))
        {
            Close();
            return;
        }

        var options = new List<RadialMenuOptionBase>(navigator.Destinations.Count + 1);
        for (var i = 0; i < navigator.Destinations.Count; i++)
        {
            var dest = navigator.Destinations[i];
            options.Add(new RadialMenuActionOption<int>(OnPressed, i)
            {
                IconSpecifier = RadialMenuIconSpecifier.With(dest.Icon ?? DefaultIcon),
                ToolTip = Loc.GetString(dest.Name),
            });
        }

        options.Add(new RadialMenuActionOption<int>(OnPressed, -1)
        {
            IconSpecifier = RadialMenuIconSpecifier.With(OffIcon),
            ToolTip = Loc.GetString("duty-navigator-off"),
        });

        _menu = this.CreateWindow<SimpleRadialMenu>();
        _menu.SetButtons(options);
        _menu.OpenOverMouseScreenPosition();
    }

    private void OnPressed(int index)
    {
        SendMessage(new DutyNavigatorSelectMessage(index));
    }
}
