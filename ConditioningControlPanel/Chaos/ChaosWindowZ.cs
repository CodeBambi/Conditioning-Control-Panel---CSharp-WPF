using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ConditioningControlPanel;

/// <summary>
/// Shared z-order helper for the chaos layer's keep-alive windows. These windows are born
/// Topmost once and then only hide/unhide (the render-deadlock contract: no layered-window
/// churn mid-run) — but un-hiding does NOT move a window in the z-order, so once a mandatory
/// video (a fresh fullscreen topmost window) lands on top, a re-shown overlay stays buried
/// behind it for the video's whole duration. Two callers fix that:
///  - each overlay's show/unhide path calls <see cref="RaiseAboveVideo"/> so anything that
///    first draws mid-video pops above it, and
///  - ChaosModeService.RaiseGameLayerAboveVideo lifts the whole layer when a video starts
///    or is clicked (a click activates the video window at the Win32 level — WPF's
///    e.Handled only stops the routed event — which yanks it back above everything).
/// </summary>
internal static class ChaosWindowZ
{
    /// <summary>Set true for the duration of a Free Desktop run (see ChaosPlayMode). While true the
    /// chaos windows are born NON-topmost (<see cref="BornTopmost"/>) and <see cref="RaiseTopmost"/>
    /// actively demotes instead of pinning, so the player can bring any other window to the front.</summary>
    public static bool DesktopMode;

    /// <summary>Whether the chaos layer pins itself topmost (default). Decoupled from
    /// <see cref="DesktopMode"/> so a Free Desktop run can still keep its other traits (avatar
    /// visible, no mandatory video) while staying pinned above other apps. Driven by
    /// AppSettings.ChaosPinOnTop; set in ChaosModeService.StartRun before any window is created.</summary>
    public static bool PinTopmost = true;

    /// <summary>What a chaos window's <c>Topmost</c> should be at birth: true when the layer is
    /// pinned (the default), false when the player has opted to let other apps sit in front.</summary>
    public static bool BornTopmost => PinTopmost;

    /// <summary>Re-assert a window to the top of the topmost band without stealing focus. When the
    /// layer isn't pinned this instead demotes the window out of the topmost band so other apps
    /// can come forward.</summary>
    public static void RaiseTopmost(Window? w)
    {
        if (w == null) return;
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (!PinTopmost)
            {
                try { w.Topmost = false; } catch { }
                SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                return;
            }
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch { }
    }

    /// <summary>
    /// <see cref="RaiseTopmost"/>, but only while a mandatory video is on screen — the only
    /// time an unhidden keep-alive window can find itself buried. No-op otherwise.
    /// </summary>
    public static void RaiseAboveVideo(Window? w)
    {
        if (App.Video?.IsPlaying != true) return;
        RaiseTopmost(w);
    }

    /// <summary>
    /// Pin a window to the top of the topmost band UNCONDITIONALLY (ignores <see cref="PinTopmost"/>).
    /// Used by the dashboard trigger-bubble overlays (glitch/cascade) which fire OUTSIDE any chaos
    /// run: there the run's pin state doesn't apply, and the keep-alive overlay singleton may carry a
    /// stale Topmost=false from a prior Free-Desktop run, leaving the wash buried behind the app.
    /// </summary>
    public static void ForceTopmost(Window? w)
    {
        if (w == null) return;
        try
        {
            w.Topmost = true;
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch { }
    }

    /// <summary>
    /// The bounds (DIPs) a full-screen chaos overlay should cover. The Rabbit Hole is a
    /// single-monitor experience: with multi-monitor OFF every spanning overlay must confine to
    /// the PRIMARY screen. Sizing these to the whole VIRTUAL desktop (as they used to) stretched
    /// a layered GIF/flash surface across every monitor — so the cascade/flashes showed up on all
    /// screens AND the app paid DWM composition for the full virtual span (the all-screen-flash
    /// perf hit). With multi-monitor ON we keep the virtual span so effects fill every screen.
    /// Read at window construction only — never resize a realized layered window mid-run (deadlock).
    /// </summary>
    public static (double left, double top, double width, double height) StageBounds()
    {
        bool dual = App.Settings?.Current?.DualMonitorEnabled ?? true;
        if (dual)
            return (SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        // Primary only: WPF places the primary monitor's top-left at (0,0) in DIP space.
        return (0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
