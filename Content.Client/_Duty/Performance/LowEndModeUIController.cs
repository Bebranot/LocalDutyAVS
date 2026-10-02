// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Text;
using Content.Shared.CCVar;
using JetBrains.Annotations;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;

namespace Content.Client._Duty.Performance;

/// <summary>
/// _Duty: режим для слабых устройств (<see cref="DutyCCVars.LowEndMode"/>).
///
/// Почему UIController, а не EntitySystem: системы живут, только пока клиент подключён к серверу,
/// а режим надо уметь включить и в главном меню, и применить сразу при запуске игры.
///
/// Почему только существующие CVar-ы: лаунчер ставит игрокам стоковый Robust.Client с
/// central.spacestation14.io, а не наш форк движка, так что до игроков доходит только контент.
/// Ссылка контента на что-то, чего нет в стоковом движке, роняет клиент при загрузке.
///
/// Пресет не прячет игровую информацию: дальние источники света не выключаются, частицы с
/// IgnoreQualitySettings остаются, оверлеи (ночное видение, контузия) не трогаются.
/// </summary>
[UsedImplicitly]
public sealed partial class LowEndModeUIController : UIController
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ILogManager _logManager = default!;

    private const char EntrySeparator = ';';
    private const char ValueSeparator = '=';

    private readonly List<PresetEntry> _preset = new();
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logManager.GetSawmill("duty.low_end");
        BuildPreset();

        if (_cfg.GetCVar(DutyCCVars.LowEndMode))
        {
            ApplyOnStartup();
        }
        else if (_cfg.GetCVar(DutyCCVars.LowEndModeSaved) != string.Empty)
        {
            // «Режим выключен, а снапшот остался» бывает только после ручной правки конфига или
            // падения посреди переключения — правильнее всего вернуть игроку его значения.
            Disable();
        }

        _cfg.OnValueChanged(DutyCCVars.LowEndMode, OnModeChanged);
    }

    private void BuildPreset()
    {
        // Свет — главный расход видеокарты. Лимит теневых источников режем умеренно: сверх лимита
        // движок не рисует дальние источники вовсе, а тёмный край экрана — уже потеря информации.
        _preset.Add(Float(CVars.LightResolutionScale, 0.125f, atMost: true));
        _preset.Add(Bool(CVars.LightSoftShadows, false));
        _preset.Add(Bool(CVars.LightBlur, false));
        _preset.Add(Int(CVars.MaxShadowcastingLights, 48, atMost: true));
        // Движок не сохраняет его в конфиг (нет ARCHIVE), поэтому повторяем при каждом запуске.
        _preset.Add(Bool(CVars.RenderTileEdges, false, reapplyOnStartup: true));

        // Рисовать мир в родном разрешении и растягивать: при масштабе 2x это вчетверо меньше пикселей
        // на каждый проход света, тайлов, спрайтов и полноэкранных шейдеров.
        _preset.Add(Bool(CCVars.ViewportScaleRender, false));
        _preset.Add(Bool(CCVars.ParallaxLowQuality, true));
        _preset.Add(Bool(CCVars.AmbientOcclusion, false));
        // «Низко», а не «выкл»: часть эффектов читается как подсказка, пусть останется хотя бы намёком.
        _preset.Add(Int(CCVars.ParticleQuality, 1, atMost: true));

        // Без VSync и лимита игра крутит кадры впустую и греет слабый ноутбук до троттлинга.
        // 0 у движка значит «без лимита», поэтому он тоже считается «выше 60».
        _preset.Add(new PresetEntry<int>(CVars.DisplayMaxFPS, 60,
            v => v == 0 || v > 60, FormatInt, TryParseInt, reapplyOnStartup: false));

        // Звук. HRTF движок читает один раз при старте — подхватится после перезапуска игры.
        _preset.Add(Bool(CVars.AudioHrtf, false));
        _preset.Add(Int(CVars.AudioDefaultConcurrent, 8, atMost: true));
        // Частота пересчёта позиций и окклюзии (лучей сквозь стены) у всех играющих звуков.
        // Тоже без ARCHIVE в движке.
        _preset.Add(Int(CVars.AudioTickRate, 15, atMost: true, reapplyOnStartup: true));
        // Как часто ищутся источники фонового эмбиента вокруг игрока.
        _preset.Add(Float(CCVars.AmbientCooldown, 0.3f, atMost: false));
    }

    private void OnModeChanged(bool enabled)
    {
        if (enabled)
            Enable();
        else
            Disable();

        // SaveToFile здесь не зовём: из меню настроек его сразу после нас делает «Применить»,
        // а пути при запуске идемпотентны — если не сохранились, повторятся с тем же итогом.
        // Лишний вызов вредил бы: без загруженного конфига (тесты) он пишет предупреждение.
    }

    private void Enable()
    {
        // Снапшот снимаем только при первом включении: повторное (например, set из консоли поверх
        // уже включённого режима) иначе запомнило бы пониженные значения как «настройки игрока».
        if (_cfg.GetCVar(DutyCCVars.LowEndModeSaved) == string.Empty)
            _cfg.SetCVar(DutyCCVars.LowEndModeSaved, CaptureSnapshot());

        var changed = 0;
        foreach (var entry in _preset)
        {
            if (entry.ApplyLow(_cfg))
                changed++;
        }

        _sawmill.Info($"Режим для слабых устройств включён, изменено настроек: {changed}");
    }

    private void ApplyOnStartup()
    {
        // Режим включили правкой конфига, минуя меню, — снапшота ещё нет.
        if (_cfg.GetCVar(DutyCCVars.LowEndModeSaved) == string.Empty)
        {
            Enable();
            return;
        }

        // Остальное движок уже восстановил из конфига, а если игрок что-то поменял руками
        // при включённом режиме — это его выбор, повторно не давим.
        foreach (var entry in _preset)
        {
            if (entry.ReapplyOnStartup)
                entry.ApplyLow(_cfg);
        }
    }

    private void Disable()
    {
        var saved = ParseSnapshot(_cfg.GetCVar(DutyCCVars.LowEndModeSaved));
        var restored = 0;

        foreach (var entry in _preset)
        {
            // Игрок поменял настройку сам, пока режим был включён, — его выбор важнее снапшота.
            if (!saved.TryGetValue(entry.Name, out var value) || !entry.IsLow(_cfg))
                continue;

            entry.Restore(_cfg, value);
            restored++;
        }

        _cfg.SetCVar(DutyCCVars.LowEndModeSaved, string.Empty);
        _sawmill.Info($"Режим для слабых устройств выключен, возвращено настроек: {restored}");
    }

    private string CaptureSnapshot()
    {
        var builder = new StringBuilder();
        foreach (var entry in _preset)
        {
            if (builder.Length > 0)
                builder.Append(EntrySeparator);

            builder.Append(entry.Name).Append(ValueSeparator).Append(entry.Capture(_cfg));
        }

        return builder.ToString();
    }

    private static Dictionary<string, string> ParseSnapshot(string snapshot)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in snapshot.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.IndexOf(ValueSeparator);
            if (split <= 0)
                continue;

            result[pair[..split]] = pair[(split + 1)..];
        }

        return result;
    }

    #region Пресет

    private static PresetEntry Bool(CVarDef<bool> def, bool low, bool reapplyOnStartup = false)
        => new PresetEntry<bool>(def, low, v => v != low, FormatBool, TryParseBool, reapplyOnStartup);

    /// <param name="atMost">true — значение не выше <paramref name="low"/>, false — не ниже.</param>
    private static PresetEntry Int(CVarDef<int> def, int low, bool atMost, bool reapplyOnStartup = false)
        => new PresetEntry<int>(def, low, atMost ? v => v > low : v => v < low,
            FormatInt, TryParseInt, reapplyOnStartup);

    /// <param name="atMost">true — значение не выше <paramref name="low"/>, false — не ниже.</param>
    private static PresetEntry Float(CVarDef<float> def, float low, bool atMost, bool reapplyOnStartup = false)
        => new PresetEntry<float>(def, low, atMost ? v => v > low : v => v < low,
            FormatFloat, TryParseFloat, reapplyOnStartup);

    private static string FormatBool(bool value) => value ? "1" : "0";

    private static bool TryParseBool(string text, out bool value)
    {
        value = text == "1";
        return text is "1" or "0";
    }

    private static string FormatInt(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool TryParseInt(string text, out int value) => Parse.TryInt32(text, out value);

    private static string FormatFloat(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static bool TryParseFloat(string text, out float value) => Parse.TryFloat(text, out value);

    private delegate bool TryParseValue<T>(string text, out T value);

    private abstract class PresetEntry
    {
        public abstract string Name { get; }

        /// <summary>Значение не сохраняется движком в конфиг — его надо выставлять при каждом запуске.</summary>
        public abstract bool ReapplyOnStartup { get; }

        public abstract string Capture(IConfigurationManager cfg);

        /// <summary>Опускает настройку до пресета. Возвращает true, если что-то пришлось менять.</summary>
        public abstract bool ApplyLow(IConfigurationManager cfg);

        /// <summary>
        /// Не «выше» пресета. Сравнение по направлению, а не на равенство: так не мешают погрешности
        /// float после круга через конфиг, а значение, которое у игрока и так было ниже, не трогается.
        /// </summary>
        public abstract bool IsLow(IConfigurationManager cfg);

        public abstract void Restore(IConfigurationManager cfg, string saved);
    }

    private sealed class PresetEntry<T>(
        CVarDef<T> def,
        T low,
        Func<T, bool> needsChange,
        Func<T, string> format,
        TryParseValue<T> parse,
        bool reapplyOnStartup) : PresetEntry
        where T : notnull
    {
        public override string Name => def.Name;

        public override bool ReapplyOnStartup => reapplyOnStartup;

        public override string Capture(IConfigurationManager cfg) => format(cfg.GetCVar(def));

        public override bool ApplyLow(IConfigurationManager cfg)
        {
            if (!needsChange(cfg.GetCVar(def)))
                return false;

            cfg.SetCVar(def, low);
            return true;
        }

        public override bool IsLow(IConfigurationManager cfg) => !needsChange(cfg.GetCVar(def));

        public override void Restore(IConfigurationManager cfg, string saved)
        {
            if (parse(saved, out var value))
                cfg.SetCVar(def, value);
        }
    }

    #endregion
}
