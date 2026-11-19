// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Client.Resources;
using Content.Shared._Duty.Neurointerface;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Neurointerface;

/// <summary>
/// _Duty: верхне-левый HUD нейроинтерфейса NT-SEC в стиле проекции на линзу визора.
/// Без окон и рамок: текст рисуется вручную в <see cref="Draw"/> (свечение, хроматическая аберрация,
/// сканлайны шейдера DutyNeuroLens), под каждой строкой — узкая тёмная плашка по ширине текста,
/// чтобы читалось на светлом полу. Анимация расшифровки и последовательность отключения при смерти сохранены.
/// </summary>
public sealed partial class DutyNeuroHudWidget : UIWidget
{
    private const float FadeInDuration = 0.35f;
    private const float DeathClearedHoldDuration = 0.35f;
    private const float DeathFadeOutDuration = 0.45f;

    // --- Шрифты (pt). Подгонка размера HUD — здесь. ---
    private const int HeaderFontSize = 14;
    private const int KeyFontSize = 13;
    private const int ValueFontSize = 13;
    private const int SmallFontSize = 12;

    // --- Лимиты усечения (символы). «…» — только для экстремальных длин. ---
    private const int MemberNameMaxLen = 32;
    private const int LocationMaxLen = 40;
    private const int CodeMaxLen = 32;
    private const int TrackerLineMaxLen = 52;
    private const int OrderLineMaxLen = 52;

    // --- Геометрия (виртуальные px, умножаются на UIScale). ---
    private const float BarWidth = 2f;
    private const float TextIndent = 9f;
    private const float KeySeparatorGap = 5f;
    private const float ValueGap = 7f;
    private const float HeaderTimeGap = 12f;
    private const float RowSpacing = 3f;
    private const float SectionGap = 9f;
    private const float PlatePadX = 5f;
    private const float PlatePadY = 1f;

    // --- Эффект линзы. ---
    /// <summary>Сдвиг красной/голубой копий хроматической аберрации.</summary>
    private const float AberrationOffset = 1f;
    private const float AberrationAlpha = 0.32f;
    /// <summary>Сдвиг копий «свечения» (4 стороны) и их альфа относительно основного цвета.</summary>
    private const float GlowOffset = 1.5f;
    private const float GlowAlpha = 0.14f;
    /// <summary>Сила сканлайнов шейдера (0 — нет, 1 — полосы до прозрачности).</summary>
    private const float ScanlineStrength = 0.22f;
    /// <summary>
    /// Лёгкий наклон плоскости линзы через SetTransform: X — сдвиг по X от высоты строки, Y — от ширины.
    /// Если глифы мылятся — выставить оба в 0.
    /// </summary>
    private const float LensShearX = 0f;
    private const float LensShearY = 0.018f;
    /// <summary>Максимальный рывок строки по X при глитче (смерть, расшифровка).</summary>
    private const float GlitchJitter = 4f;

    /// <summary>Разделитель «ключ :: значение» — не текст для перевода, а элемент интерфейса.</summary>
    private const string KeySeparator = "::";

    private static readonly ProtoId<ShaderPrototype> LensShaderId = "DutyNeuroLens";

    private static readonly Color KeyBlue = Color.FromHex("#3D8BFF");
    private static readonly Color HeaderBlue = Color.FromHex("#4F9BFF");
    private static readonly Color TimeBlue = Color.FromHex("#7DB6FF");
    private static readonly Color ValueLight = Color.FromHex("#E4F3FF");
    private static readonly Color LocationLight = Color.FromHex("#CDEBFF");
    private static readonly Color CriticalYellow = Color.FromHex("#FFD166");
    private static readonly Color StatusCritical = Color.FromHex("#FF9F1C");
    private static readonly Color TrackerRed = Color.FromHex("#FF334B");
    private static readonly Color OrderOrange = Color.FromHex("#FF6B3D");
    private static readonly Color MutedEmpty = Color.FromHex("#7F93B3");
    private static readonly Color PlateColor = new(0.01f, 0.025f, 0.05f, 0.58f);
    private static readonly Color BarCyan = Color.FromHex("#2CE6FF").WithAlpha(0.8f);
    private static readonly Color BarCrimson = Color.FromHex("#D92B44").WithAlpha(0.8f);
    private static readonly Color AberrationRed = Color.FromHex("#FF3B5C").WithAlpha(AberrationAlpha);
    private static readonly Color AberrationCyan = Color.FromHex("#3BE8FF").WithAlpha(AberrationAlpha);

    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    // Шейдер уникален для виджета (свои параметры alpha/glitch). Виджет живёт, пока жив экран,
    // и пересоздаётся только при смене экрана — экземпляр уходит вместе с ним в GC.
    private readonly ShaderInstance _lensShader;
    private readonly Font _headerFont;
    private readonly Font _keyFont;
    private readonly Font _valueFont;
    private readonly Font _timeFont;
    private readonly Font _smallFont;

