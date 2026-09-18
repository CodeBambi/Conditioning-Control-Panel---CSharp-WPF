using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.BackRoom.Overlays;

// THE BACK ROOM, Hypno v3 overlays (CONTRACT 10.13.B), the maths half: where a page rect lands on the
// desktop, and every curve the mockup draws (the wash envelope, the gif growing out of a rect, the
// spiral's fades, the tunnel's easing and shape). No WPF here; the overlay windows only sample these
// per frame, and BackRoomOverlayMathTests pins them against the mockup's numbers.

/// <summary>A rect in physical screen pixels (or, after <see cref="BackRoomOverlayMath.ToLocalDip"/>, in DIPs).</summary>
public readonly record struct PxRect(double X, double Y, double W, double H)
{
    public double Cx => X + W / 2;
    public double Cy => Y + H / 2;
    public bool Contains(double x, double y) => x >= X && x < X + W && y >= Y && y < Y + H;

    public double OverlapArea(PxRect o)
    {
        double w = Math.Min(X + W, o.X + o.W) - Math.Max(X, o.X);
        double h = Math.Min(Y + H, o.Y + o.H) - Math.Max(Y, o.Y);
        return w > 0 && h > 0 ? w * h : 0;
    }
}

/// <summary>
/// The room window's WebView2 as the host sees it at play time. <see cref="ControlPx"/> is the
/// control's on-screen rect in physical pixels; CSS px map to physical px through
/// <c>ZoomFactor x RasterizationScale</c> (the page's <c>devicePixelRatio</c>).
/// </summary>
public sealed record RoomViewport(bool Minimised, PxRect ControlPx, double ZoomFactor, double RasterizationScale)
{
    public double CssToPx => Math.Max(0.1, (double.IsFinite(ZoomFactor) ? ZoomFactor : 1) * (double.IsFinite(RasterizationScale) ? RasterizationScale : 1));
    public double CssWidth => ControlPx.W / CssToPx;
    public double CssHeight => ControlPx.H / CssToPx;
}

/// <summary>One monitor, physical pixels.</summary>
public readonly record struct FxScreenInfo(PxRect BoundsPx, bool Primary);

/// <summary>Where a gif-from grows from: the screen it plays on and the start rect, both physical px.</summary>
/// <param name="Centred">True when the page's rect was missing or unusable and the start is a centre box.</param>
public readonly record struct FxFromTarget(int ScreenIndex, PxRect ScreenPx, PxRect RectPx, bool Centred);

/// <summary>One sampled gif-from frame, in the target screen's local units.</summary>
public readonly record struct GifFromFrame(PxRect Image, double ImageAlpha, double DimAlpha);

/// <summary>The tunnel's radial gradient: transparent inside <see cref="Inner"/>, <see cref="Alpha"/> of
/// <c>rgba(6,3,12)</c> from <see cref="Outer"/> out.</summary>
public readonly record struct TunnelShape(double Inner, double Outer, double Alpha);

public static class BackRoomOverlayMath
{
    /// <summary>The start box when the page gave no usable rect (the mockup's slice box).</summary>
    public const double CentreBoxW = 60, CentreBoxH = 44;

    public const int GrowMs = 700, GifFadeMs = 900;
    /// <summary>The page behind a full-size gif-from dims under <c>rgba(8,4,14, 0.55)</c>, so it reads at 45%.</summary>
    public const double DimAlpha = 0.55;
    public const int WashRiseMs = 80;
    public const double WashDecayPerSec = 4.5;
    public const double WashPictureHeight = 0.42;
    /// <summary>
    /// The authored spiral envelope, CHOSEN BY THE OWNER 2026-09-17 with both candidates in front of
    /// them: in over 1250 ms, out over 500 ms. It blooms, then snaps away. A spiral never pops.
    ///
    /// <para>#1340 retuned these to 600 / 900 (fast in, lingering out) and grew the holds in
    /// <c>BackRoomFxPlan</c> to pay for the longer rise. That side LOST. Take ours on all four
    /// constants when that PR merges; BackRoomOverlayMathTests pins them and will conflict, by design.</para>
    ///
    /// <para>The fade-in is spent INSIDE the hold and the fade-out runs AFTER it, so a spiral's total
    /// time on screen is <c>holdMs + SpiralFadeOutMs</c> - and a brief spiral (1500 ms hold) reaches
    /// full alpha with only 250 ms of hold left. That is the accepted consequence of this envelope
    /// against main's holds, not an oversight.</para>
    /// </summary>
    public const int SpiralFadeInMs = 1250, SpiralFadeOutMs = 500;

