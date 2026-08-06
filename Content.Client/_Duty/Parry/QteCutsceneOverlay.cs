using System.Numerics;
using Content.Shared._Duty.Parry;
using Content.Shared._Duty.Parry.Components;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Parry;

/// <summary>
/// Полноэкранный слой QTE-катсцены: тёмно-серая виньетка по краям, кнопка-кружок с подписью
/// клавиши и сходящееся к ней кольцо, а в финале — крупная надпись с исходом дуэли.
///
/// Кольцо одно на все три этапа: единый визуальный язык читается лучше, чем разные фигуры на
/// разных этапах. На этапах 1-2 оно просто таймер — жать можно в любой момент, пока оно идёт.
/// На этапе 3 важен момент совпадения кольца с контуром кнопки.
///
/// Сделан оверлеем, а не UI-контролом: дочерний Control у WindowRoot не растягивается сам
/// (WindowRoot — не LayoutContainer, привязки якорей там не работают) и остался бы нулевого
/// размера. Оверлей же берёт размеры прямо у окна, как LazarusOverlay.
/// </summary>
public sealed class QteCutsceneOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private const string FontPath = "/Fonts/NotoSans/NotoSans-Bold.ttf";
    private const int BaseFontSize = 44;
    private const int ResultFontSize = 72;

    /// <summary>Глубина виньетки от края экрана, доля от меньшей стороны.</summary>
    private const float VignetteDepthFraction = 0.28f;

    /// <summary>Максимальная непрозрачность виньетки — «не сильная», по задумке.</summary>
    private const float VignetteMaxAlpha = 0.55f;

    private const int StepWidth = 4;

    /// <summary>Сегментов в окружности: на глаз уже неотличимо от гладкой.</summary>
    private const int CircleSegments = 48;

    private static readonly Color VignetteColor = new(0.06f, 0.06f, 0.08f);
    private static readonly Color ButtonFill = new(0.10f, 0.10f, 0.13f);
    private static readonly Color ButtonEdge = new(0.95f, 0.95f, 0.98f);
    private static readonly Color RingColor = new(0.85f, 0.85f, 0.92f);
    private static readonly Color PerfectColor = new(0.35f, 0.85f, 0.45f);
    private static readonly Color MissColor = new(0.90f, 0.25f, 0.25f);
    private static readonly Color WinColor = new(0.45f, 0.90f, 0.50f);
    private static readonly Color LoseColor = new(0.90f, 0.30f, 0.30f);
    private static readonly Color DrawColor = new(0.90f, 0.80f, 0.35f);

    private readonly IClyde _clyde;
    private readonly IGameTiming _timing;
    private readonly Font _font;
    private readonly Font _resultFont;

    /// <summary>Состояние текущего участника; null — катсцены нет и рисовать нечего.</summary>
    public QteParticipantComponent? Participant;

    public QteCutsceneOverlay(IClyde clyde, IGameTiming timing, IResourceCache cache)
    {
        _clyde = clyde;
        _timing = timing;

        var res = cache.GetResource<FontResource>(FontPath);
        _font = new VectorFont(res, BaseFontSize);
        _resultFont = new VectorFont(res, ResultFontSize);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (Participant is not { } participant)
            return;

        var handle = args.ScreenHandle;
        var size = _clyde.ScreenSize;

        // Интерфейс масштабируется под разрешение, иначе на 4K подсказка была бы с ноготь.
        var scale = Math.Clamp(size.Y / 1080f, 0.7f, 2.2f);
        var center = new Vector2(size.X / 2f, size.Y * 0.6f);

        DrawVignette(handle, size);

        switch (participant.Stage)
        {
            case QteStage.Directions:
            case QteStage.Letters:
                DrawPromptRing(handle, participant, center, scale);
                break;

            case QteStage.Final:
                DrawFinalRing(handle, participant, center, scale);
                break;

            case QteStage.Result:
                DrawResult(handle, participant, center, scale);
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

    // ── Этапы 1-2: кольцо как таймер ──────────────────────────

    private void DrawPromptRing(DrawingHandleScreen handle, QteParticipantComponent participant, Vector2 center, float scale)
    {
        if (participant.CurrentPrompt == QtePromptKey.None)
            return;

        var buttonRadius = 46f * scale;
        var missed = IsMissFlashing(participant);

        var total = (float) (participant.PromptEnd - participant.PromptStart).TotalSeconds;
        var elapsed = (float) (_timing.CurTime - participant.PromptStart).TotalSeconds;

        DrawButton(handle, center, buttonRadius, scale, missed, KeyLabel(participant.CurrentPrompt));

        if (total <= 0)
            return;

        // Кольцо идёт снаружи к контуру кнопки: сошлось — время вышло.
        var progress = Math.Clamp(elapsed / total, 0f, 1f);
        var ringRadius = MathHelper.Lerp(buttonRadius * 3.2f, buttonRadius, progress);

        DrawRing(handle, center, ringRadius, missed ? MissColor : RingColor, 3f * scale);
    }

    // ── Этап 3: кольцо как точность ───────────────────────────

    private void DrawFinalRing(DrawingHandleScreen handle, QteParticipantComponent participant, Vector2 center, float scale)
    {
        var buttonRadius = 46f * scale;
        var missed = IsMissFlashing(participant);

        var windup = (float) (participant.FinalPerfect - participant.FinalStart).TotalSeconds;
        if (windup <= 0)
            return;

        // Допуск читается глазами: полоса вокруг контура кнопки шириной в окно попадания,
        // пересчитанное из секунд в пиксели по той же скорости, с которой сходится кольцо.
        var ringSpan = buttonRadius * 2.2f;
        var perfectBand = ringSpan * (QteTuning.PerfectWindowSeconds / windup);

        DrawRing(handle, center, buttonRadius, PerfectColor.WithAlpha(participant.FinalAnswered ? 0.35f : 0.8f), perfectBand);
        DrawButton(handle, center, buttonRadius, scale, missed, Loc.GetString("duty-qte-key-rmb"));

        if (participant.FinalAnswered)
            return; // уже кликнул — кольцо убираем, ждём соперника

        var elapsed = (float) (_timing.CurTime - participant.FinalStart).TotalSeconds;

        // До идеального момента кольцо идёт снаружи к контуру, после — продолжает внутрь:
        // это и есть grace-период, видно, что момент упущен.
        var radius = buttonRadius + ringSpan * (1f - elapsed / windup);
        if (radius <= 1f)
            return;

        DrawRing(handle, center, radius, missed ? MissColor : RingColor, 4f * scale);
    }

    // ── Экран итога ───────────────────────────────────────────

    private void DrawResult(DrawingHandleScreen handle, QteParticipantComponent participant, Vector2 center, float scale)
    {
        var (key, color) = participant.Outcome switch
        {
            QteOutcome.Win => ("duty-qte-result-win", WinColor),
            QteOutcome.Lose => ("duty-qte-result-lose", LoseColor),
            QteOutcome.Draw => ("duty-qte-result-draw", DrawColor),
            _ => (string.Empty, Color.White),
        };

        if (key.Length == 0)
            return;

        var text = Loc.GetString(key);
        var dims = handle.GetDimensions(_resultFont, text, scale);

        handle.DrawString(_resultFont, center - dims / 2f + new Vector2(3f, 3f) * scale, text, scale, Color.Black.WithAlpha(0.6f));
        handle.DrawString(_resultFont, center - dims / 2f, text, scale, color);
    }

    // ── Примитивы ─────────────────────────────────────────────

    private void DrawButton(DrawingHandleScreen handle, Vector2 center, float radius, float scale, bool missed, string label)
    {
        var edge = missed ? MissColor : ButtonEdge;

        DrawDisc(handle, center, radius, missed ? MissColor.WithAlpha(0.30f) : ButtonFill.WithAlpha(0.75f));
        DrawRing(handle, center, radius, edge, 3f * scale);

        if (label.Length == 0)
            return;

        var dims = handle.GetDimensions(_font, label, scale);
        handle.DrawString(_font, center - dims / 2f, label, scale, edge);
    }

    /// <summary>Залитый круг — веер треугольников от центра.</summary>
    private static void DrawDisc(DrawingHandleScreen handle, Vector2 center, float radius, Color color)
    {
        Span<Vector2> verts = stackalloc Vector2[CircleSegments + 2];
        verts[0] = center;

        for (var i = 0; i <= CircleSegments; i++)
        {
            var angle = MathF.Tau * i / CircleSegments;
            verts[i + 1] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, verts, color);
    }

    /// <summary>
    /// Кольцо заданной толщины. Рисуется полосой треугольников между внутренней и внешней
    /// окружностями — линиями толщину не задать, а тонкая линия на больших экранах теряется.
    /// </summary>
    private static void DrawRing(DrawingHandleScreen handle, Vector2 center, float radius, Color color, float thickness)
    {
        var inner = MathF.Max(radius - thickness / 2f, 0f);
        var outer = radius + thickness / 2f;

        Span<Vector2> verts = stackalloc Vector2[(CircleSegments + 1) * 2];

        for (var i = 0; i <= CircleSegments; i++)
        {
            var angle = MathF.Tau * i / CircleSegments;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

            verts[i * 2] = center + dir * inner;
            verts[i * 2 + 1] = center + dir * outer;
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, verts, color);
    }

    private bool IsMissFlashing(QteParticipantComponent participant)
    {
        return _timing.CurTime < participant.MissFlashUntil;
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
