// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client.UserInterface.Controls;
using Content.Shared._Duty.Cyberware.Syndicate;
using Content.Shared.Radio;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.Cyberware.Syndicate;

/// <summary>
/// _Duty: радиальное меню выбора канала для «Голосового паразита». Список каналов — из компонента импланта.
/// </summary>
public sealed partial class DutyVoiceParasiteBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [Dependency] private IPrototypeManager _proto = default!;

    private SimpleRadialMenu? _menu;

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<SimpleRadialMenu>();

        if (!EntMan.TryGetComponent<DutyVoiceParasiteComponent>(Owner, out var comp))
            return;

        var buttons = new List<RadialMenuOptionBase>();
        foreach (var channelId in comp.Channels)
        {
            if (!_proto.TryIndex(channelId, out var channel))
                continue;

            // Иконка у всех одна — сам имплант; различает подсказка с названием канала.
            buttons.Add(new RadialMenuActionOption<ProtoId<RadioChannelPrototype>>(Select, channelId)
            {
                ToolTip = channel.LocalizedName,
                IconSpecifier = RadialMenuIconSpecifier.With(Owner),
            });
        }

        _menu.SetButtons(buttons);
        // Без явного открытия у курсора окно не получало фокус: центральная кнопка закрытия и Esc не срабатывали.
        _menu.OpenOverMouseScreenPosition();
    }

    private void Select(ProtoId<RadioChannelPrototype> channel)
    {
        SendMessage(new DutyVoiceParasiteSelectMessage(channel));
    }
}
