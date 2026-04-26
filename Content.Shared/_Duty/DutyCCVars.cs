// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

[CVarDefs]
public sealed class DutyCCVars
{
    /// <summary>
    /// Включена ли динамическая фоновая музыка.
    /// </summary>
    public static readonly CVarDef<bool> DynamicAmbientMusicEnabled =
        CVarDef.Create("duty.ambient_music_enabled", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// Громкость динамической фоновой музыки (0.0–1.0).
    /// </summary>
    public static readonly CVarDef<float> DynamicAmbientMusicVolume =
        CVarDef.Create("duty.ambient_music_volume", 0.15f, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// Секретный API-ключ OpenRouter для NPC-бармена.
    /// Значение задаётся только в серверной конфигурации, в репозитории должно оставаться пустым.
    /// </summary>
    public static readonly CVarDef<string> BarmanOpenRouterApiKey =
        CVarDef.Create("duty.barman_openrouter_api_key", string.Empty, CVar.SERVERONLY);

    /// <summary>
    /// Включена ли долговременная память NPC-бармена.
    /// </summary>
    public static readonly CVarDef<bool> BarmanMemoryEnabled =
        CVarDef.Create("duty.barman_memory_enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// Путь до sqlite-файла памяти Билли. Относительный путь считается от user-data директории сервера.
    /// </summary>
    public static readonly CVarDef<string> BarmanMemoryDbPath =
        CVarDef.Create("duty.barman_memory_db_path", "Data/barman_memory.db", CVar.SERVERONLY);

    /// <summary>
    /// Разрешены ли голоса +1/-1 для усиления или ослабления прошлых ответов Билли.
    /// </summary>
    public static readonly CVarDef<bool> BarmanFeedbackEnabled =
        CVarDef.Create("duty.barman_feedback_enabled", true, CVar.SERVERONLY);
}
