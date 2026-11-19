// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;

namespace Content.Client._Duty.Neurointerface;

/// <summary>
/// _Duty: генератор киберпанк-анимации расшифровки текста (scramble decode) для HUD и планшета.
/// Пул подстановки содержит ТОЛЬКО русские и латинские буквы, без цифр и спецсимволов.
/// </summary>
public sealed class DutyNeuroScrambleText
{
    private const string ScrambleAlphabet =
        "АБВГДЕЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдежзийклмнопрстуфхцчшщъыьэюяABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private const float StepInterval = 0.032f;
    private const float GlitchStepBackChance = 0.12f;

    private readonly Random _random = new();
    private readonly StringBuilder _builder = new();

    private float _stepAccumulator;
    private int _revealedCount;
    private int _stepBacksRemaining;
    private float _glitchBurstRemaining;
    private bool _decodeActive;

    // Обратная расшифровка (исчезновение): сначала справа налево буквы срываются в шум, затем шум «сматывается».
    private bool _dissolveActive;
    private int _dissolveLength;

    public string TargetText { get; private set; } = string.Empty;
    public string CurrentText { get; private set; } = string.Empty;
    public bool IsAnimating => _decodeActive || _dissolveActive || _glitchBurstRemaining > 0f;

    /// <summary>Идёт анимация исчезновения строки.</summary>
    public bool IsDissolving => _dissolveActive;

    /// <summary>Исчезновение доиграло: строку можно убирать.</summary>
    public bool IsDissolved { get; private set; }

    public DutyNeuroScrambleText(string initialText = "", bool scrambleOnInit = true)
    {
        if (string.IsNullOrEmpty(initialText))
            return;

        TargetText = initialText;
        if (scrambleOnInit)
        {
            StartDecode();
        }
        else
        {
            _revealedCount = TargetText.Length;
            CurrentText = TargetText;
        }
    }

    public void SetText(string newText, bool forceScramble = false)
    {
        newText ??= string.Empty;

        if (!forceScramble && string.Equals(TargetText, newText, StringComparison.Ordinal))
            return;

        TargetText = newText;
        _dissolveActive = false;
        IsDissolved = false;

        if (string.IsNullOrEmpty(TargetText))
        {
            _decodeActive = false;
            _revealedCount = 0;
            CurrentText = string.Empty;
            return;
        }

        StartDecode();
    }

    /// <summary>
    /// Запускает исчезновение — та же анимация, что и при появлении, но в обратную сторону.
    /// </summary>
    public void StartDissolve()
    {
        if (_dissolveActive || IsDissolved)
            return;

        if (string.IsNullOrEmpty(TargetText))
        {
            IsDissolved = true;
            return;
        }

        // Если строка ещё не дорасшифровалась, исчезает с того места, где остановилась.
        _revealedCount = _decodeActive ? _revealedCount : TargetText.Length;
        _decodeActive = false;
        _glitchBurstRemaining = 0f;
        _dissolveActive = true;
        _dissolveLength = TargetText.Length;
        _stepAccumulator = 0f;
        RebuildFrame();
    }

    public void TriggerGlitchBurst(float duration = 0.65f)
    {
        if (duration <= 0f || _dissolveActive)
            return;

        _glitchBurstRemaining = MathF.Max(_glitchBurstRemaining, duration);
        _stepAccumulator = StepInterval; // мгновенно обновить кадр глитча на следующем Update
        RebuildFrame();
    }

    public void Clear()
    {
        TargetText = string.Empty;
        CurrentText = string.Empty;
        _revealedCount = 0;
        _stepBacksRemaining = 0;
        _glitchBurstRemaining = 0f;
        _stepAccumulator = 0f;
        _decodeActive = false;
        _dissolveActive = false;
        _dissolveLength = 0;
        IsDissolved = false;
    }

    public void Update(float frameTime)
    {
        if (frameTime <= 0f || !IsAnimating)
            return;

        if (_glitchBurstRemaining > 0f)
        {
            _glitchBurstRemaining = MathF.Max(0f, _glitchBurstRemaining - frameTime);
            if (_glitchBurstRemaining <= 0f && !_decodeActive)
            {
                CurrentText = TargetText;
                return;
            }
        }

        _stepAccumulator += frameTime;
        if (_stepAccumulator < StepInterval)
            return;

        while (_stepAccumulator >= StepInterval)
        {
            _stepAccumulator -= StepInterval;

            if (_dissolveActive)
                AdvanceDissolve();
            else if (_decodeActive && _glitchBurstRemaining <= 0f)
                AdvanceDecode();
        }

        RebuildFrame();
    }

