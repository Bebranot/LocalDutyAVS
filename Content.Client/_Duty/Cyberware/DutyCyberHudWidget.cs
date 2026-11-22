// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Client._Duty.Neurointerface;
using Content.Client.Resources;
using Content.Shared._Duty.Cyberware;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Cyberware;

/// <summary>
/// _Duty: панель киберсистемы — проекция на линзу в том же языке, что HUD NT-SEC
/// (свечение, аберрация, сканлайны DutyNeuroLens, расшифровка строк), но в цветах фракции железа.
/// Под маской NT-SEC пристыковывается снизу как ещё один блок визора: без своего заголовка, в палитре NT.
/// ЭМИ — постоянный глитч и красная строка сбоя, перегруз — редкие рывки и красная шкала нагрузки.
/// </summary>
public sealed partial class DutyCyberHudWidget : UIWidget
{
    private const float FadeInDuration = 0.45f;

    private const int HeaderFontSize = 13;
    private const int KeyFontSize = 12;
    private const int ValueFontSize = 12;
    private const int ModuleNameMaxLen = 28;

    private const float BarWidth = 2f;
    private const float TextIndent = 9f;
    private const float KeySeparatorGap = 5f;
    private const float ValueGap = 7f;
    private const float TailGap = 6f;
    private const float RowSpacing = 3f;
    private const float PlatePadX = 5f;
    private const float PlatePadY = 1f;

    // Шкала нагрузки: сегменты рисуются прямоугольниками — глифов-блоков в моноширинном шрифте может не быть.
    private const float SegmentW = 7f;
    private const float SegmentH = 9f;
    private const float SegmentGap = 2f;

    private const float AberrationOffset = 1f;
    private const float AberrationAlpha = 0.32f;
    private const float GlowOffset = 1.5f;
    private const float GlowAlpha = 0.14f;
    private const float ScanlineStrength = 0.22f;
    private const float LensShearY = 0.018f;
    private const float GlitchJitter = 4f;

    private const string KeySeparator = "::";
    private const string TailSeparator = "·";

    private static readonly ProtoId<ShaderPrototype> LensShaderId = "DutyNeuroLens";
    private static readonly Color PlateColor = new(0.01f, 0.02f, 0.03f, 0.6f);
    private static readonly Color AlarmRed = Color.FromHex("#FF334B");
    private static readonly Color AlarmAmber = Color.FromHex("#FFB547");

    /// <summary>
    /// Палитра железа. У NT — «сертифицированный» холодный синий, у Горлекса — ржавый красный,
    /// у Синдиката — обезличенный серый с алым, у Cybersun — бело-золотой люкс, у наёмников — зелёный ЭЛТ.
    /// </summary>
    private readonly record struct Palette(Color Header, Color Key, Color Value, Color Muted, Color Bar, Color AberrA, Color AberrB);

    private static readonly Palette[] Palettes =
    {
        // NanoTrasen
        new(Color.FromHex("#4F9BFF"), Color.FromHex("#3D8BFF"), Color.FromHex("#E4F3FF"), Color.FromHex("#7F93B3"),
            Color.FromHex("#2CE6FF").WithAlpha(0.8f), Color.FromHex("#FF3B5C"), Color.FromHex("#3BE8FF")),
        // Gorlex
        new(Color.FromHex("#FF5A3C"), Color.FromHex("#C8553D"), Color.FromHex("#FFE1D6"), Color.FromHex("#9A6A5C"),
            Color.FromHex("#D9302B").WithAlpha(0.85f), Color.FromHex("#FFB000"), Color.FromHex("#7A0F0F")),
        // Syndicate
        new(Color.FromHex("#FF2E4D"), Color.FromHex("#8A8F98"), Color.FromHex("#E8E8EA"), Color.FromHex("#5E636B"),
            Color.FromHex("#FF2E4D").WithAlpha(0.75f), Color.FromHex("#FF2E4D"), Color.FromHex("#B8C0CC")),
        // Cybersun
        new(Color.FromHex("#F5D27A"), Color.FromHex("#C9A44C"), Color.FromHex("#FFF8EA"), Color.FromHex("#9C8B66"),
            Color.FromHex("#F2C14E").WithAlpha(0.85f), Color.FromHex("#FFFFFF"), Color.FromHex("#E0A22B")),
        // Mercenary
        new(Color.FromHex("#7CFF9B"), Color.FromHex("#4E9A63"), Color.FromHex("#CCFFD9"), Color.FromHex("#4C6B55"),
            Color.FromHex("#3DDC84").WithAlpha(0.8f), Color.FromHex("#3DDC84"), Color.FromHex("#0F5A2E")),
    };

    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly ShaderInstance _lensShader;
    private readonly Font _headerFont;
    private readonly Font _keyFont;
    private readonly Font _valueFont;
    private readonly Font _tailFont;

