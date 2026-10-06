// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._Duty.LatheUi;

/// <summary>
/// Палитра меню станка. Стилбоксы общие на все строки: их сотни, и плодить по экземпляру на строку незачем.
/// </summary>
public static class DutyLatheStyle
{
    /// <summary>Акцент по умолчанию (NanoGold), если станок не привязан к каналу отдела.</summary>
    public static readonly Color DefaultAccent = Color.FromHex("#C9A35E");

    public static readonly Color Text = Color.FromHex("#E4E4EA");
    public static readonly Color TextMuted = Color.FromHex("#9A9AAB");
    public static readonly Color TextUnavailable = Color.FromHex("#7E7E8A");
    public static readonly Color Good = Color.FromHex("#79D996");
    public static readonly Color Bad = Color.FromHex("#FF7A7A");

    public static readonly StyleBoxFlat RowNormal = new() { BackgroundColor = Color.Transparent };
    public static readonly StyleBoxFlat RowHover = new() { BackgroundColor = Color.FromHex("#26262F") };
    public static readonly StyleBoxFlat RowPressed = new() { BackgroundColor = Color.FromHex("#30303C") };

    public static readonly StyleBoxFlat Chip = new()
    {
        BackgroundColor = Color.FromHex("#262630"),
        ContentMarginLeftOverride = 4,
        ContentMarginRightOverride = 5,
        ContentMarginTopOverride = 1,
        ContentMarginBottomOverride = 1,
    };

    private static readonly Dictionary<Color, StyleBoxFlat> SelectedBoxes = new();

    /// <summary>
    /// Выбранная строка: подсветка и полоска акцентного цвета слева. Кэш по цвету — акцентов всего несколько.
    /// </summary>
    public static StyleBoxFlat RowSelected(Color accent)
    {
        if (SelectedBoxes.TryGetValue(accent, out var box))
            return box;

        box = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#2C2F45"),
            BorderColor = accent,
            BorderThickness = new Thickness(3, 0, 0, 0),
        };
        SelectedBoxes[accent] = box;
        return box;
    }

    /// <summary>
    /// Секунды в «4 с» / «1 мин 20 с».
    /// </summary>
    public static string FormatTime(double seconds)
    {
        var total = (int) Math.Ceiling(Math.Max(0, seconds));
        return total >= 60
            ? Loc.GetString("duty-lathe-time-min", ("m", total / 60), ("s", total % 60))
            : Loc.GetString("duty-lathe-time-sec", ("s", total));
    }

    /// <summary>
    /// Количество листов без хвоста нулей: 2, 1.5, 0.25.
    /// </summary>
    public static string FormatAmount(float amount)
    {
        return MathF.Round(amount, 2).ToString("0.##");
    }
}