    // Локализованные строки кэшируются один раз: Loc.GetString каждый кадр не зовём.
    private readonly string _headerText;
    private readonly string _keyFighter;
    private readonly string _keyLocation;
    private readonly string _keyStatus;
    private readonly string _keyCode;
    private readonly string _keyTracker;
    private readonly string _keyOrder;
    private readonly string _noSignalsText;
    private readonly string _orderUnknownText;
    private readonly string[] _statusTexts;
    private readonly string[] _orderTexts;
    private readonly string[] _allKeys;

    private readonly DutyNeuroScrambleText _headerScramble = new(string.Empty, true);
    private readonly HudRow _locationRow = new(LocationLight);
    private readonly HudRow _statusRow = new(ValueLight);
    private readonly HudRow _codeRow = new(Color.LimeGreen);

    private readonly List<HudRow> _memberRows = new();
    private readonly List<HudRow> _trackerRows = new();
    private readonly List<HudRow> _orderRows = new();

    private string _roundTimeText = string.Empty;
    private string _emptyAlertText = string.Empty;
    private bool _emptyAlertVisible = true;
    /// <summary>false в фазе «очищено» при смерти: ключи строк тоже гаснут, остаётся пустой визор.</summary>
    private bool _keysVisible;

    private float _fadeAlpha;
    private float _glitchIntensity;
    private bool _wasActive;
    private DeathHudPhase _deathPhase;
    private float _deathPhaseTimer;

    /// <summary>Последняя показанная минута раунда; -1 — строку нужно пересобрать.</summary>
    private int _lastRoundMinutes = -1;

    // Кэш ширины колонки ключей: зависит только от масштаба UI, пересчитываем при его смене.
    private float _keyMetricsScale = -1f;
    private float _keyColumnPx;
    private float _separatorPx;

    /// <summary>Размер содержимого в виртуальных px; InvalidateMeasure — только при его изменении.</summary>
    private Vector2 _contentSize;

    private enum DeathHudPhase : byte
    {
        None,
        Glitching,
        Cleared,
        FadingOut
    }

    /// <summary>Кэш усечения строки: пересобираем, только когда исходный текст изменился.</summary>
    private sealed class TruncCache
    {
        private string? _raw;
        private string _value = string.Empty;

        public string Get(string? raw, int maxLen)
        {
            if (!string.Equals(_raw, raw, StringComparison.Ordinal))
            {
                _raw = raw;
                _value = Truncate(raw, maxLen);
            }

            return _value;
        }
    }

    /// <summary>Строка «КЛЮЧ :: значение»: текст значения идёт через scramble, цвет задаётся каждый апдейт.</summary>
    private sealed class HudRow
    {
        public readonly DutyNeuroScrambleText Scramble = new(string.Empty, true);
        public readonly TruncCache Trunc = new();
        public Color ValueColor;
        public bool Visible = true;

        /// <summary>Чей это сигнал (трекер — сущность, приказ — id): по нему строка живёт на своём месте и исчезает анимацией.</summary>
        public int Key = -1;

        /// <summary>Отметка «сигнал ещё есть» на текущем апдейте.</summary>
        public bool Seen;

        // Данные, по которым собрана текущая строка тревоги: пересобираем текст только при их изменении.
        private string? _lastName;
        private string? _lastZone;
        private int _lastType = -1;

        public HudRow(Color valueColor)
        {
            ValueColor = valueColor;
        }

        /// <summary>Возвращает true, если данные изменились и нужно собрать новую строку.</summary>
        public bool NeedsRebuild(string? name, string? zone, int type)
        {
            return !string.Equals(_lastName, name, StringComparison.Ordinal) ||
                   !string.Equals(_lastZone, zone, StringComparison.Ordinal) ||
                   _lastType != type;
        }

        /// <summary>Сбрасывает кэш: после очистки scramble строку надо задать заново.</summary>
        public void Invalidate()
        {
            _lastName = null;
            _lastZone = null;
            _lastType = -1;
        }

        public string Store(string? name, string? zone, int type, string text)
        {
            _lastName = name;
            _lastZone = zone;
            _lastType = type;
            return text;
        }
    }