    private void StartDecode()
    {
        _decodeActive = true;
        _revealedCount = 0;
        _stepAccumulator = 0f;
        _stepBacksRemaining = Math.Clamp(TargetText.Length / 6, 1, 3);
        RebuildFrame();
    }

    private void AdvanceDecode()
    {
        if (string.IsNullOrEmpty(TargetText))
        {
            _decodeActive = false;
            CurrentText = string.Empty;
            return;
        }

        // Иногда делаем короткий откат на 1 символ для эффекта живого канала связи.
        if (_stepBacksRemaining > 0 && _revealedCount > 2 && _revealedCount < TargetText.Length - 1)
        {
            if (_random.NextSingle() < GlitchStepBackChance)
            {
                _revealedCount--;
                _stepBacksRemaining--;
                return;
            }
        }

        // Длинные строки расшифровываются чуть быстрее, чтобы не затягивать чтение.
        var step = TargetText.Length > 24 ? 2 : 1;
        _revealedCount = Math.Min(TargetText.Length, _revealedCount + step);

        if (_revealedCount >= TargetText.Length)
        {
            _decodeActive = false;
            CurrentText = TargetText;
        }
    }

    private void AdvanceDissolve()
    {
        // Темп как у появления: длинные строки уходят чуть быстрее.
        var step = TargetText.Length > 24 ? 2 : 1;
        if (_revealedCount > 0)
        {
            _revealedCount = Math.Max(0, _revealedCount - step);
            return;
        }

        // Шум «сматывается» вдвое быстрее, чтобы хвост анимации не затягивался.
        _dissolveLength = Math.Max(0, _dissolveLength - step * 2);
        if (_dissolveLength > 0)
            return;

        _dissolveActive = false;
        IsDissolved = true;
        CurrentText = string.Empty;
    }

    private void RebuildFrame()
    {
        if (string.IsNullOrEmpty(TargetText))
        {
            CurrentText = string.Empty;
            return;
        }

        if (IsDissolved)
        {
            CurrentText = string.Empty;
            return;
        }

        if (_dissolveActive)
        {
            _builder.Clear();
            var length = Math.Min(_dissolveLength, TargetText.Length);
            for (var i = 0; i < length; i++)
            {
                var c = TargetText[i];
                if (i < _revealedCount || char.IsWhiteSpace(c) || IsPreservedSeparator(c))
                    _builder.Append(c);
                else
                    _builder.Append(RandomLetter());
            }

            CurrentText = _builder.ToString();
            return;
        }

        if (!_decodeActive && _glitchBurstRemaining <= 0f)
        {
            CurrentText = TargetText;
            return;
        }

        _builder.Clear();
        _builder.EnsureCapacity(TargetText.Length);

        var burstActive = _glitchBurstRemaining > 0f;
        for (var i = 0; i < TargetText.Length; i++)
        {
            var ch = TargetText[i];

            if (char.IsWhiteSpace(ch))
            {
                _builder.Append(ch);
                continue;
            }

            if (burstActive)
            {
                // При глитч-всплеске большинство букв срывается в случайный буквенный шум.
                var corruptChance = _decodeActive ? 0.8f : 0.72f;
                _builder.Append(_random.NextSingle() < corruptChance ? RandomLetter() : ch);
                continue;
            }

            if (i < _revealedCount)
            {
                _builder.Append(ch);
                continue;
            }

            // Пробелы и разделители сохраняются, буквы и символы подменяются буквами пула.
            _builder.Append(IsPreservedSeparator(ch) ? ch : RandomLetter());
        }

        CurrentText = _builder.ToString();
    }

    private static bool IsPreservedSeparator(char ch)
    {
        return ch is ':' or '/' or '-' or '.' or ',' or '·';
    }

    private char RandomLetter()
    {
        return ScrambleAlphabet[_random.Next(ScrambleAlphabet.Length)];
    }
}