    // ---- coordinates -----------------------------------------------------------------------------

    /// <summary>
    /// Map a page rect onto the desktop. The screen is the one holding the room control's centre (else
    /// the one it overlaps most). A missing rect, one larger than the viewport or wholly outside it, or a rect
    /// with no viewport grows from the centre of the room control; a minimised or off-screen room grows
    /// from the centre of the primary screen.
    /// </summary>
    public static FxFromTarget MapFrom(FxCssRect? css, RoomViewport? vp, IReadOnlyList<FxScreenInfo> screens)
    {
        if (screens.Count == 0) return new FxFromTarget(-1, default, default, true);
        int primary = 0;
        for (int i = 0; i < screens.Count; i++) if (screens[i].Primary) { primary = i; break; }

        int idx = vp == null || vp.Minimised || vp.ControlPx.W <= 0 || vp.ControlPx.H <= 0 ? -1 : ScreenFor(vp.ControlPx, screens);
        if (idx < 0)
        {
            var p = screens[primary].BoundsPx;
            return new FxFromTarget(primary, p, CentreBox(p.Cx, p.Cy, 1), true);
        }

        var screen = screens[idx].BoundsPx;
        double k = vp!.CssToPx;
        if (css is { } r && Usable(r, vp))
            return new FxFromTarget(idx, screen, new PxRect(vp.ControlPx.X + r.X * k, vp.ControlPx.Y + r.Y * k, r.W * k, r.H * k), false);
        return new FxFromTarget(idx, screen, CentreBox(vp.ControlPx.Cx, vp.ControlPx.Cy, k), true);
    }

    /// <summary>Finite, w and h between 8 and the viewport's own size, and at least partly inside the viewport
    /// (a rect far outside it would start the growth off the room window, maybe on another monitor).</summary>
    internal static bool Usable(FxCssRect r, RoomViewport vp)
        => double.IsFinite(r.X) && double.IsFinite(r.Y) && double.IsFinite(r.W) && double.IsFinite(r.H)
           && r.W >= BackRoomFxPlan.MinFromPx && r.H >= BackRoomFxPlan.MinFromPx
           && r.W <= vp.CssWidth + 0.5 && r.H <= vp.CssHeight + 0.5
           && new PxRect(r.X, r.Y, r.W, r.H).OverlapArea(new PxRect(0, 0, vp.CssWidth, vp.CssHeight)) > 0;

    private static PxRect CentreBox(double cx, double cy, double k)
        => new(cx - CentreBoxW * k / 2, cy - CentreBoxH * k / 2, CentreBoxW * k, CentreBoxH * k);

    internal static int ScreenFor(PxRect control, IReadOnlyList<FxScreenInfo> screens)
    {
        for (int i = 0; i < screens.Count; i++)
            if (screens[i].BoundsPx.Contains(control.Cx, control.Cy)) return i;
        int best = -1;
        double most = 0;
        for (int i = 0; i < screens.Count; i++)
        {
            double a = screens[i].BoundsPx.OverlapArea(control);
            if (a > most) { most = a; best = i; }
        }
        return best;
    }

