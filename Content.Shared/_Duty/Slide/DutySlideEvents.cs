// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._Duty.Slide;

/// <summary>
/// Клиент просит начать подкат. Отдельное предсказуемое событие вместо самой клавиши R: если
/// клиентский хендлер поглотит R, движок её на сервер уже не отправит, а если не поглотит —
/// у себя тут же сработает ванильное падение.
/// </summary>
[Serializable, NetSerializable]
public sealed class DutySlideRequestEvent : EntityEventArgs;
