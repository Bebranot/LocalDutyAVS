using System.Numerics;
using Content.Shared._Duty.Parry;
using Content.Shared._Duty.Parry.Components;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Parry;

/// <summary>
/// Полноэкранный слой катсцены QTE: тёмно-серая виньетка по краям, текущая клавиша-подсказка
/// (этапы 1-2) и сжимающаяся шкала с идеальной зоной (этап 3).
/// Виньетка рисуется полосками вручную — тот же приём, что в LobbyVignette_Duty.
/// </summary>
public sealed class QteCutsceneControl : Control
{
    /// <summary>Глубина виньетки от края экрана, доля от меньшей стороны.</summary>
    private const float VignetteDepthFraction = 0.28f;

    /// <summary>Максимальная непрозрачность виньетки — «не сильная», по задумке.</summary>
    private const float VignetteMaxAlpha = 0.55f;

    private static readonly Color VignetteColor = new(0.06f, 0.06f, 0.08f);

    private const int StepWidth = 4;

    private static readonly Color PromptColor = new(0.95f, 0.95f, 0.98f);
    private static readonly Color PromptPendingColor = new(0.45f, 0.45f, 0.5f);
    private static readonly Color BarColor = new(0.85f, 0.85f, 0.9f);
    private static readonly Color PerfectZoneColor = new(0.35f, 0.85f, 0.45f);

    private readonly IGameTiming _timing;
    private readonly Font _promptFont;

    /// <summary>Состояние текущего участника; null — катсцены нет и рисовать нечего.</summary>
    public QteParticipantComponent? Participant;

    public QteCutsceneControl(IGameTiming timing, IResourceCache cache)
    {
        _timing = timing;
        _promptFont = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 48);
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (Participant is not { } participant)
            return;

        DrawVignette(handle);

        switch (participant.Stage)
        {
            case QteStage.Directions:
            case QteStage.Letters:
                DrawPrompt(handle, participant);
                break;

            case QteStage.Final:
                DrawFinalBar(handle, participant);
                break;
        }
    }

    private void DrawVignette(DrawingHandleScreen handle)
    {
        var size = PixelSize;
        var depth = MathF.Min(size.X, size.Y) * VignetteDepthFraction;

        if (depth <= 0)
            return;

        for (var i = 0; i < (int) depth; i += StepWidth)
        {
            var t = 1f - i / depth;
            t = t * t * (3f - 2f * t); // сглаживание, как в лобби-виньетке
            var color = VignetteColor.WithAlpha(t * VignetteMaxAlpha);

            var end = MathF.Min(i + StepWidth, depth);

            // Четыре полосы по периметру — рамка, сгущающаяся к краям экрана.
            handle.DrawRect(new UIBox2(0, i, size.X, end), color);                       // сверху
            handle.DrawRect(new UIBox2(0, size.Y - end, size.X, size.Y - i), color);     // снизу
            handle.DrawRect(new UIBox2(i, 0, end, size.Y), color);                       // слева
            handle.DrawRect(new UIBox2(size.X - end, 0, size.X - i, size.Y), color);     // справа
        }
    }

    private void DrawPrompt(DrawingHandleScreen handle, QteParticipantComponent participant)
    {
        if (participant.CurrentPrompt == QtePromptKey.None)
            return;

        var size = PixelSize;
        var center = new Vector2(size.X / 2f, size.Y * 0.62f);

        // Квадрат-подложка под клавишу; заполняется по мере истечения окна, чтобы
        // игрок видел, сколько времени осталось, не читая цифр.
        const float boxSize = 96f;
        var half = boxSize / 2f;
        var box = new UIBox2(center.X - half, center.Y - half, center.X + half, center.Y + half);

        handle.DrawRect(box, PromptPendingColor.WithAlpha(0.35f));

        var total = (float) (participant.PromptEnd - participant.PromptStart).TotalSeconds;
        if (total > 0)
        {
            var elapsed = (float) (_timing.CurTime - participant.PromptStart).TotalSeconds;
            var remaining = Math.Clamp(1f - elapsed / total, 0f, 1f);

            var filled = new UIBox2(box.Left, box.Bottom - boxSize * remaining, box.Right, box.Bottom);
            handle.DrawRect(filled, PromptColor.WithAlpha(0.28f));
        }

        // Рамка
        DrawOutline(handle, box, PromptColor);

        // Сама буква — грубое центрирование по ширине символа, точная метрика тут не нужна.
        var label = KeyLabel(participant.CurrentPrompt);
        handle.DrawString(_promptFont, new Vector2(center.X - 16f, center.Y - 26f), label, PromptColor);
    }

    private static string KeyLabel(QtePromptKey key) => key switch
    {
        QtePromptKey.W => "W",
        QtePromptKey.A => "A",
        QtePromptKey.S => "S",
        QtePromptKey.D => "D",
        QtePromptKey.Q => "Q",
        QtePromptKey.T => "T",
        QtePromptKey.E => "E",
        QtePromptKey.R => "R",
        QtePromptKey.G => "G",
        QtePromptKey.F => "F",
        QtePromptKey.H => "H",
        _ => string.Empty,
    };

    private void DrawFinalBar(DrawingHandleScreen handle, QteParticipantComponent participant)
    {
        var size = PixelSize;
        var center = new Vector2(size.X / 2f, size.Y * 0.62f);

        const float maxHalfWidth = 220f;
        const float barHeight = 54f;

        var now = _timing.CurTime;
        var total = (float) (participant.FinalPerfect - participant.FinalStart).TotalSeconds;

        if (total <= 0)
            return;

        var elapsed = (float) (now - participant.FinalStart).TotalSeconds;
        // Шкала сжимается к центру; идеальный момент — когда она сходится в точку.
        var shrink = Math.Clamp(1f - elapsed / total, 0f, 1f);
        var halfWidth = maxHalfWidth * shrink;

        // Идеальная зона — неподвижная отметка, до которой шкале нужно сжаться.
        const float perfectHalf = 14f;
        handle.DrawRect(
            new UIBox2(center.X - perfectHalf, center.Y - barHeight / 2f, center.X + perfectHalf, center.Y + barHeight / 2f),
            PerfectZoneColor.WithAlpha(participant.FinalAnswered ? 0.25f : 0.55f));

        if (participant.FinalAnswered)
            return; // уже кликнул — шкалу не рисуем, ждём соперника

        // Две сходящиеся к центру створки.
        const float jawWidth = 8f;
        var left = center.X - halfWidth;
        var right = center.X + halfWidth;

        handle.DrawRect(new UIBox2(left - jawWidth, center.Y - barHeight / 2f, left, center.Y + barHeight / 2f), BarColor);
        handle.DrawRect(new UIBox2(right, center.Y - barHeight / 2f, right + jawWidth, center.Y + barHeight / 2f), BarColor);
    }

    private static void DrawOutline(DrawingHandleScreen handle, UIBox2 box, Color color)
    {
        const float thickness = 3f;
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Right, box.Top + thickness), color);
        handle.DrawRect(new UIBox2(box.Left, box.Bottom - thickness, box.Right, box.Bottom), color);
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Left + thickness, box.Bottom), color);
        handle.DrawRect(new UIBox2(box.Right - thickness, box.Top, box.Right, box.Bottom), color);
    }
}
