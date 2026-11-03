// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Robust.Shared.Log;

namespace Content.Server._Duty.Logging;

/// <summary>
/// Уровни логирования по каналам (sawmill) из CVar <c>duty.log_levels</c>. Движок читает такие
/// переопределения только из аргумента командной строки <c>--loglevel</c>, а нам нужно, чтобы
/// консоль была тихой «по умолчанию» и без правки скриптов запуска.
/// Формат: <c>канал=Уровень;канал=Уровень</c>, уровень — Verbose/Debug/Info/Warning/Error/Fatal
/// или <c>null</c> (снять переопределение и унаследовать уровень родителя).
/// Дочерние каналы наследуют уровень родителя: <c>statushost=Warning</c> глушит и
/// <c>statushost.http</c>, и <c>statushost.acz</c>.
/// </summary>
public static class DutyLogLevels
{
    public static void Apply(ILogManager logManager, string spec)
    {
        foreach (var rawEntry in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = rawEntry.IndexOf('=');
            if (eq <= 0)
            {
                logManager.GetSawmill("duty.log").Warning($"duty.log_levels: пропускаю '{rawEntry}', ожидалось канал=Уровень");
                continue;
            }

            var name = rawEntry[..eq].Trim();
            var value = rawEntry[(eq + 1)..].Trim();

            LogLevel? level;
            if (value.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                level = null;
            }
            else if (Enum.TryParse<LogLevel>(value, true, out var parsed))
            {
                level = parsed;
            }
            else
            {
                logManager.GetSawmill("duty.log").Warning($"duty.log_levels: неизвестный уровень '{value}' для канала '{name}'");
                continue;
            }

            logManager.GetSawmill(name).Level = level;
        }
    }
}