    private readonly string _keyModule;
    private readonly string _keyLoad;
    private readonly string _keyEmp;
    private readonly string _overloadText;
    private readonly string[] _headerTexts;
    private readonly string[] _stateTexts;
    private readonly string[] _allKeys;

    private readonly DutyNeuroScrambleText _headerScramble = new(string.Empty, true);
    private readonly DutyNeuroScrambleText _empScramble = new(string.Empty, true);
    private readonly List<ModuleRow> _rows = new();

    private Palette _palette = Palettes[0];
    private DutyCyberFaction _shownFaction = (DutyCyberFaction) 255;
    private bool _docked;
    private bool _wasActive;
    private float _fadeAlpha;
    private float _glitchIntensity;
    private float _burstTimer;

    private int _load;
    private int _loadLimit;
    private bool _overloaded;
    private bool _emped;
    private string _empTail = string.Empty;

    private float _keyMetricsScale = -1f;
    private float _keyColumnPx;
    private float _separatorPx;
    private Vector2 _contentSize;

    /// <summary>Строка модуля: имя идёт через расшифровку, хвост (статус, кулдаун) рисуется как есть — таймер не перешифровывается каждую секунду.</summary>
    private sealed class ModuleRow
    {
        public readonly DutyNeuroScrambleText Name = new(string.Empty, true);
        public string Key = string.Empty;
        public string Tail = string.Empty;
        public Color TailColor;
        public bool Dim;
        public bool Seen;
    }

    public DutyCyberHudWidget()
    {
        IoCManager.InjectDependencies(this);

        MouseFilter = MouseFilterMode.Ignore;
        Visible = false;

        _keyModule = Loc.GetString("duty-cyber-hud-module");
        _keyLoad = Loc.GetString("duty-cyber-hud-load");
        _keyEmp = Loc.GetString("duty-cyber-hud-emp");
        _overloadText = Loc.GetString("duty-cyber-hud-overload");
        _allKeys = new[] { _keyModule, _keyLoad, _keyEmp };

        // Порядок строго по DutyCyberFaction.
        _headerTexts = new[]
        {
            Loc.GetString("duty-cyber-hud-header-nanotrasen"),
            Loc.GetString("duty-cyber-hud-header-gorlex"),
            Loc.GetString("duty-cyber-hud-header-syndicate"),
            Loc.GetString("duty-cyber-hud-header-cybersun"),
            Loc.GetString("duty-cyber-hud-header-mercenary"),
        };

        // Порядок строго по DutyCyberModuleState.
        _stateTexts = new[]
        {
            Loc.GetString("duty-cyber-hud-state-online"),
            Loc.GetString("duty-cyber-hud-state-active"),
            Loc.GetString("duty-cyber-hud-state-cooldown"),
            Loc.GetString("duty-cyber-hud-state-offline"),
            Loc.GetString("duty-cyber-hud-state-burned"),
        };

        _lensShader = _proto.Index(LensShaderId).InstanceUnique();

        _headerFont = _resCache.GetFont(new[] { "/Fonts/NotoSans/NotoSans-BoldItalic.ttf", "/Fonts/NotoSans/NotoSans-Bold.ttf" }, HeaderFontSize);
        _keyFont = _resCache.GetFont(new[] { "/Fonts/RobotoMono/RobotoMono-Bold.ttf", "/Fonts/NotoSans/NotoSans-Bold.ttf" }, KeyFontSize);
        _valueFont = _resCache.GetFont(new[] { "/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf", "/Fonts/NotoSans/NotoSans-Bold.ttf" }, ValueFontSize);
        _tailFont = _resCache.GetFont(new[] { "/Fonts/RobotoMono/RobotoMono-Regular.ttf", "/Fonts/NotoSans/NotoSans-Regular.ttf" }, ValueFontSize);
    }