    /// <summary>
    /// One cell per monitor, in the local DIPs of a surface over the whole virtual screen (<paramref name="virtualPx"/>)
    /// drawn at <paramref name="pxPerDip"/>: what the tunnel's vignettes and the spiral's fields are laid out in
    /// (10.14: one field per screen, centred on that screen). Physical bounds in, so a mixed-DPI desktop maps every
    /// monitor through the SAME window scale. No monitor reported = one cell over the whole surface.
    /// </summary>
    public static IReadOnlyList<PxRect> ScreenCells(IReadOnlyList<FxScreenInfo> screens, PxRect virtualPx, double pxPerDip)
    {
        var cells = new List<PxRect>(Math.Max(1, screens.Count));
        foreach (var s in screens)
            if (s.BoundsPx.W > 0 && s.BoundsPx.H > 0) cells.Add(ToLocalDip(s.BoundsPx, virtualPx, pxPerDip));
        if (cells.Count == 0) cells.Add(ToLocalDip(virtualPx, virtualPx, pxPerDip));
        return cells;
    }

    /// <summary>Physical px -> DIPs local to a surface whose top-left is <paramref name="originPx"/>.</summary>
    public static PxRect ToLocalDip(PxRect rectPx, PxRect originPx, double pxPerDip)
    {
        double s = pxPerDip > 0 && double.IsFinite(pxPerDip) ? pxPerDip : 1;
        return new PxRect((rectPx.X - originPx.X) / s, (rectPx.Y - originPx.Y) / s, rectPx.W / s, rectPx.H / s);
    }

    // ---- wash ------------------------------------------------------------------------------------

    /// <summary>0..1: linear up over 80 ms, then <c>exp(-4.5 t)</c>, and nothing from 900 ms.</summary>
    public static double WashEnvelope(double ageMs)
    {
        if (ageMs < 0 || ageMs >= BackRoomFxPlan.WashMs) return 0;
        if (ageMs < WashRiseMs) return ageMs / WashRiseMs;
        return Math.Exp(-(ageMs - WashRiseMs) / 1000.0 * WashDecayPerSec);
    }

    /// <summary>The picture in the middle of a wash: <c>min(1, env x 1.3) x 0.85 x strength</c>, where the
    /// resolved peak is <c>0.42 x strength</c>.</summary>
    public static double WashPictureAlpha(double envelope, double peak)
        => Math.Min(1, envelope * 1.3) * 0.85 * Math.Clamp(peak / BackRoomFxPlan.WashPeak, 0, 1);

    /// <summary>The picture's box: 42% of the screen height, 4:3, centred (the image covers it).</summary>
    public static PxRect WashPictureBox(double w, double h)
    {
        double bh = h * WashPictureHeight, bw = bh * 4 / 3;
        return new PxRect((w - bw) / 2, (h - bh) / 2, bw, bh);
    }

    // ---- gif-from --------------------------------------------------------------------------------

    public static double EaseInOut(double p)
    {
        p = Math.Clamp(p, 0, 1);
        return p < 0.5 ? 2 * p * p : 1 - Math.Pow(-2 * p + 2, 2) / 2;
    }

    /// <summary>
    /// The mockup's fullscreen GIF: the image lerps from <paramref name="from"/> to a centred box that
    /// covers the screen (<c>max(W, H x aspect) x scale</c> wide) over 700 ms, ease in-out, and fades over
    /// the last 900 ms. Behind it the screen dims under 0.55 black, rising with the growth, unless
    /// <paramref name="scale"/> is below 1 (it then sits inside a running spiral).
    /// </summary>
    public static GifFromFrame GifFrom(double ageMs, int durationMs, PxRect from, double w, double h, double aspect,
        double scale, double dimLevel)
    {
        if (ageMs < 0 || ageMs >= durationMs) return new GifFromFrame(from, 0, 0);
        double ar = aspect > 0.05 && double.IsFinite(aspect) ? aspect : 4.0 / 3;
        double tw = Math.Max(w, h * ar) * scale, th = tw / ar;
        var cover = new PxRect((w - tw) / 2, (h - th) / 2, tw, th);

        double grow = EaseInOut(ageMs / GrowMs);
        double fade = ageMs > durationMs - GifFadeMs ? Math.Max(0, (durationMs - ageMs) / GifFadeMs) : 1;
        var image = new PxRect(Lerp(from.X, cover.X, grow), Lerp(from.Y, cover.Y, grow), Lerp(from.W, cover.W, grow), Lerp(from.H, cover.H, grow));
        double dim = scale >= 1 ? DimAlpha * fade * (0.55 + 0.45 * grow) * dimLevel : 0;
        return new GifFromFrame(image, fade, dim);
    }

