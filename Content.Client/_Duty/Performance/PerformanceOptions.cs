// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client.Options.UI;
using Content.Shared.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Client._Duty.Performance;

/// <summary>
/// _Duty: чекбокс «Режим для слабых устройств». Сам пресет применяет
/// <see cref="LowEndModeUIController"/> по смене CVar-а; здесь — только UI.
/// </summary>
public sealed class OptionLowEndMode : BaseOptionCVar<bool>
{
    private readonly OptionsTabControlRow _controller;
    private readonly IConfigurationManager _cfg;
    private readonly CheckBox _checkBox;
    private readonly Control _warning;

    protected override bool Value
    {
        get => _checkBox.Pressed;
        set => _checkBox.Pressed = value;
    }

    public OptionLowEndMode(
        OptionsTabControlRow controller,
        IConfigurationManager cfg,
        CheckBox checkBox,
        Control warning)
        : base(controller, cfg, DutyCCVars.LowEndMode)
    {
        _controller = controller;
        _cfg = cfg;
        _checkBox = checkBox;
        _warning = warning;
        _checkBox.OnToggled += _ =>
        {
            ValueChanged();
            UpdateWarning();
        };
    }

    public override void LoadValue()
    {
        base.LoadValue();
        UpdateWarning();
    }

    public override void SaveValue()
    {
        base.SaveValue();

        // Пресет уже применён синхронно (колбэк CVar-а). Перечитываем всю вкладку: иначе выпадашка
        // освещения и прочие показывали бы старые значения, считались бы «изменёнными», и следующее
        // «Применить» молча вернуло бы их обратно поверх пресета.
        _controller.ReloadValues();
    }

    private void UpdateWarning()
    {
        // Предупреждение о фризе — только пока переключение ждёт «Применить».
        _warning.Visible = _checkBox.Pressed != _cfg.GetCVar(DutyCCVars.LowEndMode);
    }
}

/// <summary>
/// _Duty: чекбокс «Низкая сетевая задержка» — <c>net.buffer_size</c> 1 вместо 2.
/// Если игрок руками выставил буфер больше двух, при снятой галке его значение сохраняется.
/// </summary>
public sealed class OptionLowLatency : BaseOptionCVar<int>
{
    private const int LowLatencyBuffer = 1;

    private readonly CheckBox _checkBox;
    private int _loaded = CVars.NetBufferSize.DefaultValue;

    protected override int Value
    {
        get
        {
            if (_checkBox.Pressed)
                return LowLatencyBuffer;

            return _loaded > LowLatencyBuffer ? _loaded : CVars.NetBufferSize.DefaultValue;
        }
        set
        {
            _loaded = value;
            _checkBox.Pressed = value <= LowLatencyBuffer;
        }
    }

    public OptionLowLatency(OptionsTabControlRow controller, IConfigurationManager cfg, CheckBox checkBox)
        : base(controller, cfg, CVars.NetBufferSize)
    {
        _checkBox = checkBox;
        _checkBox.OnToggled += _ => ValueChanged();
    }
}
