using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Games.BackRoom;

/// <summary>
/// The room's web view as the host sees it at play time (WPF Overlays.RoomViewport).
/// <see cref="ControlPx"/> is the control's on-screen rect in physical pixels; CSS px reach physical
/// px through <see cref="CssToPx"/> (page zoom times the scale the engine rasterises at).
/// </summary>
internal sealed record RoomViewport(bool Minimised, Rect ControlPx, double CssToPx)
{
    public double K => double.IsFinite(CssToPx) ? Math.Max(0.1, CssToPx) : 1;
    public double CssWidth => ControlPx.Width / K;
    public double CssHeight => ControlPx.Height / K;
}

/// <summary>Where a gif-from grows from: the screen it plays on and the start rect, physical px
/// (WPF FxFromTarget). <paramref name="Centred"/> = the page's rect was missing or unusable.</summary>
internal readonly record struct RoomFromTarget(int ScreenIndex, Rect ScreenPx, Rect RectPx, bool Centred);

/// <summary>WPF BackRoomOverlayMath.MapFrom, same rules and numbers.</summary>
internal static class BackRoomFromMap
{
    /// <summary>
    /// Map a page rect onto the desktop. The screen is the one holding the room control's centre (else
    /// the one it overlaps most). A missing rect, one larger than the viewport or wholly outside it
    /// grows from the centre of the room control; no viewport, a minimised or an off-screen room grows
    /// from the centre of the primary screen.
    /// </summary>
    public static RoomFromTarget MapFrom(FxCssRect? css, RoomViewport? vp, IReadOnlyList<(Rect BoundsPx, bool Primary)> screens)
    {
        if (screens.Count == 0) return new RoomFromTarget(-1, default, default, true);
        int primary = 0;
        for (int i = 0; i < screens.Count; i++) if (screens[i].Primary) { primary = i; break; }

        int idx = vp == null || vp.Minimised || vp.ControlPx.Width <= 0 || vp.ControlPx.Height <= 0 ? -1 : ScreenFor(vp.ControlPx, screens);
        if (idx < 0)
        {
            var p = screens[primary].BoundsPx;
            return new RoomFromTarget(primary, p, CentreBox(p.Center, 1), true);
        }

        var screen = screens[idx].BoundsPx;
        double k = vp!.K;
        if (css is { } r && Usable(r, vp))
            return new RoomFromTarget(idx, screen, new Rect(vp.ControlPx.X + r.X * k, vp.ControlPx.Y + r.Y * k, r.W * k, r.H * k), false);
        return new RoomFromTarget(idx, screen, CentreBox(vp.ControlPx.Center, k), true);
    }

    /// <summary>Finite, w and h between 8 and the viewport's own size, and at least partly inside the viewport
    /// (a rect far outside it would start the growth off the room window, maybe on another monitor).</summary>
    internal static bool Usable(FxCssRect r, RoomViewport vp)
        => double.IsFinite(r.X) && double.IsFinite(r.Y) && double.IsFinite(r.W) && double.IsFinite(r.H)
           && r.W >= BackRoomFxArgs.MinFromPx && r.H >= BackRoomFxArgs.MinFromPx
           && r.W <= vp.CssWidth + 0.5 && r.H <= vp.CssHeight + 0.5
           && Overlap(new Rect(r.X, r.Y, r.W, r.H), new Rect(0, 0, vp.CssWidth, vp.CssHeight)) > 0;

    private static Rect CentreBox(Point c, double k)
        => new(c.X - RoomOverlayMath.CentreBoxW * k / 2, c.Y - RoomOverlayMath.CentreBoxH * k / 2, RoomOverlayMath.CentreBoxW * k, RoomOverlayMath.CentreBoxH * k);

    private static double Overlap(Rect a, Rect b)
    {
        double w = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        double h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        return w > 0 && h > 0 ? w * h : 0;
    }

    internal static int ScreenFor(Rect control, IReadOnlyList<(Rect BoundsPx, bool Primary)> screens)
    {
        for (int i = 0; i < screens.Count; i++)
            if (screens[i].BoundsPx.Contains(control.Center)) return i;
        int best = -1;
        double most = 0;
        for (int i = 0; i < screens.Count; i++)
        {
            double a = Overlap(screens[i].BoundsPx, control);
            if (a > most) { most = a; best = i; }
        }
        return best;
    }

    /// <summary>
    /// The viewport of a room hosted in <paramref name="host"/> (WPF BackRoomHostService.ReadViewport), UI
    /// thread. The control's corners go through <c>PointToScreen</c>, so the monitor scale is measured, not
    /// assumed. The page zoom is taken as 1: WebHost does not expose the engine's zoom factor, so a room the
    /// player zoomed with Ctrl+wheel starts its picture from a slightly wrong place (never off the window:
    /// <see cref="Usable"/> still bounds the rect to the viewport).
    /// </summary>
    internal static RoomViewport? Read(Visual? host)
    {
        try
        {
            if (host is not GameWindow w) return null;
            var web = w.Web;
            if (w.WindowState == WindowState.Minimized || !w.IsVisible || !web.IsVisible || web.Bounds.Width < 1 || web.Bounds.Height < 1)
                return new RoomViewport(true, default, 1);
            var tl = web.PointToScreen(new Point(0, 0));
            var br = web.PointToScreen(new Point(web.Bounds.Width, web.Bounds.Height));
            double px = br.X - tl.X, py = br.Y - tl.Y;
            if (px <= 0 || py <= 0) return new RoomViewport(true, default, 1);
            return new RoomViewport(false, new Rect(tl.X, tl.Y, px, py), px / web.Bounds.Width);
        }
        catch { return null; }
    }
}