    /// <summary>
    /// <paramref name="host"/> = null — панель гаснет. <paramref name="docked"/> — под маской NT-SEC (без заголовка, палитра сети визора).
    /// </summary>
    public void UpdateState(DutyCyberHostComponent? host, bool docked, TimeSpan curTime, float frameTime, DutyCyberFaction dockedFaction = DutyCyberFaction.NanoTrasen)
    {
        if (host == null || host.Modules.Count == 0)
        {
            Reset();
            return;
        }

        var faction = docked ? dockedFaction : host.Skin;
        var bootNow = !_wasActive || docked != _docked || faction != _shownFaction;

        if (bootNow)
        {
            // Загрузка ядра: заголовок и все строки расшифровываются заново, сильный глитч на первые доли секунды.
            _wasActive = true;
            _docked = docked;
            _shownFaction = faction;
            _palette = Palettes[Math.Clamp((int) faction, 0, Palettes.Length - 1)];
            _headerScramble.SetText(docked ? string.Empty : _headerTexts[Math.Clamp((int) faction, 0, _headerTexts.Length - 1)], forceScramble: true);
            foreach (var row in _rows)
                row.Name.SetText(row.Name.TargetText, forceScramble: true);
            _glitchIntensity = MathF.Max(_glitchIntensity, 0.5f);
        }

        Visible = true;
        _fadeAlpha = MathF.Min(1f, _fadeAlpha + frameTime / FadeInDuration);

        _load = host.Load;
        _loadLimit = Math.Max(1, host.LoadLimit);
        _overloaded = host.Load > host.LoadLimit;

        var empUntil = host.EmpUntil;
        var emped = empUntil is { } until && until > curTime;
        if (emped != _emped)
        {
            _emped = emped;
            _empScramble.SetText(emped ? "--:--" : string.Empty, forceScramble: true);
            if (emped)
            {
                _glitchIntensity = 1f;
                foreach (var row in _rows)
                    row.Name.TriggerGlitchBurst(1.2f);
                _headerScramble.TriggerGlitchBurst(1.5f);
            }
        }

        _empTail = emped ? FormatTimer(empUntil!.Value - curTime) : string.Empty;

        UpdateRows(host, emped, curTime, frameTime);
        UpdateGlitch(frameTime);

        _headerScramble.Update(frameTime);
        _empScramble.Update(frameTime);

        Modulate = Color.White.WithAlpha(_fadeAlpha);
        UpdateContentSize();
    }

    private void UpdateRows(DutyCyberHostComponent host, bool emped, TimeSpan curTime, float frameTime)
    {
        foreach (var row in _rows)
            row.Seen = false;

        // Модули идут в порядке вживления: строки не прыгают, новая появляется снизу расшифровкой.
        for (var i = 0; i < host.Modules.Count; i++)
        {
            var entry = host.Modules[i];
            var row = FindRow(entry.NameLoc);
            if (row == null)
            {
                row = new ModuleRow { Key = entry.NameLoc };
                _rows.Add(row);
            }

            row.Seen = true;

            var name = Truncate(Loc.GetString(entry.NameLoc), ModuleNameMaxLen);
            if (entry.Count > 1)
                name = $"{name} ×{entry.Count}";
            row.Name.SetText(name);

            BuildTail(row, entry, emped, curTime);
        }

        for (var i = _rows.Count - 1; i >= 0; i--)
        {
            var row = _rows[i];
            if (!row.Seen && !row.Name.IsDissolving && !row.Name.IsDissolved)
                row.Name.StartDissolve();

            row.Name.Update(frameTime);

            if (!row.Seen && row.Name.IsDissolved)
                _rows.RemoveAt(i);
        }
    }

