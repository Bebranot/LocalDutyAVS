using System.Linq;
using Content.Server.Administration;
using Content.Server.Chat.Systems;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.FixedPoint;
using Content.Shared._Duty.HealthPhrases;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Duty.HealthPhrases;

public sealed class HealthPhrasesSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IConsoleHost _console = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;

    private bool _enabled;
    private float _popupMin;
    private float _popupMax;
    private float _whisperMin;
    private float _whisperMax;
    private float _critScreamMin;
    private float _critScreamMax;
    private float _damageScreamThreshold;
    private float _damageScreamChance;
    private float _damageScreamCooldown;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawn);
        SubscribeLocalEvent<HealthPhrasesComponent, DamageDealtEvent>(OnDamageDealt);

        _cfg.OnValueChanged(DutyCCVars.HealthPhrasesEnabled,   v => _enabled    = v, true);
        _cfg.OnValueChanged(DutyCCVars.HealthPhrasesPopupMin,  v => _popupMin   = v, true);
        _cfg.OnValueChanged(DutyCCVars.HealthPhrasesPopupMax,  v => _popupMax   = v, true);
        _cfg.OnValueChanged(DutyCCVars.HealthPhrasesWhisperMin, v => _whisperMin = v, true);
        _cfg.OnValueChanged(DutyCCVars.HealthPhrasesWhisperMax, v => _whisperMax = v, true);
        _cfg.OnValueChanged(DutyCCVars.HealthPhrasesCritScreamMin, v => _critScreamMin = v, true);
        _cfg.OnValueChanged(DutyCCVars.HealthPhrasesCritScreamMax, v => _critScreamMax = v, true);
        _cfg.OnValueChanged(DutyCCVars.HealthPhrasesDamageScreamThreshold, v => _damageScreamThreshold = v, true);
        _cfg.OnValueChanged(DutyCCVars.HealthPhrasesDamageScreamChance, v => _damageScreamChance = v, true);
        _cfg.OnValueChanged(DutyCCVars.HealthPhrasesDamageScreamCooldown, v => _damageScreamCooldown = v, true);

        _console.RegisterCommand("duty_hp_test",
            "Принудительно вызвать реплику боли у игрока. Использование: duty_hp_test <username> [popup|whisper|say]",
            "duty_hp_test <username> [popup|whisper|say]",
            TestCommand,
            TestCommandCompletion);
    }

    private void OnPlayerSpawn(PlayerSpawnCompleteEvent ev)
    {
        if (!HasComp<HumanoidProfileComponent>(ev.Mob))
            return;

        var comp = EnsureComp<HealthPhrasesComponent>(ev.Mob);
        var p = ev.Profile.HealthPhrases;

        comp.CustomPopup70 = new List<string>(p.Popup70);
        comp.CustomWhisper70 = new List<string>(p.Whisper70);
        comp.CustomPopup55 = new List<string>(p.Popup55);
        comp.CustomWhisper55 = new List<string>(p.Whisper55);
        comp.CustomPopup40 = new List<string>(p.Popup40);
        comp.CustomWhisper40 = new List<string>(p.Whisper40);
        comp.CustomPopup25 = new List<string>(p.Popup25);
        comp.CustomWhisper25 = new List<string>(p.Whisper25);
        comp.CustomPopup10 = new List<string>(p.Popup10);
        comp.CustomWhisper10 = new List<string>(p.Whisper10);
        comp.CustomPopup5 = new List<string>(p.Popup5);
        comp.CustomWhisper5 = new List<string>(p.Whisper5);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<HealthPhrasesComponent, DamageableComponent, HumanoidProfileComponent, MobStateComponent>();

        while (query.MoveNext(out var uid, out var phrases, out var damageable, out var humanoid, out var mobState))
        {
            if (mobState.CurrentState != MobState.Alive)
                continue;

            if (!TryComp<MobThresholdsComponent>(uid, out var thresholds))
                continue;

            FixedPoint2 critThreshold = 0;
            foreach (var (threshold, state) in thresholds.Thresholds)
            {
                if (state == MobState.Critical)
                {
                    critThreshold = threshold;
                    break;
                }
            }

            if (critThreshold <= 0)
                continue;

            var hpPercent = Math.Clamp(1f - (float)(_damageable.GetTotalDamage((uid, damageable)) / critThreshold), 0f, 1f);
            var level = GetHpLevel(hpPercent);
            if (level == HpLevel.None)
                continue;

            var raceKey = GetRaceKey(humanoid.Species.Id);

            // ── Popup ──────────────────────────────────────────────────────
            if (now >= phrases.NextPopupTime)
            {
                TryOutputPhrase(uid, phrases, level, PhraseType.Popup, raceKey);
                phrases.NextPopupTime = now + TimeSpan.FromSeconds(_random.NextFloat(_popupMin, _popupMax));
            }

            // ── Whisper (с Level25) ────────────────────────────────────────
            if (level >= HpLevel.Level25 && now >= phrases.NextSpeechTime)
            {
                TryOutputPhrase(uid, phrases, level, PhraseType.Whisper, raceKey);
                phrases.NextSpeechTime = now + TimeSpan.FromSeconds(_random.NextFloat(_whisperMin, _whisperMax));
            }

            // ── Крик боли на критичном HP (5-10% и 0-5%) — публичный, с тряской ─
            if (level is HpLevel.Level10 or HpLevel.Level5 && now >= phrases.NextCritScreamTime)
            {
                TryOutputScream(uid, phrases, level, raceKey);
                phrases.NextCritScreamTime = now + TimeSpan.FromSeconds(_random.NextFloat(_critScreamMin, _critScreamMax));
            }
        }
    }

    /// <summary>
    /// Крик боли на критичном HP (Level10/Level5) — переиспользует существующие FTL-фразы
    /// "-say-" (это уже готовый предсмертный/болевой крик, просто раньше нигде не читался).
    /// Настоящая say-реплика (чат-лог + дрожащий речевой пузырь), а не popup — см.
    /// <see cref="ChatSystem.SendDutyHealthScream"/>.
    /// </summary>
    private void TryOutputScream(EntityUid uid, HealthPhrasesComponent phrases, HpLevel level, string raceKey)
    {
        var text = PickPhrase(phrases, level, PhraseType.Say, raceKey);
        if (text == null)
            return;

        _chat.SendDutyHealthScream(uid, text);
    }

    /// <summary>
    /// Крик боли от любого достаточно сильного урона, с шансом и кулдауном — аналог
    /// EmoteOnDamage из RussianCM/RMC14, но встроенный в HealthPhrasesComponent, а не отдельная
    /// система. Не зависит от текущего HP — может сработать даже на полном здоровье.
    /// </summary>
    private void OnDamageDealt(Entity<HealthPhrasesComponent> ent, ref DamageDealtEvent args)
    {
        if (!_enabled)
            return;

        if (args.Damage.GetTotal() < _damageScreamThreshold)
            return;

        if (!TryComp<MobStateComponent>(ent, out var mobState) || mobState.CurrentState != MobState.Alive)
            return;

        var now = _timing.CurTime;
        if (now < ent.Comp.NextDamageScreamTime)
            return;

        if (!_random.Prob(_damageScreamChance))
            return;

        // Кулдаун выставляем сразу, даже если фразы не найдётся — иначе при отсутствии контента
        // для расы попытки будут повторяться каждый тик урона впустую.
        ent.Comp.NextDamageScreamTime = now + TimeSpan.FromSeconds(_damageScreamCooldown);

        var raceKey = TryComp<HumanoidProfileComponent>(ent, out var humanoid) ? GetRaceKey(humanoid.Species.Id) : "general";
        var text = PickScreamPhrase(raceKey);
        if (text == null)
            return;

        _chat.SendDutyHealthScream(ent, text);
    }

    /// <summary>Короткий крик боли ("АААА!" и расовые варианты) — не привязан к HpLevel.</summary>
    private string? PickScreamPhrase(string raceKey)
    {
        if (TryPickFtlPhrase($"duty-health-phrases-{raceKey}-scream", out var racial))
            return racial;

        if (TryPickFtlPhrase("duty-health-phrases-general-scream", out var general))
            return general;

        return null;
    }

    public void TryOutputPhrase(EntityUid uid, HealthPhrasesComponent phrases, HpLevel level, PhraseType type, string raceKey)
    {
        var text = PickPhrase(phrases, level, type, raceKey);
        if (text == null)
            return;

        if (type == PhraseType.Popup)
        {
            // Ниже ~40% HP (Level40 и хуже) боль уже не абстрактная — текст слегка дрожит.
            var popupType = level >= HpLevel.Level40 ? PopupType.DutyHealthPainShake : PopupType.DutyHealthPain;
            _popup.PopupEntity(text, uid, uid, popupType);
        }
        else
            _chat.SendDutyHealthPainWhisper(uid, text);
    }

    private string? PickPhrase(HealthPhrasesComponent comp, HpLevel level, PhraseType type, string raceKey)
    {
        var customList = GetCustomList(comp, level, type);
        if (customList.Count > 0)
            return _random.Pick(customList);

        var ftlKey = BuildFtlKey(raceKey, level, type);
        if (TryPickFtlPhrase(ftlKey, out var racial))
            return racial;

        var generalKey = BuildFtlKey("general", level, type);
        if (TryPickFtlPhrase(generalKey, out var general))
            return general;

        return null;
    }

    private bool TryPickFtlPhrase(string baseKey, out string? result)
    {
        var variants = new List<string>();
        var i = 1;
        while (Loc.TryGetString($"{baseKey}-{i}", out var str))
        {
            variants.Add(str);
            i++;
        }

        if (variants.Count == 0)
        {
            result = null;
            return false;
        }

        result = _random.Pick(variants);
        return true;
    }

    private string BuildFtlKey(string race, HpLevel level, PhraseType type)
    {
        // Ключи локали исторически названы по верхней границе диапазона, а не по HpLevel:
        // Level70 (55-70% HP) -> "50", Level55 (40-55%) -> "40", Level40 (25-40%) -> "35",
        // Level25 (10-25%) -> "25", Level10 (5-10%) -> "10", Level5 (0-5%) -> "5".
        // См. health_phrases.ftl — несовпадение чисел с реальным % HP намеренное, не баг.
        var levelStr = level switch
        {
            HpLevel.Level70 => "50",
            HpLevel.Level55 => "40",
            HpLevel.Level40 => "35",
            HpLevel.Level25 => "25",
            HpLevel.Level10 => "10",
            HpLevel.Level5  => "5",
            _ => "50"
        };
        var typeStr = type switch
        {
            PhraseType.Whisper => "whisper",
            PhraseType.Say => "say",
            _ => "popup"
        };
        return $"duty-health-phrases-{race}-{levelStr}-{typeStr}";
    }

    private List<string> GetCustomList(HealthPhrasesComponent comp, HpLevel level, PhraseType type) => (level, type) switch
    {
        (HpLevel.Level70, PhraseType.Popup) => comp.CustomPopup70,
        (HpLevel.Level70, PhraseType.Whisper) => comp.CustomWhisper70,
        (HpLevel.Level55, PhraseType.Popup) => comp.CustomPopup55,
        (HpLevel.Level55, PhraseType.Whisper) => comp.CustomWhisper55,
        (HpLevel.Level40, PhraseType.Popup) => comp.CustomPopup40,
        (HpLevel.Level40, PhraseType.Whisper) => comp.CustomWhisper40,
        (HpLevel.Level25, PhraseType.Popup) => comp.CustomPopup25,
        (HpLevel.Level25, PhraseType.Whisper) => comp.CustomWhisper25,
        (HpLevel.Level10, PhraseType.Popup) => comp.CustomPopup10,
        (HpLevel.Level10, PhraseType.Whisper) => comp.CustomWhisper10,
        (HpLevel.Level5, PhraseType.Popup) => comp.CustomPopup5,
        (HpLevel.Level5, PhraseType.Whisper) => comp.CustomWhisper5,
        _ => new List<string>()
    };

    public HpLevel GetHpLevel(float hp) => hp switch
    {
        >= 0.55f and < 0.70f => HpLevel.Level70,
        >= 0.40f and < 0.55f => HpLevel.Level55,
        >= 0.25f and < 0.40f => HpLevel.Level40,
        >= 0.10f and < 0.25f => HpLevel.Level25,
        >= 0.05f and < 0.10f => HpLevel.Level10,
        >= 0.00f and < 0.05f => HpLevel.Level5,
        _ => HpLevel.None
    };

    private string GetRaceKey(string speciesId) => speciesId switch
    {
        "MobReptilian" => "unath",
        "MobResomi"    => "rezomi",
        "MobMoth"      => "nian",
        "MobDrask"     => "drask",
        _ => "general"
    };

    // ── Консольная команда ────────────────────────────────────────────────────

    [AdminCommand(AdminFlags.Admin)]
    private void TestCommand(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError("Использование: duty_hp_test <username> [popup|whisper]");
            return;
        }

        if (!_playerManager.TryGetSessionByUsername(args[0], out var session))
        {
            shell.WriteError($"Игрок '{args[0]}' не найден.");
            return;
        }

        var mob = session.AttachedEntity;
        if (mob == null || !TryComp<HealthPhrasesComponent>(mob, out var phrases))
        {
            shell.WriteError("У игрока нет компонента HealthPhrases или он не заспавнен.");
            return;
        }

        if (!TryComp<DamageableComponent>(mob, out var damageable) ||
            !TryComp<MobThresholdsComponent>(mob, out var thresholds) ||
            !TryComp<HumanoidProfileComponent>(mob, out var humanoid))
        {
            shell.WriteError("Сущность не подходит для системы фраз.");
            return;
        }

        FixedPoint2 critThreshold = 0;
        foreach (var (threshold, state) in thresholds.Thresholds)
        {
            if (state == MobState.Critical) { critThreshold = threshold; break; }
        }

        if (critThreshold <= 0)
        {
            shell.WriteError("Не удалось определить порог крита.");
            return;
        }

        var hpPercent = Math.Clamp(1f - (float)(_damageable.GetTotalDamage((mob.Value, damageable)) / critThreshold), 0f, 1f);
        var level = GetHpLevel(hpPercent);

        if (level == HpLevel.None)
        {
            shell.WriteError($"HP {hpPercent:P0} — вне диапазона системы (нужно ниже 70%).");
            return;
        }

        var typeArg = args.Length >= 2 ? args[1].ToLower() : "popup";
        var type = typeArg switch
        {
            "whisper" => PhraseType.Whisper,
            "say" => PhraseType.Say,
            _ => PhraseType.Popup
        };
        var raceKey = GetRaceKey(humanoid.Species.Id);

        if (type == PhraseType.Say)
            TryOutputScream(mob.Value, phrases, level, raceKey);
        else
            TryOutputPhrase(mob.Value, phrases, level, type, raceKey);
        shell.WriteLine($"Фраза выдана: уровень {level}, тип {type}, раса {raceKey}.");
    }

    private CompletionResult TestCommandCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var names = _playerManager.Sessions.Select(s => new CompletionOption(s.Name));
            return CompletionResult.FromOptions(names);
        }

        if (args.Length == 2)
            return CompletionResult.FromOptions(new[] { new CompletionOption("popup"), new CompletionOption("whisper"), new CompletionOption("say") });

        return CompletionResult.Empty;
    }
}

public enum HpLevel
{
    None,
    Level70,
    Level55,
    Level40,
    Level25,
    Level10,
    Level5,
}

public enum PhraseType
{
    Popup,
    Whisper,

    /// <summary>Крик боли на критичном HP — публичный DutyHealthScream popup, ключ "-say-" в FTL.</summary>
    Say,
}
