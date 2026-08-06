using System.Numerics;
using Content.Shared._Duty.Parry;
using Content.Shared._Duty.Parry.Components;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Parry;

/// <summary>
/// Полноэкранный слой QTE-катсцены: тёмно-серая виньетка по краям, текущая клавиша-подсказка
/// (этапы 1-2) и сжимающаяся шкала с идеальной зоной (этап 3).
///
/// Сделан оверлеем, а не UI-контролом: дочерний Control у WindowRoot не растягивается сам
/// (WindowRoot — не LayoutContainer, привязки якорей там не работают) и остался бы нулевого
/// размера. Оверлей же берёт размеры прямо у окна, как LazarusOverlay.
/// </summary>
public sealed class QteCutsceneOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private const string FontPath = "/Fonts/NotoSans/NotoSans-Bold.ttf";
    private const int BaseFontSize = 48;

    /// <summary>Глубина виньетки от края экрана, доля от меньшей стороны.</summary>
    private const float VignetteDepthFraction = 0.28f;

    /// <summary>Максимальная непрозрачность виньетки — «не сильная», по задумке.</summary>
    private const float VignetteMaxAlpha = 0.55f;

    private const int StepWidth = 4;

    private static readonly Color VignetteColor = new(0.06f, 0.06f, 0.08f);
    private static readonly Color PromptColor = new(0.95f, 0.95f, 0.98f);
    private static readonly Color PromptPendingColor = new(0.45f, 0.45f, 0.5f);
    private static readonly Color BarColor = new(0.85f, 0.85f, 0.9f);
    private static readonly Color PerfectZoneColor = new(0.35f, 0.85f, 0.45f);

    private readonly IClyde _clyde;
    private readonly IGameTiming _timing;
    private readonly Font _font;

    /// <summary>Состояние текущего участника; null — катсцены нет и рисовать нечего.</summary>
    public QteParticipantComponent? Participant;

    public QteCutsceneOverlay(IClyde clyde, IGameTiming timing, IResourceCache cache)
    {
        _clyde = clyde;
        _timing = timing;
        _font = new VectorFont(cache.GetResource<FontResource>(FontPath), BaseFontSize);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (Participant is not { } participant)
            return;

        var handle = args.ScreenHandle;
        var size = _clyde.ScreenSize;

        // Интерфейс масштабируется под разрешение, иначе на 4K подсказка была бы с ноготь.
        var scale = Math.Clamp(size.Y / 1080f, 0.7f, 2.2f);

        DrawVignette(handle, size);

        switch (participant.Stage)
        {
            case QteStage.Directions:
            case QteStage.Letters:
                DrawPrompt(handle, participant, size, scale);
                break;

            case QteStage.Final:
                DrawFinalBar(handle, participant, size, scale);
                break;
        }
    }

    private static void DrawVignette(DrawingHandleScreen handle, Vector2i size)
    {
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
            handle.DrawRect(new UIBox2(0, i, size.X, end), color);                   // сверху
            handle.DrawRect(new UIBox2(0, size.Y - end, size.X, size.Y - i), color); // снизу
            handle.DrawRect(new UIBox2(i, 0, end, size.Y), color);                   // слева
            handle.DrawRect(new UIBox2(size.X - end, 0, size.X - i, size.Y), color); // справа
        }
    }

    private void DrawPrompt(DrawingHandleScreen handle, QteParticipantComponent participant, Vector2i size, float scale)
    {
        if (participant.CurrentPrompt == QtePromptKey.None)
            return;

        var center = new Vector2(size.X / 2f, size.Y * 0.62f);
        var half = 48f * scale;
        var box = new UIBox2(center.X - half, center.Y - half, center.X + half, center.Y + half);

        handle.DrawRect(box, PromptPendingColor.WithAlpha(0.35f));

        // Подложка убывает вместе с окном — видно, сколько осталось, без чтения цифр.
        var total = (float) (participant.PromptEnd - participant.PromptStart).TotalSeconds;
        if (total > 0)
        {
            var elapsed = (float) (_timing.CurTime - participant.PromptStart).TotalSeconds;
            var remaining = Math.Clamp(1f - elapsed / total, 0f, 1f);

            var filled = new UIBox2(box.Left, box.Bottom - half * 2f * remaining, box.Right, box.Bottom);
            handle.DrawRect(filled, PromptColor.WithAlpha(0.28f));
        }

        DrawOutline(handle, box, PromptColor, 3f * scale);

        var label = KeyLabel(participant.CurrentPrompt);
        if (label.Length == 0)
            return;

        // Честное центрирование по фактическим метрикам шрифта.
        var dims = handle.GetDimensions(_font, label, scale);
        handle.DrawString(_font, center - dims / 2f, label, scale, PromptColor);
    }

    private void DrawFinalBar(DrawingHandleScreen handle, QteParticipantComponent participant, Vector2i size, float scale)
    {
        var center = new Vector2(size.X / 2f, size.Y * 0.62f);

        var maxHalfWidth = 220f * scale;
        var barHeight = 54f * scale;
        var perfectHalf = 14f * scale;

        var total = (float) (participant.FinalPerfect - participant.FinalStart).TotalSeconds;
        if (total <= 0)
            return;

        // Идеальная зона — неподвижная отметка, до которой шкале нужно сжаться.
        handle.DrawRect(
            new UIBox2(center.X - perfectHalf, center.Y - barHeight / 2f, center.X + perfectHalf, center.Y + barHeight / 2f),
            PerfectZoneColor.WithAlpha(participant.FinalAnswered ? 0.25f : 0.55f));

        if (participant.FinalAnswered)
            return; // уже кликнул — шкалу не рисуем, ждём соперника

        var elapsed = (float) (_timing.CurTime - participant.FinalStart).TotalSeconds;
        var shrink = Math.Clamp(1f - elapsed / total, 0f, 1f);
        var halfWidth = maxHalfWidth * shrink;

        // Две сходящиеся к центру створки.
        var jawWidth = 8f * scale;
        var left = center.X - halfWidth;
        var right = center.X + halfWidth;

        handle.DrawRect(new UIBox2(left - jawWidth, center.Y - barHeight / 2f, left, center.Y + barHeight / 2f), BarColor);
        handle.DrawRect(new UIBox2(right, center.Y - barHeight / 2f, right + jawWidth, center.Y + barHeight / 2f), BarColor);
    }

    private static void DrawOutline(DrawingHandleScreen handle, UIBox2 box, Color color, float thickness)
    {
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Right, box.Top + thickness), color);
        handle.DrawRect(new UIBox2(box.Left, box.Bottom - thickness, box.Right, box.Bottom), color);
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Left + thickness, box.Bottom), color);
        handle.DrawRect(new UIBox2(box.Right - thickness, box.Top, box.Right, box.Bottom), color);
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
}
