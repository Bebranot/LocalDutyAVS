// SPDX-FileCopyrightText: 2025 LocalDuty
//
// SPDX-License-Identifier: MIT

using System.Numerics;
using Content.Client.Parallax.Data;
using Content.Client.Parallax.Managers;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client.MainMenu.UI;

/// <summary>
///     Параллакс-фон главного меню LocalDuty.
///     Слой 0 (space-bg)  — почти неподвижен, слабо реагирует на курсор, едва заметно дрейфует сам по себе.
///     Слой 1 (stars-bg)  — реагирует на курсор заметнее и заметнее скользит сам по себе (звёзды "плывут").
///     Курсорная реакция сглаживается по реальному времени кадра, а не по количеству кадров,
///     поэтому скорость "доводки" к курсору одинаковая на любом FPS.
/// </summary>
public sealed class MainMenuParallaxControl_Duty : Control
{
    [Dependency] private readonly IGameTiming _timing      = default!;
    [Dependency] private readonly IParallaxManager _parallax = default!;
    [Dependency] private readonly IInputManager _input      = default!;

    private const string PrototypeName = "ParallaxDuty";

    // Насколько курсор смещает каждый слой.
    // Значение = максимальное смещение в пикселях при курсоре у края экрана.
    private const float Layer0MouseStrength = 12f;  // фон — почти стоит
    private const float Layer1MouseStrength = 36f;  // звёзды — заметно двигаются

    // Период полураспада (сек) экспоненциального сглаживания: за это время смещение
    // проходит половину пути до цели. Меньше = быстрее и "упруже" догоняет курсор.
    // Слой звёзд догоняет курсор быстрее фона — это добавляет ощущение глубины.
    private const float Layer0SmoothingHalfLife = 0.12f;
    private const float Layer1SmoothingHalfLife = 0.06f;

    // Текущее сглаженное смещение для каждого слоя (курсорная часть, без автономного скольжения).
    private Vector2 _currentOffset0 = Vector2.Zero;
    private Vector2 _currentOffset1 = Vector2.Zero;

    public MainMenuParallaxControl_Duty()
    {
        IoCManager.InjectDependencies(this);

        RectClipContent = true;

        _parallax.LoadParallaxByName(PrototypeName);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        // ── Вычисляем целевое смещение на основе позиции курсора ──────────────
        var screenSize  = Size;                           // размер контрола в UI-пикселях
        var mousePos    = _input.MouseScreenPosition.Position; // позиция мыши на экране

        // Нормализованное отклонение курсора от центра: диапазон [-1 .. +1]
        Vector2 normalizedDelta;
        if (screenSize.X > 0 && screenSize.Y > 0)
        {
            normalizedDelta = new Vector2(
                (float)(mousePos.X / screenSize.X) * 2f - 1f,
                (float)(mousePos.Y / screenSize.Y) * 2f - 1f
            );
            // Clamp — на случай если мышь за пределами окна
            normalizedDelta = Vector2.Clamp(normalizedDelta, new Vector2(-1f), new Vector2(1f));
        }
        else
        {
            normalizedDelta = Vector2.Zero;
        }

        var targetOffset0 = normalizedDelta * Layer0MouseStrength;
        var targetOffset1 = normalizedDelta * Layer1MouseStrength;

        // ── Плавное сглаживание, независимое от FPS ─────────────────────────────
        // factor = 1 - 2^(-dt/halfLife): за halfLife секунд смещение проходит половину
        // оставшегося пути до цели, вне зависимости от того, сколько кадров прошло.
        var dt = (float)_timing.FrameTime.TotalSeconds;
        var factor0 = 1f - MathF.Pow(2f, -dt / Layer0SmoothingHalfLife);
        var factor1 = 1f - MathF.Pow(2f, -dt / Layer1SmoothingHalfLife);
        _currentOffset0 = Vector2.Lerp(_currentOffset0, targetOffset0, factor0);
        _currentOffset1 = Vector2.Lerp(_currentOffset1, targetOffset1, factor1);

        // ── Автономное скольжение слоёв (эффект "плывущего" космоса) ────────────
        // Используем то же реальное время и поле scrolling из прототипа, что и штатный
        // ParallaxControl — независимо от курсора и от того, идёт ли игровая сессия.
        var realTime = (float)_timing.RealTime.TotalSeconds;

        // ── Рисуем слои ────────────────────────────────────────────────────────
        var layers = _parallax.GetParallaxLayers(PrototypeName);

        for (var i = 0; i < layers.Length; i++)
        {
            var layer      = layers[i];
            var tex        = layer.Texture;
            var ourSize    = PixelSize;

            // Масштабируем текстуру под ширину экрана (как в оригинальном ParallaxControl)
            var texSize = new Vector2i(
                (int)(tex.Size.X * Size.X * layer.Config.Scale.X / 1920f),
                (int)(tex.Size.Y * Size.X * layer.Config.Scale.Y / 1920f)
            );

            texSize.X = Math.Max(texSize.X, 1);
            texSize.Y = Math.Max(texSize.Y, 1);

            // Смещение на основе курсора для текущего слоя
            var mouseOffset = i == 0 ? _currentOffset0 : _currentOffset1;

            // Автономное скольжение слоя (px/сек из прототипа) — не зависит от курсора.
            var slideOffset = layer.Config.Scrolling * realTime;

            if (layer.Config.Tiled)
            {
                // Для тайлового слоя суммируем курсорное смещение и автономное скольжение
                var scaledOffset = (mouseOffset + slideOffset).Floored();

                // Модуло чтобы не рисовать лишние тайлы за экраном
                scaledOffset.X = ((scaledOffset.X % texSize.X) + texSize.X) % texSize.X;
                scaledOffset.Y = ((scaledOffset.Y % texSize.Y) + texSize.Y) % texSize.Y;

                for (var x = -scaledOffset.X; x < ourSize.X; x += texSize.X)
                {
                    for (var y = -scaledOffset.Y; y < ourSize.Y; y += texSize.Y)
                    {
                        handle.DrawTextureRect(tex, UIBox2.FromDimensions(new Vector2(x, y), texSize));
                    }
                }
            }
            else
            {
                // Не тайловый — рисуем по центру со смещением мыши и автономным скольжением
                var origin = ((ourSize - texSize) / 2)
                             + layer.Config.ControlHomePosition
                             + mouseOffset
                             + slideOffset;
                handle.DrawTextureRect(tex, UIBox2.FromDimensions(origin, texSize));
            }
        }
    }
}