    public DutyNeuroHudWidget()
    {
        IoCManager.InjectDependencies(this);

        MouseFilter = MouseFilterMode.Ignore;
        Visible = false;

        _headerText = Loc.GetString("duty-neuro-hud-header");
        _keyFighter = Loc.GetString("duty-neuro-hud-fighter");
        _keyLocation = Loc.GetString("duty-neuro-hud-location");
        _keyStatus = Loc.GetString("duty-neuro-hud-status");
        _keyCode = Loc.GetString("duty-neuro-hud-code");
        _keyTracker = Loc.GetString("duty-neuro-hud-tracker");
        _keyOrder = Loc.GetString("duty-neuro-hud-order");
        _noSignalsText = Loc.GetString("duty-neuro-hud-no-signals");
        _orderUnknownText = Loc.GetString("duty-neuro-hud-order-unknown");
        _allKeys = new[] { _keyFighter, _keyLocation, _keyStatus, _keyCode, _keyTracker, _keyOrder };

        // Порядок строго по DutyNeuroMemberStatus: Alive, Critical, Dead, Offline.
        _statusTexts = new[]
        {
            Loc.GetString("duty-neuro-status-alive"),
            Loc.GetString("duty-neuro-status-crit"),
            Loc.GetString("duty-neuro-status-dead"),
            Loc.GetString("duty-neuro-status-offline")
        };

        // Порядок строго по DutyNeuroOrderType; в HUD капсом, а в ftl значения общие с планшетом.
        _orderTexts = new[]
        {
            Loc.GetString("duty-neuro-order-move").ToUpperInvariant(),
            Loc.GetString("duty-neuro-order-assault").ToUpperInvariant(),
            Loc.GetString("duty-neuro-order-hold").ToUpperInvariant(),
            Loc.GetString("duty-neuro-order-retreat").ToUpperInvariant(),
            Loc.GetString("duty-neuro-order-sos").ToUpperInvariant(),
            Loc.GetString("duty-neuro-order-recon").ToUpperInvariant()
        };

        _lensShader = _proto.Index(LensShaderId).InstanceUnique();

        // Все цепочки с запасным NotoSans: у него гарантированно есть кириллица.
        _headerFont = _resCache.GetFont(new[]
        {
            "/Fonts/NotoSans/NotoSans-BoldItalic.ttf",
            "/Fonts/NotoSans/NotoSans-Bold.ttf"
        }, HeaderFontSize);
        // Ключи — моноширинный жирный: колонка «КЛЮЧ ::» ровная.
        _keyFont = _resCache.GetFont(new[]
        {
            "/Fonts/RobotoMono/RobotoMono-Bold.ttf",
            "/Fonts/NotoSans/NotoSans-Bold.ttf"
        }, KeyFontSize);
        // Значения — пропорциональный жирный: длинная строка приказа заметно уже, чем в моно.
        _valueFont = _resCache.GetFont(new[]
        {
            "/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf",
            "/Fonts/NotoSans/NotoSans-Bold.ttf"
        }, ValueFontSize);
        // Время — моно, чтобы цифры не прыгали по ширине.
        _timeFont = _resCache.GetFont(new[]
        {
            "/Fonts/RobotoMono/RobotoMono-Bold.ttf",
            "/Fonts/NotoSans/NotoSans-Bold.ttf"
        }, KeyFontSize);
        _smallFont = _resCache.GetFont(new[]
        {
            "/Fonts/RobotoMono/RobotoMono-Regular.ttf",
            "/Fonts/NotoSans/NotoSans-Regular.ttf"
        }, SmallFontSize);
    }

