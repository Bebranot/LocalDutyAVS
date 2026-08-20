// SPDX-FileCopyrightText: 2025 LocalDuty
//
// SPDX-License-Identifier: MIT

using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client.Lobby.UI;

/// <summary>
///     Вертикальная полоска-направляющая слева от блока кнопок лобби.
///     Тянется во всю высоту контейнера (лого + кнопки), концы плавно
///     растворяются, чтобы линия не выглядела обрубленной.
/// </summary>
public sealed class LobbySideStripe_Duty : Control
{
    /// <summary>Цвет полоски — тот же, что у кнопок в обычном состоянии.</summary>
    private static readonly Color StripeColor = Color.FromHex("#4E5754");

    /// <summary>Доля высоты, на которой полоска затухает у каждого конца.</summary>
    private const float FadeFraction = 0.12f;

    /// <summary>Высота одной ступеньки градиента затухания, в пикселях.</summary>
    private const int StepHeight = 2;

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = PixelSize;
        if (size.X <= 0 || size.Y <= 0)
            return;

        var fade = size.Y * FadeFraction;

        for (var y = 0; y < size.Y; y += StepHeight)
        {
            // Расстояние до ближайшего конца полоски.
            var edgeDistance = MathF.Min(y, size.Y - y);
            var t = fade > 0f ? MathF.Min(1f, edgeDistance / fade) : 1f;
            // Кубическая сглаживающая кривая — как в LobbyVignette_Duty.
            t = t * t * (3f - 2f * t);

            var color = StripeColor.WithAlpha(StripeColor.A * t);

            handle.DrawRect(
                new UIBox2(0, y, size.X, MathF.Min(y + StepHeight, size.Y)),
                color
            );
        }
    }
}
