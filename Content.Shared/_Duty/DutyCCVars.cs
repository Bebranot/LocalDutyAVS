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
    /// Устаревший общий множитель; если &gt; 0, умножается на громкость каждого уровня.
    /// </summary>
    public static readonly CVarDef<float> DynamicAmbientMusicVolume =
        CVarDef.Create("duty.ambient_music_volume", 1f, CVar.ARCHIVE | CVar.CLIENTONLY);

    public static readonly CVarDef<bool> DynamicAmbientMusicPeacefulDisabled =
        CVarDef.Create("duty.ambient_music_disable_peaceful", false, CVar.ARCHIVE | CVar.CLIENTONLY);

    public static readonly CVarDef<bool> DynamicAmbientMusicCombatDisabled =
        CVarDef.Create("duty.ambient_music_disable_combat", false, CVar.ARCHIVE | CVar.CLIENTONLY);

    public static readonly CVarDef<float> DynamicAmbientMusicVolumeVeryGood =
        CVarDef.Create("duty.ambient_music_volume_very_good", 1f, CVar.ARCHIVE | CVar.CLIENTONLY);

    public static readonly CVarDef<float> DynamicAmbientMusicVolumeGood =
        CVarDef.Create("duty.ambient_music_volume_good", 1f, CVar.ARCHIVE | CVar.CLIENTONLY);

    public static readonly CVarDef<float> DynamicAmbientMusicVolumeMedium =
        CVarDef.Create("duty.ambient_music_volume_medium", 1f, CVar.ARCHIVE | CVar.CLIENTONLY);

    public static readonly CVarDef<float> DynamicAmbientMusicVolumeBelowMedium =
        CVarDef.Create("duty.ambient_music_volume_below_medium", 1f, CVar.ARCHIVE | CVar.CLIENTONLY);

    public static readonly CVarDef<float> DynamicAmbientMusicVolumeAwful =
        CVarDef.Create("duty.ambient_music_volume_awful", 1f, CVar.ARCHIVE | CVar.CLIENTONLY);

    public static readonly CVarDef<float> DynamicAmbientMusicVolumeHpCritical =
        CVarDef.Create("duty.ambient_music_volume_hp_critical", 1f, CVar.ARCHIVE | CVar.CLIENTONLY);

    public static readonly CVarDef<float> DynamicAmbientMusicVolumeMobCritical =
        CVarDef.Create("duty.ambient_music_volume_mob_critical", 1f, CVar.ARCHIVE | CVar.CLIENTONLY);

    public static readonly CVarDef<float> DynamicAmbientMusicVolumeCombat =
        CVarDef.Create("duty.ambient_music_volume_combat", 1f, CVar.ARCHIVE | CVar.CLIENTONLY);

    public static readonly CVarDef<float> DynamicAmbientMusicVolumeCombatLow =
        CVarDef.Create("duty.ambient_music_volume_combat_low", 1f, CVar.ARCHIVE | CVar.CLIENTONLY);

    public static readonly CVarDef<float> DynamicAmbientMusicVolumeDeath =
        CVarDef.Create("duty.ambient_music_volume_death", 1f, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>Громкость звука входа в крит (CritEnterSound). Громче остальных по умолчанию.</summary>
    public static readonly CVarDef<float> DynamicAmbientMusicVolumeCritEnter =
        CVarDef.Create("duty.ambient_music_volume_crit_enter", 1.4f, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>Доп. усиление critmode-музыки (dB), поверх boost из yml.</summary>
    public static readonly CVarDef<float> DynamicAmbientMusicCritExtraBoostDb =
        CVarDef.Create("duty.ambient_music_crit_extra_boost_db", 2.5f, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>Множитель master gain в крит. состоянии (0–1).</summary>
    public static readonly CVarDef<float> CritAudioDuckGain =
        CVarDef.Create("duty.crit_audio_duck_gain", 0.35f, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>Длительность плавного duck / восстановления (сек).</summary>
    public static readonly CVarDef<float> CritAudioDuckFadeSeconds =
        CVarDef.Create("duty.crit_audio_duck_fade_seconds", 1.5f, CVar.ARCHIVE | CVar.CLIENTONLY);

    // ── Health Phrases ────────────────────────────────────────────────────────

    /// <summary>
    /// Включена ли система реплик боли.
    /// </summary>
    public static readonly CVarDef<bool> HealthPhrasesEnabled =
        CVarDef.Create("duty.health_phrases_enabled", true, CVar.ARCHIVE | CVar.REPLICATED);

    /// <summary>
    /// Минимальный интервал между popup-сообщениями (секунды).
    /// </summary>
    public static readonly CVarDef<float> HealthPhrasesPopupMin =
        CVarDef.Create("duty.health_phrases_popup_min", 40f, CVar.ARCHIVE | CVar.REPLICATED);

    /// <summary>
    /// Максимальный интервал между popup-сообщениями (секунды).
    /// </summary>
    public static readonly CVarDef<float> HealthPhrasesPopupMax =
        CVarDef.Create("duty.health_phrases_popup_max", 180f, CVar.ARCHIVE | CVar.REPLICATED);

    /// <summary>
    /// Минимальный интервал между whisper-сообщениями (секунды).
    /// </summary>
    public static readonly CVarDef<float> HealthPhrasesWhisperMin =
        CVarDef.Create("duty.health_phrases_whisper_min", 60f, CVar.ARCHIVE | CVar.REPLICATED);

    /// <summary>
    /// Максимальный интервал между whisper-сообщениями (секунды).
    /// </summary>
    public static readonly CVarDef<float> HealthPhrasesWhisperMax =
        CVarDef.Create("duty.health_phrases_whisper_max", 300f, CVar.ARCHIVE | CVar.REPLICATED);

    // ── Крик боли (публичный DutyHealthScream popup с тряской текста) ──────────

    /// <summary>
    /// Минимальный интервал между криками боли на критичном HP (5-10% и 0-5%), секунды.
    /// </summary>
    public static readonly CVarDef<float> HealthPhrasesCritScreamMin =
        CVarDef.Create("duty.health_phrases_crit_scream_min", 45f, CVar.ARCHIVE | CVar.REPLICATED);

    /// <summary>
    /// Максимальный интервал между криками боли на критичном HP (5-10% и 0-5%), секунды.
    /// </summary>
    public static readonly CVarDef<float> HealthPhrasesCritScreamMax =
        CVarDef.Create("duty.health_phrases_crit_scream_max", 120f, CVar.ARCHIVE | CVar.REPLICATED);

    /// <summary>
    /// Минимальный урон за один раз, чтобы вообще рассматривать крик от боли (порог, как EmoteOnDamage в RMC14).
    /// </summary>
    public static readonly CVarDef<float> HealthPhrasesDamageScreamThreshold =
        CVarDef.Create("duty.health_phrases_damage_scream_threshold", 10f, CVar.ARCHIVE | CVar.REPLICATED);

    /// <summary>
    /// Шанс крика при получении урона выше порога (0-1).
    /// </summary>
    public static readonly CVarDef<float> HealthPhrasesDamageScreamChance =
        CVarDef.Create("duty.health_phrases_damage_scream_chance", 0.5f, CVar.ARCHIVE | CVar.REPLICATED);

    /// <summary>
    /// Кулдаун между криками боли от урона на одну сущность, секунды.
    /// </summary>
    public static readonly CVarDef<float> HealthPhrasesDamageScreamCooldown =
        CVarDef.Create("duty.health_phrases_damage_scream_cooldown", 6f, CVar.ARCHIVE | CVar.REPLICATED);

    // ── Контузия (оглушение от выстрелов и взрывов) ───────────────────────────

    /// <summary>
    /// Включена ли система контузии (серверная логика: набор шкалы, импульсы, алерт).
    /// </summary>
    public static readonly CVarDef<bool> ConcussionEnabled =
        CVarDef.Create("duty.concussion_enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// Включены ли визуально-звуковые эффекты контузии (затемнение экрана, звон в ушах).
    /// </summary>
    public static readonly CVarDef<bool> ConcussionEffectsEnabled =
        CVarDef.Create("duty.concussion_effects_enabled", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    // ── Сердцебиение (пресентейшн-звук пульса тела) ───────────────────────────

    /// <summary>
    /// Включён ли пассивный звук сердцебиения тела (пульс у владельца).
    /// Сервер решает (выключается сразу у всех, accessibility), но REPLICATED — чтобы
    /// клиент, который и проигрывает звук у владельца, мог его прочитать.
    /// НЕ затрагивает диагностический звук в анализаторе здоровья.
    /// </summary>
    public static readonly CVarDef<bool> HeartbeatEnabled =
        CVarDef.Create("duty.heartbeat_enabled", true, CVar.SERVER | CVar.REPLICATED);

    // ── Реалистичный бег (выносливость + замедление от ХП/брони/оружия) ─────────

    /// <summary>
    /// Включена ли система реалистичного бега: расход выносливости, замедление спринта
    /// от ХП, занятых слотов и оружия в руках. Выключение возвращает ванильный спринт.
    /// </summary>
    public static readonly CVarDef<bool> SprintEnabled =
        CVarDef.Create("duty.sprint_enabled", true, CVar.SERVER | CVar.REPLICATED);

    // ── Discord ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ID роли, которую пингует вебхук при старте раунда. Пусто — пинга нет.
    /// Аналог апстримного discord.round_end_role, но для начала раунда.
    /// </summary>
    public static readonly CVarDef<string> DiscordRoundStartRole =
        CVarDef.Create("discord.round_start_role", string.Empty, CVar.SERVERONLY);
}