    public void UpdateState(DutyNeurointerfaceWearerComponent? wearer, TimeSpan roundTime, float frameTime)
    {
        if (wearer == null || !wearer.Active)
        {
            ResetOnInactive();
            return;
        }

        if (!_wasActive)
        {
            _wasActive = true;

            // Вход/реконнект в уже мёртвое тело: сразу скрытое состояние, без повторного глитча и fade-out.
            if (wearer.OwnerStatus == DutyNeuroMemberStatus.Dead)
            {
                EnterHiddenDeadState();
                return;
            }

            _fadeAlpha = 0f;
            _glitchIntensity = 0.35f;
            _deathPhase = DeathHudPhase.None;
            _deathPhaseTimer = 0f;

            _headerScramble.SetText(_headerText, forceScramble: true);
            RestoreStaticLabels();
        }

        // Обработка смерти: глитч 0.85с -> очистка текста (пустой визор) -> fade out.
        if (wearer.OwnerStatus == DutyNeuroMemberStatus.Dead)
        {
            UpdateDeathSequence(frameTime);
            UpdateContentSize();
            return;
        }

        if (_deathPhase != DeathHudPhase.None)
        {
            // При реанимации восстанавливаем HUD.
            _deathPhase = DeathHudPhase.None;
            _deathPhaseTimer = 0f;
            _fadeAlpha = 0f;
            _headerScramble.SetText(_headerText, forceScramble: true);
            RestoreStaticLabels();
        }

        Visible = true;
        _fadeAlpha = MathF.Min(1f, _fadeAlpha + frameTime / FadeInDuration);
        _glitchIntensity = MathF.Max(0f, _glitchIntensity - frameTime * 0.9f);

        // Время раунда ЧЧ:ММ — строка меняется раз в минуту, пересобираем только тогда.
        var totalMinutes = (int) Math.Max(0, roundTime.TotalMinutes);
        if (totalMinutes != _lastRoundMinutes)
        {
            _lastRoundMinutes = totalMinutes;
            _roundTimeText = $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";
        }

        // Бойцы отряда (исключаем погибших)
        UpdateMemberRows(wearer.SquadMembers, frameTime);

        // Локация, статус, код
        _locationRow.Scramble.SetText(_locationRow.Trunc.Get(wearer.LocationName, LocationMaxLen));
        _locationRow.Scramble.Update(frameTime);

        // Статус берём из enum, а не из StatusText: текст локализован у клиента и не зависит от сервера.
        _statusRow.Scramble.SetText(GetStatusText(wearer.OwnerStatus));
        _statusRow.Scramble.Update(frameTime);
        _statusRow.ValueColor = wearer.OwnerStatus switch
        {
            DutyNeuroMemberStatus.Critical => StatusCritical,
            DutyNeuroMemberStatus.Dead => TrackerRed,
            _ => ValueLight
        };

        _codeRow.Scramble.SetText(_codeRow.Trunc.Get(wearer.AlertCodeName, CodeMaxLen));
        _codeRow.Scramble.Update(frameTime);
        _codeRow.ValueColor = wearer.AlertCodeColor;

        _headerScramble.Update(frameTime);

        // Лёгкий глитч при активной расшифровке
        if (_locationRow.Scramble.IsAnimating || _codeRow.Scramble.IsAnimating || _statusRow.Scramble.IsAnimating)
            _glitchIntensity = MathF.Max(_glitchIntensity, 0.12f);

        // Трекеры (сверху) и приказы (до 3, новые внизу)
        UpdateAlertRows(wearer.Trackers, wearer.Orders, frameTime);

        ApplyFadeAlpha();
        UpdateContentSize();
    }

    /// <summary>
    /// Единственный механизм fade: Modulate виджета умножает всё, что рисует Draw.
    /// Шейдер и Draw альфу fade не умножают — иначе затухание было бы квадратичным и короче заявленного.
    /// </summary>
    private void ApplyFadeAlpha()
    {
        Modulate = Color.White.WithAlpha(_fadeAlpha);
    }

    private void EnterHiddenDeadState()
    {
        _deathPhase = DeathHudPhase.FadingOut;
        _deathPhaseTimer = DeathFadeOutDuration;
        _fadeAlpha = 0f;
        _glitchIntensity = 0f;
        Visible = false;
        ClearAllHudText();
        ApplyFadeAlpha();
    }

    private void UpdateDeathSequence(float frameTime)
    {
        if (_deathPhase == DeathHudPhase.None)
        {
            _deathPhase = DeathHudPhase.Glitching;
            _deathPhaseTimer = 0f;
            TriggerAllDeathGlitch();
        }

        _deathPhaseTimer += MathF.Max(0f, frameTime);

        switch (_deathPhase)
        {
            case DeathHudPhase.Glitching:
                Visible = true;
                _fadeAlpha = 1f;
                _glitchIntensity = 1f;
                UpdateAllScrambles(frameTime);

                if (_deathPhaseTimer >= DutyNeuroClientSystem.HudDeathGlitchDuration)
                {
                    _deathPhase = DeathHudPhase.Cleared;
                    _deathPhaseTimer = 0f;
                    ClearAllHudText();
                }
                break;

            case DeathHudPhase.Cleared:
                Visible = true;
                _fadeAlpha = 1f;
                _glitchIntensity = 0.45f;
                if (_deathPhaseTimer >= DeathClearedHoldDuration)
                {
                    _deathPhase = DeathHudPhase.FadingOut;
                    _deathPhaseTimer = 0f;
                }
                break;

            case DeathHudPhase.FadingOut:
                _glitchIntensity = 0.2f;
                _fadeAlpha = MathF.Max(0f, 1f - _deathPhaseTimer / DeathFadeOutDuration);
                Visible = _fadeAlpha > 0.005f;
                break;
        }

        ApplyFadeAlpha();
    }

    private void TriggerAllDeathGlitch()
    {
        const float duration = DutyNeuroClientSystem.HudDeathGlitchDuration;
        _headerScramble.TriggerGlitchBurst(duration);
        _locationRow.Scramble.TriggerGlitchBurst(duration);
        _statusRow.Scramble.TriggerGlitchBurst(duration);
        _codeRow.Scramble.TriggerGlitchBurst(duration);

        foreach (var row in _memberRows)
            row.Scramble.TriggerGlitchBurst(duration);
        foreach (var row in _trackerRows)
            row.Scramble.TriggerGlitchBurst(duration);
        foreach (var row in _orderRows)
            row.Scramble.TriggerGlitchBurst(duration);
    }