    // ---- spiral-loom -----------------------------------------------------------------------------

    /// <summary>0..1 before the step's alpha: in over 1250 ms, out over 500 ms from the hold's end or
    /// from a release, whichever comes first.</summary>
    public static double SpiralEnvelope(double ageMs, int holdMs, double? releasedAtAgeMs)
    {
        if (ageMs < 0) return 0;
        double a = Math.Min(1, ageMs / SpiralFadeInMs);
        a = a * a * (3 - 2 * a);
        double outFrom = releasedAtAgeMs is { } r ? Math.Min(r, holdMs) : holdMs;
        if (ageMs > outFrom) a *= 1 - Math.Min(1, (ageMs - outFrom) / SpiralFadeOutMs);
        return Math.Max(0, a);
    }

    public static bool SpiralDone(double ageMs, int holdMs, double? releasedAtAgeMs)
        => ageMs >= (releasedAtAgeMs is { } r ? Math.Min(r, holdMs) : holdMs) + SpiralFadeOutMs;

    // ---- tunnel ----------------------------------------------------------------------------------

    /// <summary>Per screen, <c>R = hypot(w, h) / 2</c>: clear inside <c>R(1 - 0.72k)</c>, reaching
    /// <c>0.94 x min(1, 1.3k)</c> at <c>R(1.12 - 0.5k)</c>. The centre never closes.</summary>
    public static TunnelShape Tunnel(double w, double h, double k)
    {
        k = Math.Clamp(k, 0, 1);
        double r = Math.Sqrt(w * w + h * h) / 2;
        return new TunnelShape(r * (1 - 0.72 * k), r * (1.12 - 0.5 * k), 0.94 * Math.Min(1, 1.3 * k));
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

/// <summary>
/// The tunnel's level over time (one per room, drawn on every screen). It eases toward the wanted level
/// at 1.4/s closing and 2.2/s opening (the mockup's exponential approach) and lets go by itself when
/// nobody refreshed the want for 1500 ms.
/// </summary>
public sealed class TunnelModel
{
    public const double CloseRate = 1.4, OpenRate = 2.2;
    public const int StaleMs = 1500;
    /// <summary>Below this the tunnel is not drawn (and snaps to 0 once nothing wants it).</summary>
    public const double Epsilon = 0.01;

    private long _refreshedAt;

    public double Want { get; private set; }
    public double Level { get; private set; }
    public bool Idle => Want <= 0 && Level <= 0;

    public void Set(double level, long nowMs)
    {
        Want = double.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0;
        _refreshedAt = nowMs;
    }

    /// <summary>Gone at once (suspend, close, exit, station-close).</summary>
    public void Cancel() { Want = 0; Level = 0; }

    public double Step(long nowMs, double dtMs)
    {
        if (Want > 0 && nowMs - _refreshedAt > StaleMs) Want = 0;
        double rate = Want > Level ? CloseRate : OpenRate;
        Level += (Want - Level) * Math.Min(1, Math.Max(0, dtMs) / 1000 * rate);
        if (Want <= 0 && Level < Epsilon) Level = 0;
        return Level;
    }
}
