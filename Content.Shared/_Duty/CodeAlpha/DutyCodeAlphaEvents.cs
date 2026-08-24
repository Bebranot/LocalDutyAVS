// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._Duty.CodeAlpha;

/// <summary>
/// Сервер → хост: запрос подтверждения кода «Альфа».
/// Своё окно, а не <c>QuickDialogSystem</c>: у того нет булева поля, а <c>OpenDialogInternal</c>
/// приватный — пришлось бы патчить upstream ради одной кнопки.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyCodeAlphaPromptEvent : EntityEventArgs
{
    /// <summary>Уже локализованный текст запроса.</summary>
    public string Body;

    /// <summary>
    /// Момент, после которого ответ не нужен: сервер включит код сам. Клиент по нему рисует
    /// собственный отсчёт и закрывает окно.
    /// </summary>
    public TimeSpan ExpiresAt;

    public DutyCodeAlphaPromptEvent(string body, TimeSpan expiresAt)
    {
        Body = body;
        ExpiresAt = expiresAt;
    }
}

/// <summary>
/// Хост → сервер: ответ на запрос. <c>false</c> — вето, кода не будет.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutyCodeAlphaReplyEvent : EntityEventArgs
{
    public bool Confirmed;

    public DutyCodeAlphaReplyEvent(bool confirmed)
    {
        Confirmed = confirmed;
    }
}