    private void UpdateAllScrambles(float frameTime)
    {
        _headerScramble.Update(frameTime);
        _locationRow.Scramble.Update(frameTime);
        _statusRow.Scramble.Update(frameTime);
        _codeRow.Scramble.Update(frameTime);

        foreach (var row in _memberRows)
            row.Scramble.Update(frameTime);
        foreach (var row in _trackerRows)
            row.Scramble.Update(frameTime);
        foreach (var row in _orderRows)
            row.Scramble.Update(frameTime);
    }

    private void ClearAllHudText()
    {
        _lastRoundMinutes = -1;
        _roundTimeText = string.Empty;
        _emptyAlertText = string.Empty;
        _keysVisible = false;

        _headerScramble.Clear();
        _locationRow.Scramble.Clear();
        _statusRow.Scramble.Clear();
        _codeRow.Scramble.Clear();

        foreach (var row in _memberRows)
            row.Scramble.Clear();

        // Строки сигналов привязаны к ключам — после сброса HUD они соберутся заново, без анимации исчезновения.
        _trackerRows.Clear();
        _orderRows.Clear();
    }

    private void RestoreStaticLabels()
    {
        _keysVisible = true;
        _emptyAlertText = _noSignalsText;
    }

    private void UpdateMemberRows(List<DutyNeuroSquadMemberEntry> members, float frameTime)
    {
        var activeCount = 0;
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            // Офлайн-бойцы приходят ради обводки, но в списке HUD показываем только тех, кто в игре.
            if (member.Status is DutyNeuroMemberStatus.Dead or DutyNeuroMemberStatus.Offline)
                continue;

            if (activeCount >= _memberRows.Count)
                _memberRows.Add(new HudRow(ValueLight));

            var row = _memberRows[activeCount];
            row.Visible = true;
            row.Scramble.SetText(row.Trunc.Get(member.Name, MemberNameMaxLen));
            row.Scramble.Update(frameTime);
            row.ValueColor = member.Status == DutyNeuroMemberStatus.Critical
                ? CriticalYellow
                : ValueLight;

            activeCount++;
        }