    private void BuildTail(ModuleRow row, DutyCyberModuleEntry entry, bool emped, TimeSpan curTime)
    {
        row.Dim = false;

        if (emped && entry.EmpSensitive)
        {
            row.Tail = _keyEmp;
            row.TailColor = AlarmRed;
            row.Dim = true;
            return;
        }

        switch (entry.State)
        {
            case DutyCyberModuleState.Active:
                row.Tail = string.IsNullOrEmpty(entry.Detail) ? _stateTexts[(int) entry.State] : entry.Detail;
                row.TailColor = AlarmAmber;
                break;
            case DutyCyberModuleState.Cooldown:
                row.Tail = entry.CooldownEnd is { } end && end > curTime
                    ? FormatTimer(end - curTime)
                    : _stateTexts[(int) entry.State];
                row.TailColor = _palette.Muted;
                break;
            case DutyCyberModuleState.Offline:
                row.Tail = _stateTexts[(int) entry.State];
                row.TailColor = _palette.Muted;
                row.Dim = true;
                break;
            case DutyCyberModuleState.Burned:
                row.Tail = _stateTexts[(int) entry.State];
                row.TailColor = AlarmRed.WithAlpha(0.7f);
                row.Dim = true;
                break;
            default:
                row.Tail = string.IsNullOrEmpty(entry.Detail) ? _stateTexts[0] : entry.Detail;
                row.TailColor = _palette.Header;
                break;
        }
    }

    private ModuleRow? FindRow(string key)
    {
        foreach (var row in _rows)
        {
            if (row.Key == key && !row.Name.IsDissolving)
                return row;
        }

        return null;
    }

    /// <summary>
    /// ЭМИ держит глитч высоким и всё время рвёт строки, перегруз даёт короткие рывки раз в несколько секунд —
    /// «нервы не справляются», но читать можно.
    /// </summary>
    private void UpdateGlitch(float frameTime)
    {
        _glitchIntensity = MathF.Max(0f, _glitchIntensity - frameTime * 0.8f);
        _burstTimer -= frameTime;

        if (_emped)
        {
            _glitchIntensity = MathF.Max(_glitchIntensity, 0.55f);
            if (_burstTimer <= 0f)
            {
                _burstTimer = 0.6f + Hash(_rows.Count, (int) (_timing.RealTime.TotalSeconds * 3)) * 0.8f;
                _headerScramble.TriggerGlitchBurst(0.5f);
                foreach (var row in _rows)
                    row.Name.TriggerGlitchBurst(0.4f);
            }

            return;
        }

        if (_overloaded && _burstTimer <= 0f)
        {
            var over = Math.Max(1, _load - _loadLimit);
            _burstTimer = MathF.Max(1.5f, 6f - over) + Hash(over, (int) _timing.RealTime.TotalSeconds) * 2f;
            _glitchIntensity = MathF.Max(_glitchIntensity, 0.25f + 0.08f * over);
            if (_rows.Count > 0)
                _rows[(int) (Hash(_rows.Count, (int) (_timing.RealTime.TotalSeconds * 7)) * _rows.Count) % _rows.Count].Name.TriggerGlitchBurst(0.35f);
        }
    }

    private void Reset()
    {
        if (!_wasActive && !Visible)
            return;

        _wasActive = false;
        _shownFaction = (DutyCyberFaction) 255;
        _fadeAlpha = 0f;
        _emped = false;
        _rows.Clear();
        _headerScramble.Clear();
        _empScramble.Clear();
        Visible = false;
    }

