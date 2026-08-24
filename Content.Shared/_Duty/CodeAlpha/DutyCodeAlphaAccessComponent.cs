// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._Duty.CodeAlpha;

/// <summary>
/// _Duty: «у этого существа полный доступ по коду Альфа». Данных не несёт — весь набор тегов
/// выводится из него в <see cref="SharedDutyCodeAlphaAccessSystem"/>.
///
/// Сетевой обязательно: проверка доступа выполняется и на клиенте (предсказание открытия шлюза).
/// Без сети клиент считал бы дверь закрытой, предсказывал отказ, а сервер её открывал — шлюзы
/// дёргались бы на каждом откате.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DutyCodeAlphaAccessComponent : Component;
