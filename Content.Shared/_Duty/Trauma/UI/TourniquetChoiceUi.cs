// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Trauma.UI;

/// <summary>
/// _Duty: ключ радиального меню «что перетягивать жгутом». Открывается на самом пациенте (он же
/// лечащий — быстрый жгут работает только на себе) и только когда кровотечений сразу два.
/// </summary>
[Serializable, NetSerializable]
public enum TourniquetChoiceUiKey : byte
{
    Key,
}

/// <summary>_Duty: что игрок выбрал лечить жгутом.</summary>
[Serializable, NetSerializable]
public enum TourniquetChoice : byte
{
    /// <summary>Артериальное кровотечение — быстрый DoAfter, жгут расходуется.</summary>
    Artery,

    /// <summary>Обычное кровотечение — ванильное применение жгута, как без травм.</summary>
    PlainBleeding,
}

/// <summary>
/// _Duty: сообщение клиент→сервер с выбранным пунктом меню. Предмет заново берётся из активной
/// руки на сервере, поэтому сообщение не несёт ничего, кроме выбора.
/// </summary>
[Serializable, NetSerializable]
public sealed class TourniquetChoiceMessage(TourniquetChoice choice) : BoundUserInterfaceMessage
{
    public TourniquetChoice Choice = choice;
}
