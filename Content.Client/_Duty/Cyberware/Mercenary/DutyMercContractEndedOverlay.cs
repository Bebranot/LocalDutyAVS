// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.Cyberware.Mercenary;

/// <summary>
/// _Duty: экран «КОНТРАКТ ЗАКРЫТ» — родственник flatline нейроинтерфейса NT, но серо-зелёный и без драмы:
/// для наёмника смерть цели — закрытая сделка. Затемнение полупрозрачное и короткое, играть не мешает.
/// Расторжение контракта показывается тем же экраном с другой надписью и «рваной» линией.
/// </summary>
public sealed partial class DutyMercContractEndedOverlay : Overlay
{
    private const int EndedZIndex = 940;
    private const float FadeIn = 0.25f;
    private const float FadeOut = 0.8f;

    // Тот же CRT-шейдер, что у flatline NT: одно «железо» на экране, разные прошивки.
    private static readonly ProtoId<ShaderPrototype> CrtShaderId = "DutyNeuroCrt";

    private static readonly Color Backdrop = new(0.01f, 0.03f, 0.015f, 0.62f);
    private static readonly Color Title = Color.FromHex("#7CFF9B");
    private static readonly Color TitleGlow = Color.FromHex("#0F5A2E");
    private static readonly Color Sub = Color.FromHex("#9FBFA8");
    private static readonly Color Trace = Color.FromHex("#3DDC84");
    private static readonly Color Steel = Color.FromHex("#4C6B55");

    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private readonly ShaderInstance _crt;
    private readonly Font _titleFont;
    private readonly Font _monoFont;

    private readonly string _closedTitle;
    private readonly string _terminatedTitle;

    private DutyMercContractClientSystem? _system;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public DutyMercContractEndedOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = EndedZIndex;

        _closedTitle = Loc.GetString("duty-merc-ended-closed-title");
        _terminatedTitle = Loc.GetString("duty-merc-ended-terminated-title");

        _crt = _proto.Index(CrtShaderId).InstanceUnique();
        _titleFont = _resCache.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 34);
        _monoFont = _resCache.GetFont(new[]
        {
            "/Fonts/RobotoMono/RobotoMono-Regular.ttf",
            "/Fonts/NotoSans/NotoSans-Regular.ttf"
        }, 14);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _system ??= _entMan.System<DutyMercContractClientSystem>();
        return _system.EndedElapsed >= 0f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_system is not { } system)
            return;

        var elapsed = system.EndedElapsed;
        if (elapsed < 0f)
            return;

        var alpha = Envelope(elapsed);
        if (alpha <= 0.005f)
            return;

        var handle = args.ScreenHandle;
        handle.SetTransform(Matrix3x2.Identity);

        var screen = _clyde.ScreenSize;
        var uiScale = (args.ViewportControl as Control)?.UIScale ?? 1f;
        var terminated = system.EndedTerminated;

        _crt.SetParameter("alpha", alpha);
        _crt.SetParameter("glitch_intensity", terminated ? 0.45f : (elapsed < 0.5f ? 0.5f * (1f - elapsed / 0.5f) : 0.06f));
        handle.UseShader(_crt);
        handle.DrawRect(new UIBox2(0f, 0f, screen.X, screen.Y), Backdrop);
        handle.UseShader(null);

        var cx = screen.X * 0.5f;
        var cy = screen.Y * 0.42f;

        DrawTrace(handle, screen.X, cy - 40f * uiScale, alpha, uiScale, terminated);

        var title = terminated ? _terminatedTitle : _closedTitle;
        var titleColor = terminated ? Steel : Title;
        var dims = handle.GetDimensions(_titleFont, title, uiScale);
        var titlePos = new Vector2(cx - dims.X * 0.5f, cy - dims.Y * 0.5f);
        handle.DrawString(_titleFont, titlePos + new Vector2(2f, 2f) * uiScale, title, uiScale, TitleGlow.WithAlpha(0.8f * alpha));
        handle.DrawString(_titleFont, titlePos, title, uiScale, titleColor.WithAlpha(alpha));

        var sub = Loc.GetString(terminated ? "duty-merc-ended-terminated-sub" : "duty-merc-ended-closed-sub",
            ("target", system.EndedTargetName));
        var subDims = handle.GetDimensions(_monoFont, sub, uiScale);
        var subPos = new Vector2(cx - subDims.X * 0.5f, titlePos.Y + dims.Y + 10f * uiScale);
        handle.DrawString(_monoFont, subPos, sub, uiScale, Sub.WithAlpha(alpha));
    }

    /// <summary>
    /// Ровная линия с одним последним всплеском, как у flatline; при расторжении — обрыв посередине.
    /// </summary>
    private static void DrawTrace(DrawingHandleScreen handle, int width, float y, float alpha, float uiScale, bool broken)
    {
        var bright = Trace.WithAlpha(0.7f * alpha);
        var dim = Trace.WithAlpha(0.25f * alpha);
        var peak = width * 0.24f;
        var a = 18f * uiScale;
        var b = 16f * uiScale;
        var c = 10f * uiScale;
        var d = 6f * uiScale;
        var e = 2f * uiScale;

        handle.DrawLine(new Vector2(0f, y), new Vector2(peak - a, y), dim);
        handle.DrawLine(new Vector2(peak - a, y), new Vector2(peak - c, y - b), bright);
        handle.DrawLine(new Vector2(peak - c, y - b), new Vector2(peak - e, y + c), bright);
        handle.DrawLine(new Vector2(peak - e, y + c), new Vector2(peak + d, y), bright);

        if (!broken)
        {
            handle.DrawLine(new Vector2(peak + d, y), new Vector2(width, y), bright);
            return;
        }

        var gap = width * 0.5f;
        handle.DrawLine(new Vector2(peak + d, y), new Vector2(gap - 20f * uiScale, y), bright);
        handle.DrawLine(new Vector2(gap + 20f * uiScale, y + 4f * uiScale), new Vector2(width, y + 4f * uiScale), dim);
    }

    private static float Envelope(float elapsed)
    {
        var total = DutyMercContractClientSystem.EndedDuration;
        if (elapsed <= 0f || elapsed >= total)
            return 0f;
        if (elapsed < FadeIn)
            return elapsed / FadeIn;
        if (elapsed > total - FadeOut)
            return MathF.Max(0f, (total - elapsed) / FadeOut);
        return 1f;
    }
}
