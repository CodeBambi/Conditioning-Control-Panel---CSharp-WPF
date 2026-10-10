// PORTED from ConditioningControlPanel/Windows/Friends/LandingChrome.cs (7.1.5): the few pieces the
// landing windows share. Colours, fonts, pills and the avatar disc come from FriendsLook (the drawer's
// palette, same values); this file adds what a landing WINDOW needs: the dress (borderless, transparent,
// off the taskbar, never activated), the passive placement, and the anchor's rectangle in device pixels.
//
// Focus rule (WPF Helpers.PassiveToastWindow + the owned-window z-order hook): a landing window is
// UNOWNED, so showing it can never lift the panel's window chain over the app the player is in, and it
// is never activated: ShowActivated = false everywhere, WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW on Windows
// and override-redirect on X11, both through X11Overlay.SetOverrideRedirect(passive) (the same door the
// achievement toast uses; it is guarded per platform inside). Wayland / headless: placed only.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Views.Controls.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Friends;

internal static class LandingChrome
{
    /// <summary>WPF LandingChrome.Dress: Topmost = true. A notice has to show over a game window; it
    /// still never activates. One switch, so the owner can turn it off without touching the windows.</summary>
    internal const bool Topmost = true;

    /// <summary>The ground of a card (WPF LandingChrome.Ground, the drawer's menu violet).</summary>
    public static readonly Color Ground = Color.FromRgb(0x22, 0x16, 0x41);

    /// <summary>Room around a card for its BoxShadow (the window is transparent; no Effect anywhere).</summary>
    internal const double ShadowRoom = 16;

    public static IBrush Brush(Color c, byte alpha = 255) => FriendsLook.Frozen(Color.FromArgb(alpha, c.R, c.G, c.B));

    /// <summary>A borderless, transparent, unowned window that never takes focus.</summary>
    public static void Dress(Window w)
    {
        w.WindowDecorations = WindowDecorations.None;
        w.TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        w.Background = Brushes.Transparent;
        w.ShowInTaskbar = false;
        w.ShowActivated = false;
        w.Topmost = Topmost;
        w.CanResize = false;
        w.Focusable = false;
        w.WindowStartupLocation = WindowStartupLocation.Manual;
    }

    /// <summary>A round avatar: the drawer's disc (initials on the name's gradient, the picture once it loads).</summary>
    public static Control Avatar(string name, string? avatarPath, double size)
    {
        var a = FriendsLook.Avatar(name, size, null, avatarPath);
        a.VerticalAlignment = VerticalAlignment.Top;
        return a;
    }

    /// <summary>A resource picture under Resources/ ("features/backroom.png"), or null when it is not there.</summary>
    public static Bitmap? Picture(string resourcePath)
    {
        try { return Helpers.ModArt.TryLoad(resourcePath, 480); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] picture {P}: {E}", resourcePath, ex.Message); return null; }
    }

    /// <summary>The scale and work area of the screen <paramref name="anchor"/> is on (the primary one
    /// without an anchor). Null when the platform lists no screen (headless).</summary>
    public static (PixelRect Work, double Scale)? ScreenOf(Window self, Window? anchor)
    {
        try
        {
            var screen = (anchor is { IsVisible: true } ? self.Screens.ScreenFromWindow(anchor) : null) ?? self.Screens.Primary;
            if (screen == null) return null;
            return (screen.WorkingArea, screen.Scaling > 0 ? screen.Scaling : 1);
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] screen: {E}", ex.Message); return null; }
    }

    /// <summary>The anchor's client rectangle in device pixels. Works for a maximised window too.</summary>
    public static PixelRect AnchorRect(Window anchor)
    {
        var k = anchor.DesktopScaling > 0 ? anchor.DesktopScaling : 1;
        var size = anchor.ClientSize;
        return new PixelRect(anchor.Position.X, anchor.Position.Y,
            (int)Math.Round(Math.Max(size.Width, 400) * k), (int)Math.Round(Math.Max(size.Height, 300) * k));
    }

    /// <summary>Sizes the window to <paramref name="dip"/> and puts its top-left at <paramref name="at"/>
    /// (device pixels), passively: never activated, off the taskbar, above a game. Safe to call again
    /// whenever the content grows or the stack moves.</summary>
    public static void PlacePassive(Window w, PixelPoint at, Size dip, double scale)
    {
        try
        {
            var rect = new PixelRect(at.X, at.Y, (int)Math.Ceiling(dip.Width * scale), (int)Math.Ceiling(dip.Height * scale));
            if (!Platform.X11Overlay.SetOverrideRedirect(w, rect, passive: true))
                Serilog.Log.Debug("[Friends] {Window}: no passive style on this platform; placed only", w.GetType().Name);
            // SetOverrideRedirect sizes by the window's own scaling, which before Show() may still read 1.
            w.Width = dip.Width;
            w.Height = dip.Height;
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] place: {E}", ex.Message); }
    }

    /// <summary>What the content wants, measured unconstrained.</summary>
    public static Size Want(Control content)
    {
        content.Measure(Size.Infinity);
        var d = content.DesiredSize;
        return new Size(Math.Max(1, Math.Ceiling(d.Width)), Math.Max(1, Math.Ceiling(d.Height)));
    }
}