        for (var i = activeCount; i < _memberRows.Count; i++)
            _memberRows[i].Visible = false;
    }

    private void UpdateAlertRows(
        List<DutyNeuroTrackerEntry> trackers,
        List<DutyNeuroOrderEntry> orders,
        float frameTime)
    {
        // 1) Трекеры критического состояния — всегда ВЫШЕ приказов
        foreach (var row in _trackerRows)
            row.Seen = false;

        for (var i = 0; i < trackers.Count; i++)
        {
            var entry = trackers[i];
            var row = GetOrAddKeyedRow(_trackerRows, entry.Entity.Id, TrackerRed);
            if (row.NeedsRebuild(entry.Name, entry.ZoneName, 0))
            {
                var line = Loc.GetString("duty-neuro-hud-tracker-line",
                    ("name", entry.Name), ("zone", entry.ZoneName));
                row.Scramble.SetText(Truncate(row.Store(entry.Name, entry.ZoneName, 0, line), TrackerLineMaxLen));
            }
        }

        // 2) Приказы — до 3 строк, самые новые внизу
        foreach (var row in _orderRows)
            row.Seen = false;

        var orderStart = Math.Max(0, orders.Count - 3);
        for (var i = orderStart; i < orders.Count; i++)
        {
            var order = orders[i];
            var row = GetOrAddKeyedRow(_orderRows, order.OrderId, OrderOrange);
            var typeIdx = (int) order.OrderType;
            if (row.NeedsRebuild(null, order.ZoneName, typeIdx))
            {
                var line = Loc.GetString("duty-neuro-hud-order-line",
                    ("type", GetOrderTypeLabel(order.OrderType)), ("zone", order.ZoneName));
                row.Scramble.SetText(Truncate(row.Store(null, order.ZoneName, typeIdx, line), OrderLineMaxLen));
            }
        }

        var trackersLeft = FinishKeyedRows(_trackerRows, frameTime);
        var ordersLeft = FinishKeyedRows(_orderRows, frameTime);

        // «Нет сигналов» — только когда последний сигнал доиграл исчезновение, иначе строки наложатся.
        var hasAny = trackersLeft || ordersLeft;
        _emptyAlertVisible = !hasAny;
        if (!hasAny)
            _emptyAlertText = _noSignalsText;
    }

    /// <summary>
    /// Строка сигнала по ключу. Пропавший и вернувшийся сигнал (например, отмена снятия) расшифровывается заново.
    /// </summary>
    private static HudRow GetOrAddKeyedRow(List<HudRow> rows, int key, Color color)
    {
        foreach (var existing in rows)
        {
            if (existing.Key != key || existing.Seen)
                continue;

            if (existing.Scramble.IsDissolving || existing.Scramble.IsDissolved)
            {
                existing.Invalidate();
                existing.Scramble.Clear();
            }

            existing.Seen = true;
            return existing;
        }

        // Новые сигналы встают в конец секции — у приказов это и есть «новые внизу».
        var row = new HudRow(color) { Key = key, Seen = true };
        rows.Add(row);
        return row;
    }

    /// <summary>
    /// Пропавшие сигналы запускают исчезновение на своём месте, доигравшие удаляются.
    /// Возвращает true, если в секции осталось что рисовать.
    /// </summary>
    private static bool FinishKeyedRows(List<HudRow> rows, float frameTime)
    {
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            var row = rows[i];
            if (!row.Seen)
                row.Scramble.StartDissolve();

            row.Scramble.Update(frameTime);

            if (row.Scramble.IsDissolved)
            {
                rows.RemoveAt(i);
                continue;
            }

            row.Visible = true;
        }

        return rows.Count > 0;
    }

    private void ResetOnInactive()
    {
        if (!_wasActive && !Visible)
            return;

        _wasActive = false;
        _fadeAlpha = 0f;
        _glitchIntensity = 0f;
        _deathPhase = DeathHudPhase.None;
        _deathPhaseTimer = 0f;
        Visible = false;
        ClearAllHudText();
    }

    #region Layout & Draw

    /// <summary>
    /// Пересчитывает размер по целевым (не анимированным) текстам — чтобы расшифровка не дёргала
    /// layout каждый кадр. Контроллер использует DesiredSize для клампа позиции к экрану.
    /// </summary>
    private void UpdateContentSize()
    {
        var scale = UIScale;
        if (scale <= 0f)
            scale = 1f;

        var sizePx = RunLayout(null, scale, true);
        var size = sizePx / scale;
        if (MathF.Abs(size.X - _contentSize.X) < 0.5f && MathF.Abs(size.Y - _contentSize.Y) < 0.5f)
            return;

        _contentSize = size;
        InvalidateMeasure();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return _contentSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (_fadeAlpha <= 0.005f)
            return;

        var scale = UIScale;

        // Мерцание проекции и сканлайны — в шейдере DutyNeuroLens. Общую прозрачность (fade) даёт Modulate.
        var time = (float) _timing.RealTime.TotalSeconds;
        var holoFlicker = 0.95f + 0.05f * MathF.Sin(time * 13f) * MathF.Cos(time * 4.7f);
        _lensShader.SetParameter("alpha", holoFlicker);
        _lensShader.SetParameter("glitch_intensity", _glitchIntensity);
        _lensShader.SetParameter("scan_strength", ScanlineStrength);

        // Лёгкий наклон плоскости линзы: локальный сдвиг применяется до базовой трансформации контрола.
        var baseTransform = handle.GetTransform();
        var sheared = LensShearX != 0f || LensShearY != 0f;
        if (sheared)
            handle.SetTransform(new Matrix3x2(1f, LensShearY, LensShearX, 1f, 0f, 0f) * baseTransform);

        handle.UseShader(_lensShader);
        RunLayout(handle, scale, false);
        handle.UseShader(null);

        if (sheared)
            handle.SetTransform(baseTransform);
    }

    /// <summary>
    /// Единый проход раскладки: без handle только меряет (по целевым текстам), с handle — рисует текущие.
    /// Возвращает размер содержимого в пикселях.
    /// </summary>
    private Vector2 RunLayout(DrawingHandleScreen? handle, float scale, bool measureTarget)
    {
        EnsureKeyMetrics(scale);

        var x0 = (BarWidth + TextIndent) * scale;
        var valueX = x0 + _keyColumnPx + KeySeparatorGap * scale + _separatorPx + ValueGap * scale;
        var rowSpacing = RowSpacing * scale;
        var sectionGap = SectionGap * scale;
        var glitchStep = (int) (_timing.RealTime.TotalSeconds * 22.0);

        var y = 0f;
        var maxRight = 0f;
        var lineIdx = 0;

        // --- Секция 1: заголовок + время, бойцы отряда ---
        var sectionTop = y;
        {
            var title = measureTarget ? _headerScramble.TargetText : _headerScramble.CurrentText;
            var rowH = MathF.Max(_headerFont.GetHeight(scale), _timeFont.GetHeight(scale));
            var ascent = MathF.Max(_headerFont.GetAscent(scale), _timeFont.GetAscent(scale));
            var jitter = handle != null ? GetGlitchJitter(lineIdx, glitchStep, scale) : 0f;
            var titleW = MeasureText(_headerFont, title, scale);
            var timeX = x0 + titleW + (titleW > 0f ? HeaderTimeGap * scale : 0f);
            var timeW = MeasureText(_timeFont, _roundTimeText, scale);
            var right = timeW > 0f ? timeX + timeW : x0 + titleW;

            if (handle != null && right > x0)
            {
                DrawPlate(handle, x0 + jitter, right + jitter, y, rowH, scale);
                DrawLensText(handle, _headerFont, new Vector2(x0 + jitter, y + ascent - _headerFont.GetAscent(scale)), title, scale, HeaderBlue);
                DrawLensText(handle, _timeFont, new Vector2(timeX + jitter, y + ascent - _timeFont.GetAscent(scale)), _roundTimeText, scale, TimeBlue);
            }

            maxRight = MathF.Max(maxRight, right);
            y += rowH + rowSpacing;
            lineIdx++;
        }

        foreach (var row in _memberRows)
        {
            if (!row.Visible)
                continue;

            ProcessRow(handle, ref y, ref maxRight, ref lineIdx, _keyFighter, row, x0, valueX, scale, measureTarget, glitchStep);
        }

        DrawSectionBar(handle, sectionTop, y - rowSpacing, scale, BarCyan);
        y += sectionGap;

        // --- Секция 2: локация, статус, код тревоги ---
        sectionTop = y;
        ProcessRow(handle, ref y, ref maxRight, ref lineIdx, _keyLocation, _locationRow, x0, valueX, scale, measureTarget, glitchStep);
        ProcessRow(handle, ref y, ref maxRight, ref lineIdx, _keyStatus, _statusRow, x0, valueX, scale, measureTarget, glitchStep);
        ProcessRow(handle, ref y, ref maxRight, ref lineIdx, _keyCode, _codeRow, x0, valueX, scale, measureTarget, glitchStep);
        DrawSectionBar(handle, sectionTop, y - rowSpacing, scale, BarCyan);
        y += sectionGap;

        // --- Секция 3: трекеры (выше) + тактические метки ---
        sectionTop = y;
        var anyTracker = false;
        foreach (var row in _trackerRows)
        {
            if (!row.Visible)
                continue;

            anyTracker = true;
            ProcessRow(handle, ref y, ref maxRight, ref lineIdx, _keyTracker, row, x0, valueX, scale, measureTarget, glitchStep);
        }

        foreach (var row in _orderRows)
        {
            if (!row.Visible)
                continue;

            ProcessRow(handle, ref y, ref maxRight, ref lineIdx, _keyOrder, row, x0, valueX, scale, measureTarget, glitchStep);
        }

        if (_emptyAlertVisible)
        {
            var rowH = _smallFont.GetHeight(scale);
            var w = MeasureText(_smallFont, _emptyAlertText, scale);
            if (handle != null && w > 0f)
            {
                var jitter = GetGlitchJitter(lineIdx, glitchStep, scale);
                DrawPlate(handle, x0 + jitter, x0 + w + jitter, y, rowH, scale);
                DrawLensText(handle, _smallFont, new Vector2(x0 + jitter, y), _emptyAlertText, scale, MutedEmpty);
            }

            maxRight = MathF.Max(maxRight, x0 + w);
            y += rowH + rowSpacing;
            lineIdx++;
        }

        DrawSectionBar(handle, sectionTop, y - rowSpacing, scale, anyTracker ? BarCrimson : BarCyan);

        var width = maxRight + (PlatePadX + GlitchJitter) * scale;
        var height = y - rowSpacing + PlatePadY * scale;
        // Наклон линзы выносит правый край вниз — учитываем, чтобы кламп к экрану не обрезал низ.
        height += MathF.Abs(LensShearY) * width;
        width += MathF.Abs(LensShearX) * height;
        return new Vector2(width, height);
    }

    private void ProcessRow(
        DrawingHandleScreen? handle,
        ref float y,
        ref float maxRight,
        ref int lineIdx,
        string key,
        HudRow row,
        float x0,
        float valueX,
        float scale,
        bool measureTarget,
        int glitchStep)
    {
        var value = measureTarget ? row.Scramble.TargetText : row.Scramble.CurrentText;
        var rowH = MathF.Max(_keyFont.GetHeight(scale), _valueFont.GetHeight(scale));
        var ascent = MathF.Max(_keyFont.GetAscent(scale), _valueFont.GetAscent(scale));

        var valueW = MeasureText(_valueFont, value, scale);
        var right = valueW > 0f ? valueX + valueW : (_keysVisible ? valueX - ValueGap * scale : 0f);

        if (handle != null && right > 0f)
        {
            var jitter = GetGlitchJitter(lineIdx, glitchStep, scale);
            var left = (_keysVisible ? x0 : valueX) + jitter;
            DrawPlate(handle, left, right + jitter, y, rowH, scale);

            if (_keysVisible)
            {
                var keyY = y + ascent - _keyFont.GetAscent(scale);
                DrawLensText(handle, _keyFont, new Vector2(x0 + jitter, keyY), key, scale, KeyBlue);
                DrawLensText(handle, _keyFont,
                    new Vector2(x0 + _keyColumnPx + KeySeparatorGap * scale + jitter, keyY),
                    KeySeparator, scale, KeyBlue);
            }

            if (valueW > 0f)
            {
                DrawLensText(handle, _valueFont,
                    new Vector2(valueX + jitter, y + ascent - _valueFont.GetAscent(scale)),
                    value, scale, row.ValueColor);
            }
        }

        maxRight = MathF.Max(maxRight, right);
        y += rowH + RowSpacing * scale;
        lineIdx++;
    }

    private void EnsureKeyMetrics(float scale)
    {
        if (MathF.Abs(scale - _keyMetricsScale) < 0.001f)
            return;

        _keyMetricsScale = scale;
        _keyColumnPx = 0f;
        foreach (var key in _allKeys)
            _keyColumnPx = MathF.Max(_keyColumnPx, MeasureText(_keyFont, key, scale));

        _separatorPx = MeasureText(_keyFont, KeySeparator, scale);
    }

    /// <summary>Узкая тёмная плашка только под текстом строки — без общего прямоугольника HUD.</summary>
    private static void DrawPlate(DrawingHandleScreen handle, float left, float right, float y, float rowH, float scale)
    {
        var padX = PlatePadX * scale;
        var padY = PlatePadY * scale;
        handle.DrawRect(new UIBox2(left - padX, y - padY, right + padX, y + rowH + padY), PlateColor);
    }

    /// <summary>Тонкая вертикальная полоса визора слева от секции.</summary>
    private static void DrawSectionBar(DrawingHandleScreen? handle, float top, float bottom, float scale, Color color)
    {
        if (handle == null || bottom - top < 1f)
            return;

        var w = MathF.Max(2f, BarWidth * scale);
        handle.DrawRect(new UIBox2(0f, top - PlatePadY * scale, w, bottom + PlatePadY * scale), color);
    }

    /// <summary>
    /// «Проекция на линзу»: мягкое свечение (4 полупрозрачные копии), хроматическая аберрация
    /// (красная и голубая копии со сдвигом) и основной текст поверх.
    /// </summary>
    private static void DrawLensText(DrawingHandleScreen handle, Font font, Vector2 pos, string text, float scale, Color color)
    {
        if (text.Length == 0)
            return;

        var glow = GlowOffset * scale;
        var glowColor = color.WithAlpha(color.A * GlowAlpha);
        handle.DrawString(font, pos + new Vector2(-glow, 0f), text, scale, glowColor);
        handle.DrawString(font, pos + new Vector2(glow, 0f), text, scale, glowColor);
        handle.DrawString(font, pos + new Vector2(0f, -glow), text, scale, glowColor);
        handle.DrawString(font, pos + new Vector2(0f, glow), text, scale, glowColor);

        var ab = AberrationOffset * scale;
        handle.DrawString(font, pos + new Vector2(-ab, 0f), text, scale, AberrationRed);
        handle.DrawString(font, pos + new Vector2(ab, 0f), text, scale, AberrationCyan);

        handle.DrawString(font, pos, text, scale, color);
    }

    /// <summary>Детерминированный рывок строки при глитче — без Random и аллокаций в Draw.</summary>
    private float GetGlitchJitter(int lineIdx, int step, float scale)
    {
        if (_glitchIntensity <= 0.01f)
            return 0f;

        var roll = Hash(lineIdx, step);
        if (roll < 1f - _glitchIntensity * 0.6f)
            return 0f;

        return (Hash(lineIdx * 7 + 3, step) - 0.5f) * 2f * GlitchJitter * scale * _glitchIntensity;
    }

    private static float Hash(int a, int b)
    {
        unchecked
        {
            var h = (uint) (a * 73856093) ^ (uint) (b * 19349663);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }
    }

    private static float MeasureText(Font font, string text, float scale)
    {
        var w = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            var metrics = font.GetCharMetrics(rune, scale);
            if (metrics != null)
                w += metrics.Value.Advance;
        }

        return w;
    }

    #endregion

    private string GetOrderTypeLabel(DutyNeuroOrderType type)
    {
        var idx = (int) type;
        return idx >= 0 && idx < _orderTexts.Length ? _orderTexts[idx] : _orderUnknownText;
    }

    private string GetStatusText(DutyNeuroMemberStatus status)
    {
        var idx = (int) status;
        return idx >= 0 && idx < _statusTexts.Length ? _statusTexts[idx] : _statusTexts[0];
    }

    private static string Truncate(string? value, int maxLen)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= maxLen ? value : $"{value[..(maxLen - 1)]}…";
    }
}