    #region Отрисовка

    private void UpdateContentSize()
    {
        var scale = UIScale;
        if (scale <= 0f)
            scale = 1f;

        var size = RunLayout(null, scale, true) / scale;
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

        var time = (float) _timing.RealTime.TotalSeconds;
        // Под ЭМИ проекция «плавает» заметно сильнее обычного мерцания.
        var flicker = _emped
            ? 0.7f + 0.3f * MathF.Abs(MathF.Sin(time * 29f) * MathF.Cos(time * 7.3f))
            : 0.95f + 0.05f * MathF.Sin(time * 13f) * MathF.Cos(time * 4.7f);
        _lensShader.SetParameter("alpha", flicker);
        _lensShader.SetParameter("glitch_intensity", _glitchIntensity);
        _lensShader.SetParameter("scan_strength", ScanlineStrength);

        var baseTransform = handle.GetTransform();
        handle.SetTransform(new Matrix3x2(1f, LensShearY, 0f, 1f, 0f, 0f) * baseTransform);
        handle.UseShader(_lensShader);
        RunLayout(handle, UIScale, false);
        handle.UseShader(null);
        handle.SetTransform(baseTransform);
    }

    private Vector2 RunLayout(DrawingHandleScreen? handle, float scale, bool measureTarget)
    {
        EnsureKeyMetrics(scale);

        var x0 = (BarWidth + TextIndent) * scale;
        var valueX = x0 + _keyColumnPx + KeySeparatorGap * scale + _separatorPx + ValueGap * scale;
        var rowSpacing = RowSpacing * scale;
        var glitchStep = (int) (_timing.RealTime.TotalSeconds * 22.0);
        var rowH = MathF.Max(_keyFont.GetHeight(scale), _valueFont.GetHeight(scale));
        var ascent = MathF.Max(_keyFont.GetAscent(scale), _valueFont.GetAscent(scale));

        var y = 0f;
        var maxRight = 0f;
        var lineIdx = 0;
        var sectionTop = 0f;

        // --- Заголовок фракции (только без маски NT-SEC) ---
        if (!_docked)
        {
            var title = measureTarget ? _headerScramble.TargetText : _headerScramble.CurrentText;
            var headerH = _headerFont.GetHeight(scale);
            var titleW = MeasureText(_headerFont, title, scale);
            if (handle != null && titleW > 0f)
            {
                var jitter = GetGlitchJitter(lineIdx, glitchStep, scale);
                DrawPlate(handle, x0 + jitter, x0 + titleW + jitter, y, headerH, scale);
                DrawLensText(handle, _headerFont, new Vector2(x0 + jitter, y), title, scale, _emped ? AlarmRed : _palette.Header);
            }

            maxRight = MathF.Max(maxRight, x0 + titleW);
            y += headerH + rowSpacing;
            lineIdx++;
        }

        // --- Нагрузка: сегментная шкала, перелимит — красные сегменты и мигающее «ПЕРЕГРУЗ» ---
        {
            var right = DrawKey(handle, _keyLoad, x0, y, ascent, scale, lineIdx, glitchStep, valueX);
            var seg = SegmentW * scale;
            var gap = SegmentGap * scale;
            var segH = SegmentH * scale;
            var total = Math.Max(_load, _loadLimit);
            var segY = y + (rowH - segH) * 0.5f;
            var jitter = handle != null ? GetGlitchJitter(lineIdx, glitchStep, scale) : 0f;
            var meterRight = valueX + total * (seg + gap) - gap;

            var tail = $"{_load}/{_loadLimit}";
            var tailX = meterRight + TailGap * scale;
            var tailW = MeasureText(_tailFont, tail, scale);
            var overW = _overloaded ? TailGap * scale + MeasureText(_keyFont, _overloadText, scale) : 0f;
            right = MathF.Max(right, tailX + tailW + overW);

            if (handle != null)
            {
                DrawPlate(handle, x0 + jitter, right + jitter, y, rowH, scale);
                DrawKey(handle, _keyLoad, x0 + jitter, y, ascent, scale, lineIdx, glitchStep, valueX);

                var blink = 0.55f + 0.45f * MathF.Abs(MathF.Sin((float) _timing.RealTime.TotalSeconds * 6f));
                for (var i = 0; i < total; i++)
                {
                    var sx = valueX + i * (seg + gap) + jitter;
                    Color color;
                    if (i >= _loadLimit)
                        color = AlarmRed.WithAlpha(blink);
                    else if (i < _load)
                        color = _overloaded ? AlarmAmber : _palette.Header;
                    else
                        color = _palette.Muted.WithAlpha(0.35f);

                    handle.DrawRect(new UIBox2(sx, segY, sx + seg, segY + segH), color);
                }

                DrawLensText(handle, _tailFont, new Vector2(tailX + jitter, y + ascent - _tailFont.GetAscent(scale)), tail,
                    scale, _overloaded ? AlarmRed : _palette.Value);

                if (_overloaded)
                {
                    DrawLensText(handle, _keyFont,
                        new Vector2(tailX + tailW + TailGap * scale + jitter, y + ascent - _keyFont.GetAscent(scale)),
                        _overloadText, scale, AlarmRed.WithAlpha(blink));
                }
            }

            maxRight = MathF.Max(maxRight, right);
            y += rowH + rowSpacing;
            lineIdx++;
        }

        // --- ЭМИ-сбой: красная строка с обратным отсчётом ---
        if (_emped)
        {
            var text = measureTarget ? _empScramble.TargetText : _empScramble.CurrentText;
            var shown = _empScramble.IsAnimating ? text : _empTail;
            var right = valueX + MeasureText(_tailFont, shown, scale);
            if (handle != null)
            {
                var jitter = GetGlitchJitter(lineIdx, glitchStep, scale);
                DrawPlate(handle, x0 + jitter, right + jitter, y, rowH, scale);
                DrawKey(handle, _keyEmp, x0 + jitter, y, ascent, scale, lineIdx, glitchStep, valueX, AlarmRed);
                DrawLensText(handle, _tailFont, new Vector2(valueX + jitter, y + ascent - _tailFont.GetAscent(scale)), shown, scale, AlarmRed);
            }

            maxRight = MathF.Max(maxRight, right);
            y += rowH + rowSpacing;
            lineIdx++;
        }

        // --- Модули ---
        foreach (var row in _rows)
        {
            var name = measureTarget ? row.Name.TargetText : row.Name.CurrentText;
            if (name.Length == 0 && !measureTarget)
                continue;

            var nameW = MeasureText(_valueFont, name, scale);
            var showTail = row.Seen && !row.Name.IsAnimating && row.Tail.Length > 0;
            var sepW = MeasureText(_tailFont, TailSeparator, scale);
            var tailX = valueX + nameW + TailGap * scale + sepW + TailGap * scale;
            var tailW = row.Tail.Length > 0 ? MeasureText(_tailFont, row.Tail, scale) : 0f;
            var right = row.Tail.Length > 0 ? tailX + tailW : valueX + nameW;

            if (handle != null)
            {
                var jitter = GetGlitchJitter(lineIdx, glitchStep, scale);
                DrawPlate(handle, x0 + jitter, (showTail ? right : valueX + nameW) + jitter, y, rowH, scale);
                DrawKey(handle, _keyModule, x0 + jitter, y, ascent, scale, lineIdx, glitchStep, valueX);

                var nameColor = row.Dim ? _palette.Muted : _palette.Value;
                DrawLensText(handle, _valueFont, new Vector2(valueX + jitter, y + ascent - _valueFont.GetAscent(scale)), name, scale, nameColor);

                if (showTail)
                {
                    var tailY = y + ascent - _tailFont.GetAscent(scale);
                    DrawLensText(handle, _tailFont, new Vector2(valueX + nameW + TailGap * scale + jitter, tailY), TailSeparator, scale, _palette.Key);
                    DrawLensText(handle, _tailFont, new Vector2(tailX + jitter, tailY), row.Tail, scale, row.TailColor);
                }
            }

            maxRight = MathF.Max(maxRight, right);
            y += rowH + rowSpacing;
            lineIdx++;
        }

        var barColor = _emped || _overloaded ? AlarmRed.WithAlpha(0.8f) : _palette.Bar;
        DrawSectionBar(handle, sectionTop, y - rowSpacing, scale, barColor);

        var width = maxRight + (PlatePadX + GlitchJitter) * scale;
        var height = y - rowSpacing + PlatePadY * scale;
        height += MathF.Abs(LensShearY) * width;
        return new Vector2(width, height);
    }

