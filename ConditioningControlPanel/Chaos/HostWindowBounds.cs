using System;
using System.Collections.Generic;
using System.Windows;

namespace ConditioningControlPanel;

/// <summary>
/// The rectangle maths behind <see cref="ChaosWebViewHost"/>'s window modes, kept WPF-window-free
/// so mixed-DPI placement can be tested without opening anything.
///
/// <para><b>Why it exists.</b> The fullscreen branch used to hardcode
/// <c>Left = 0, Top = 0, Width/Height = SystemParameters.PrimaryScreenWidth/Height</c>, so every
/// game window on the shared host jumped to the PRIMARY monitor whatever screen the player had it
/// on (ccp-bugs #1239). The reporter's desk is the awkward class: a 1440p primary with two 1080p
/// sides, where the primary's size is not even the right size for the monitor the window is on.</para>
///
/// <para><b>Units.</b> Monitor bounds arrive in PHYSICAL pixels (that is what
/// <c>System.Windows.Forms.Screen.Bounds</c> reports); WPF's <c>Window.Left/Top/Width/Height</c> are
/// DIPs. Dividing by that monitor's own scale is only exact while the window already lives there,
/// which is why the caller pins the frame with a physical-pixel <c>SetWindowPos</c> once the HWND
/// exists - the same two-step <c>BrowserVideoWindow</c> uses.</para>
/// </summary>
internal static class HostWindowBounds
{
    /// <summary>Which window's monitor a placement should follow.</summary>
    internal enum ScreenChoice
    {
        /// <summary>The host's own window: it is already on screen, so it owns the answer.</summary>
        Self,

        /// <summary>The window the game was started from (the launcher, or the control panel).
        /// The only honest answer before our own HWND exists.</summary>
        Owner,

        /// <summary>Nothing to follow. The historic behaviour, now the last resort.</summary>
        Primary,
    }

    /// <summary>
    /// The monitor this placement follows. Our own window wins as soon as it has been realized -
    /// that is the fullscreen TOGGLE, where the user has already put the window somewhere. Before
    /// that (the window is built and laid out before <c>Show</c>) the only thing that knows where
    /// the player is looking is whatever launched the game.
    /// </summary>
    internal static ScreenChoice ChooseScreen(bool ownWindowRealized, bool ownerUsable)
        => ownWindowRealized ? ScreenChoice.Self
         : ownerUsable ? ScreenChoice.Owner
         : ScreenChoice.Primary;

    /// <summary>A scale we can divide by. A monitor probe that fails reads as 100%, never as 0.</summary>
    internal static double SafeScale(double dpiScale)
        => dpiScale > 0 && !double.IsNaN(dpiScale) && !double.IsInfinity(dpiScale) ? dpiScale : 1.0;

    /// <summary>The whole monitor, in DIPs: borderless fullscreen covers the taskbar too, so these
    /// are the screen's BOUNDS and never its working area.</summary>
    internal static Rect Fullscreen(Rect physicalBounds, double dpiScale)
    {
        var s = SafeScale(dpiScale);
        return new Rect(physicalBounds.X / s, physicalBounds.Y / s,
                        physicalBounds.Width / s, physicalBounds.Height / s);
    }

    /// <summary>
    /// True when <paramref name="frame"/> still overlaps at least one live monitor, i.e. putting a
    /// window back on it would put it somewhere the user can reach.
    ///
    /// <para>Both sides are DIPs. The frame is one the host remembered before it went fullscreen,
    /// and a monitor can go away in between - undock, unplug, an RDP reconnect with a different
    /// layout - which is how a faithful restore hands back a window that is nowhere on the desk. A
    /// frame with no size, and a list with no screens in it, are not on screen either: both answer
    /// false, and the caller's fallback (re-centring) is what this path did before it remembered
    /// any frame at all.</para>
    /// </summary>
    internal static bool IntersectsAnyScreen(Rect frame, IReadOnlyList<Rect> screensInDips)
    {
        if (screensInDips == null || screensInDips.Count == 0) return false;
        if (double.IsNaN(frame.Left) || double.IsNaN(frame.Top)
            || double.IsNaN(frame.Width) || double.IsNaN(frame.Height)) return false;
        if (frame.Width <= 0 || frame.Height <= 0) return false;

        foreach (var s in screensInDips)
        {
            if (s.Width <= 0 || s.Height <= 0) continue;
            // A shared EDGE is not an overlap: a window whose right edge is exactly the next
            // monitor's left edge has no pixel on it. Hence the strict compares rather than
            // Rect.IntersectsWith, which counts touching rectangles as intersecting.
            if (frame.Left < s.Right && s.Left < frame.Right
                && frame.Top < s.Bottom && s.Top < frame.Bottom) return true;
        }
        return false;
    }

    /// <summary>
    /// A <paramref name="widthDip"/> x <paramref name="heightDip"/> frame centred on that monitor,
    /// in DIPs. The size is clamped to the monitor first, so the historic "85% of the PRIMARY
    /// screen" default cannot hang a 1440p-sized window off the side of a 1080p one.
    /// </summary>
    internal static Rect Centred(Rect physicalBounds, double dpiScale, double widthDip, double heightDip)
    {
        var screen = Fullscreen(physicalBounds, dpiScale);
        double w = Math.Min(widthDip, screen.Width);
        double h = Math.Min(heightDip, screen.Height);
        if (w <= 0) w = screen.Width;
        if (h <= 0) h = screen.Height;
        return new Rect(screen.X + (screen.Width - w) / 2, screen.Y + (screen.Height - h) / 2, w, h);
    }
}
