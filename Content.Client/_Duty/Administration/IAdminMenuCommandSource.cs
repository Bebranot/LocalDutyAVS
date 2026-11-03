// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Client.UserInterface.Controls;

namespace Content.Client._Duty.Administration;

/// <summary>
/// Вкладка меню F7, у которой команды висят не на <c>CommandButton</c>, а на обычных кнопках
/// (например, кнопки раунда с двойным подтверждением). Нужна, чтобы поиск F7 их находил,
/// а вкладка пряталась, когда ни одна из них не доступна.
/// </summary>
public interface IAdminMenuCommandSource
{
    /// <summary>Кнопка, консольная команда, которую она выполняет, и требует ли она подтверждения.</summary>
    IEnumerable<(BaseButton Button, string Command, bool Confirm)> MenuCommands { get; }
}