    /// <summary>Рисует «КЛЮЧ ::» (если есть handle) и возвращает правую границу колонки ключей.</summary>
    private float DrawKey(DrawingHandleScreen? handle, string key, float x, float y, float ascent, float scale, int lineIdx, int glitchStep, float valueX, Color? color = null)
    {
        if (handle != null)
        {
            var keyY = y + ascent - _keyFont.GetAscent(scale);
            var c = color ?? _palette.Key;
            DrawLensText(handle, _keyFont, new Vector2(x, keyY), key, scale, c);
            DrawLensText(handle, _keyFont, new Vector2(x + _keyColumnPx + KeySeparatorGap * scale, keyY), KeySeparator, scale, c);
        }

        return valueX - ValueGap * scale;
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

    private static void DrawPlate(DrawingHandleScreen handle, float left, float right, float y, float rowH, float scale)
    {
        var padX = PlatePadX * scale;
        var padY = PlatePadY * scale;
        handle.DrawRect(new UIBox2(left - padX, y - padY, right + padX, y + rowH + padY), PlateColor);
    }

    private static void DrawSectionBar(DrawingHandleScreen? handle, float top, float bottom, float scale, Color color)
    {
        if (handle == null || bottom - top < 1f)
            return;

        var w = MathF.Max(2f, BarWidth * scale);
        handle.DrawRect(new UIBox2(0f, top - PlatePadY * scale, w, bottom + PlatePadY * scale), color);
    }

    private void DrawLensText(DrawingHandleScreen handle, Font font, Vector2 pos, string text, float scale, Color color)
    {
        if (text.Length == 0)
            return;

        var glow = GlowOffset * scale;
        var glowColor = color.WithAlpha(color.A * GlowAlpha);
        handle.DrawString(font, pos + new Vector2(-glow, 0f), text, scale, glowColor);
        handle.DrawString(font, pos + new Vector2(glow, 0f), text, scale, glowColor);
        handle.DrawString(font, pos + new Vector2(0f, -glow), text, scale, glowColor);
        handle.DrawString(font, pos + new Vector2(0f, glow), text, scale, glowColor);

        // Под ЭМИ аберрация разъезжается сильнее: «картинка двоится».
        var ab = AberrationOffset * scale * (_emped ? 2.5f : 1f);
        handle.DrawString(font, pos + new Vector2(-ab, 0f), text, scale, _palette.AberrA.WithAlpha(AberrationAlpha));
        handle.DrawString(font, pos + new Vector2(ab, 0f), text, scale, _palette.AberrB.WithAlpha(AberrationAlpha));

        handle.DrawString(font, pos, text, scale, color);
    }

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

    private static string FormatTimer(TimeSpan span)
    {
        var total = Math.Max(0, (int) Math.Ceiling(span.TotalSeconds));
        return $"{total / 60:00}:{total % 60:00}";
    }

    private static string Truncate(string value, int maxLen)
    {
        return value.Length <= maxLen ? value : $"{value[..(maxLen - 1)]}…";
    }
}
