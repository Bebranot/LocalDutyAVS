using System.Numerics;
using Content.Shared._Duty.Parry;
using Content.Shared._Duty.Parry.Components;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Parry;

/// <summary>
/// Полноэкранный слой QTE-катсцены: тёмно-серая виньетка по краям, а поверх — картинка этапа.
///
/// Этапы 1-2 (буквы): кнопка-кружок с подписью клавиши и сходящееся к ней кольцо-таймер —
/// жать можно в любой момент, пока кольцо идёт.
///
/// Этап 3 (решающий, ПКМ): горизонтальная шкала с бегущим маркером и отмеченной на ней
/// целевой зоной — как в мини-игре взлома Mass Effect. Отдельная фигура для этого этапа
/// осознанно: кольцо здесь плохо читалось (не видно, «на сколько мимо» промазал), а по
/// плоской шкале точность видна на глаз.
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

    // Палитра чуть живее нейтрально-серой: холодный циан на кнопках и кольце вместо
    // просто белого/серого читается заметно приятнее на тёмной виньетке.
    private static readonly Color VignetteColor = new(0.05f, 0.06f, 0.09f);
    private static readonly Color ButtonFill = new(0.08f, 0.11f, 0.15f);
    private static readonly Color ButtonEdge = new(0.55f, 0.90f, 0.98f);
    private static readonly Color GlowColor = new(0.35f, 0.80f, 0.95f);
    private static readonly Color RingColor = new(0.55f, 0.90f, 0.98f);
    private static readonly Color TrackColor = new(0.20f, 0.24f, 0.30f);
    private static readonly Color MarkerColor = new(0.95f, 0.85f, 0.40f);
    private static readonly Color PerfectColor = new(0.35f, 0.90f, 0.55f);
    private static readonly Color MissColor = new(0.95f, 0.30f, 0.30f);
    private static readonly Color WinColor = new(0.45f, 0.90f, 0.55f);
    private static readonly Color LoseColor = new(0.95f, 0.35f, 0.35f);
    private static readonly Color DrawColor = new(0.95f, 0.80f, 0.35f);

    private readonly IClyde _clyde;
    private readonly IGameTiming _timing;
    private readonly Font _font;
    private readonly Font _resultFont;

    /// <summary>Буфер вершин кольца — размер постоянный, выделяем один раз (см. DrawRing).</summary>
    private readonly Vector2[] _ringVerts = new Vector2[(CircleSegments + 1) * 2];

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
                DrawFinalBar(handle, participant, center, scale);
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

        // Мягкое свечение под кольцом — просто более широкое и тусклое повторение того же
        // кольца, без него кружок на тёмной виньетке смотрелся плоско.
        if (!missed)
            DrawRing(handle, center, ringRadius, GlowColor.WithAlpha(0.20f), 12f * scale);

        DrawRing(handle, center, ringRadius, missed ? MissColor : RingColor, 3f * scale);
    }

    // ── Этап 3: горизонтальная шкала с бегущим маркером ───────

    private void DrawFinalBar(DrawingHandleScreen handle, QteParticipantComponent participant, Vector2 center, float scale)
    {
        var missed = IsMissFlashing(participant);

        var windup = (float) (participant.FinalPerfect - participant.FinalStart).TotalSeconds;
        var total = (float) (participant.FinalDeadline - participant.FinalStart).TotalSeconds;
        if (windup <= 0 || total <= 0)
            return;

        var halfWidth = 260f * scale;
        var barHeight = 10f * scale;
        var markerRadius = 15f * scale;

        var left = center.X - halfWidth;
        var right = center.X + halfWidth;
        var trackBox = new UIBox2(left, center.Y - barHeight / 2f, right, center.Y + barHeight / 2f);

        // Целевая зона стоит у правого края — маркер доходит до неё ровно к идеальному моменту.
        // Ширина зоны пересчитана из допуска в секундах через скорость маркера по той же шкале,
        // так что «идеально» на экране совпадает с тем, что реально засчитает сервер.
        var pxPerSecond = halfWidth * 2f / windup;
        var zoneHalfWidth = MathF.Max(pxPerSecond * QteTuning.PerfectWindowSeconds, 6f * scale);

        // Лёгкая пульсация зоны — просто чтобы взгляд сам находил, куда целиться.
        var pulse = 0.75f + 0.25f * MathF.Sin((float) _timing.CurTime.TotalSeconds * 6f);
        var zoneColor = PerfectColor.WithAlpha((participant.FinalAnswered ? 0.35f : 0.55f + 0.25f * pulse));
        var zoneBox = new UIBox2(right - zoneHalfWidth, center.Y - barHeight * 1.6f, right + zoneHalfWidth, center.Y + barHeight * 1.6f);

        handle.DrawRect(trackBox, TrackColor.WithAlpha(0.85f));
        handle.DrawRect(zoneBox, zoneColor);
        DrawOutline(handle, trackBox, ButtonEdge.WithAlpha(0.6f), 2f * scale);

        var label = Loc.GetString("duty-qte-key-rmb");
        var dims = handle.GetDimensions(_font, label, scale);
        handle.DrawString(_font, new Vector2(center.X, center.Y - barHeight * 4f) - dims / 2f, label, scale, ButtonEdge);

        if (participant.FinalAnswered)
            return; // уже кликнул — маркер убираем, ждём соперника

        var elapsed = (float) (_timing.CurTime - participant.FinalStart).TotalSeconds;
        if (elapsed > total)
            return;

        var markerX = left + pxPerSecond * elapsed;
        var markerPos = new Vector2(markerX, center.Y);
        var markerColor = missed ? MissColor : MarkerColor;

        handle.DrawCircle(markerPos, markerRadius * 1.8f, markerColor.WithAlpha(0.18f));
        handle.DrawCircle(markerPos, markerRadius, markerColor);
        DrawOutline(handle, new UIBox2(markerPos.X - markerRadius, markerPos.Y - markerRadius, markerPos.X + markerRadius, markerPos.Y + markerRadius), Color.Black.WithAlpha(0.4f), 1.5f * scale);
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

        // Мягкая подложка под кнопкой — без нее плоская заливка терялась на тёмной виньетке.
        handle.DrawCircle(center, radius * 1.35f, (missed ? MissColor : GlowColor).WithAlpha(0.15f));
        handle.DrawCircle(center, radius, missed ? MissColor.WithAlpha(0.30f) : ButtonFill.WithAlpha(0.85f));
        DrawRing(handle, center, radius, edge, 3f * scale);

        if (label.Length == 0)
            return;

        var dims = handle.GetDimensions(_font, label, scale);
        handle.DrawString(_font, center - dims / 2f, label, scale, edge);
    }

    /// <summary>
    /// Кольцо заданной толщины — полоса треугольников между внутренней и внешней окружностями.
    /// Готовый DrawCircle(filled: false) не подошёл: он даёт линию в один пиксель, которая на
    /// больших разрешениях теряется, а толщину ему задать нельзя.
    ///
    /// Буфер вершин — поле, а не stackalloc: в песочнице клиента stackalloc запрещён
    /// (см. комментарий в AtmosphereSystem.Gases.cs), а размер тут всё равно постоянный,
    /// так что заодно не мусорим в GC каждый кадр.
    /// </summary>
    private void DrawRing(DrawingHandleScreen handle, Vector2 center, float radius, Color color, float thickness)
    {
        var inner = MathF.Max(radius - thickness / 2f, 0f);
        var outer = radius + thickness / 2f;

        for (var i = 0; i <= CircleSegments; i++)
        {
            var angle = MathF.Tau * i / CircleSegments;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

            _ringVerts[i * 2] = center + dir * inner;
            _ringVerts[i * 2 + 1] = center + dir * outer;
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, _ringVerts, color);
    }

    /// <summary>Тонкая рамка прямоугольника — четыре узкие полосы по периметру.</summary>
    private static void DrawOutline(DrawingHandleScreen handle, UIBox2 box, Color color, float thickness)
    {
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Right, box.Top + thickness), color);
        handle.DrawRect(new UIBox2(box.Left, box.Bottom - thickness, box.Right, box.Bottom), color);
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Left + thickness, box.Bottom), color);
        handle.DrawRect(new UIBox2(box.Right - thickness, box.Top, box.Right, box.Bottom), color);
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
