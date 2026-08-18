using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ConditioningControlPanel
{
    // ============================================================================================
    // HIGH-DPI WORK-AREA FIT
    // ============================================================================================
    // The window ships at a fixed 1563x943 DIP with 1131x620 floors (MainWindow.xaml). Those numbers
    // were tuned against 100-150% scaling on a desktop monitor. On a TV at 300% the work area
    // collapses to a third of its pixel size in DIP space (a 4K panel is 1280x672 DIP once the
    // taskbar is out; a 1080p panel is 640x312), so the window is taller than the screen and the
    // bottom bar - START, the status strip, the resize grip - is pushed off the desktop with no way
    // to drag it back. The community workaround was a per-app DPI override; this makes the app do it.
    //
    // Two things had to change for the clamp to actually bite:
    //   1. It has to measure the monitor the window is ON, not the primary. CenterOnPrimaryScreen
    //      only ever looked at Screen.PrimaryScreen, which is the wrong panel the moment the TV is
    //      the secondary display - and its DPI is wrong too under PerMonitorV2 (app.manifest).
    //   2. MinWidth/MinHeight have to yield. WPF enforces them over any size we ask for, so at 300%
    //      the old clamp computed a correct 672 DIP height and WPF handed back 620 - and against a
    //      640 DIP wide 1080p panel it handed back the full 1131 DIP of width. The floors are
    //      relaxed to the work area first, and restored to their design values once there is room.
    //
    // Shrinking is visually safe: the whole UI is a Viewbox with Stretch="Fill" over a fixed
    // 1585x901 design canvas (MainWindow.xaml), so a smaller window scales the content down rather
    // than clipping it. No scrollbars, no cut-off controls - just smaller.
    //
    // The move/resize itself runs in PHYSICAL pixels through SetWindowPos rather than Window
    // Left/Top/Width/Height. Under PerMonitorV2 WPF's DIP space is anchored per-window and is
    // non-linear across a mixed-DPI virtual desktop (the same reason the avatar easter egg drives
    // itself with SetWindowPos - AvatarTubeWindow.Windowing.cs), whereas Screen.WorkingArea is
    // already physical virtual-desktop pixels. Staying in that one space removes the conversion.
    public partial class MainWindow
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out FitRect lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct FitRect { public int Left, Top, Right, Bottom; }

        private const uint SWP_NOZORDER = 0x0004;

        // The XAML floors, captured before we ever lower them so they can be restored.
        private double _designMinWidth = double.NaN;
        private double _designMinHeight = double.NaN;

        // Re-entrancy guard: relaxing MinWidth/MinHeight and SetWindowPos both raise size/location
        // changes that could route back here.
        private bool _workAreaFitInProgress;

        private void EnsureDesignFloorsCaptured()
        {
            if (double.IsNaN(_designMinWidth))
            {
                _designMinWidth = MinWidth;
                _designMinHeight = MinHeight;
            }
        }

        /// <summary>
        /// Lowers MinWidth/MinHeight so they cannot out-vote a work area smaller than the design
        /// floors, and raises them back toward the XAML values when the screen can take it.
        /// Sizes are DIPs, the space MinWidth/MinHeight themselves live in.
        /// </summary>
        private void RelaxSizeFloorsTo(double maxWidthDip, double maxHeightDip)
        {
            EnsureDesignFloorsCaptured();
            if (maxWidthDip > 0)
            {
                // 200 DIP is a sanity floor: below that the window stops being a window. Nothing
                // real reaches it (that would be a work area under 600px at 300%).
                var w = Math.Max(200, Math.Min(_designMinWidth, maxWidthDip));
                if (Math.Abs(MinWidth - w) > 0.5) MinWidth = w;
            }
            if (maxHeightDip > 0)
            {
                var h = Math.Max(200, Math.Min(_designMinHeight, maxHeightDip));
                if (Math.Abs(MinHeight - h) > 0.5) MinHeight = h;
            }
        }

        /// <summary>
        /// The monitor this window is actually on. Null when screen enumeration is unavailable
        /// (it can legitimately come back empty during display-topology churn / session switches) -
        /// callers must treat null as "don't touch the window" rather than guessing at primary.
        /// </summary>
        private static System.Windows.Forms.Screen? ResolveMonitorForWindow(IntPtr hwnd)
        {
            try
            {
                var screens = App.GetAllScreensCached();
                if (screens.Length == 0) return null;
                // Screen.FromHandle is MonitorFromWindow(MONITOR_DEFAULTTONEAREST) - the same
                // monitor Windows itself considers the window to live on.
                return System.Windows.Forms.Screen.FromHandle(hwnd)
                       ?? System.Windows.Forms.Screen.PrimaryScreen
                       ?? screens[0];
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ResolveMonitorForWindow failed: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Clamps the window to the work area of the monitor it is on and nudges it back on-screen.
        /// Safe to call repeatedly; a no-op when the window already fits.
        /// </summary>
        /// <param name="reason">Log tag for the call site.</param>
        /// <param name="dpiOverride">
        /// The DPI to convert with. Pass the value handed to OnDpiChanged: mid-transition
        /// VisualTreeHelper.GetDpi can still report the old scale.
        /// </param>
        private void FitToCurrentMonitorWorkArea(string reason, DpiScale? dpiOverride = null)
        {
            if (_workAreaFitInProgress) return;
            try
            {
                // Maximized/minimized windows are sized by the shell against this same work area,
                // and full-screen mode is deliberately larger than it. Only Normal is ours to clamp.
                if (WindowState != WindowState.Normal) return;

                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                var screen = ResolveMonitorForWindow(hwnd);
                if (screen == null) return;
                var wa = screen.WorkingArea;
                if (wa.Width <= 0 || wa.Height <= 0) return;

                var dpi = dpiOverride ?? VisualTreeHelper.GetDpi(this);
                var sx = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
                var sy = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

                _workAreaFitInProgress = true;

                // Programmatic size changes go through Manual - an auto-sizing window fighting an
                // explicit resize is the documented crash-on-resize shape in this codebase.
                if (SizeToContent != SizeToContent.Manual) SizeToContent = SizeToContent.Manual;

                // Step 1: let the floors down to the work area so step 2's resize is not vetoed.
                RelaxSizeFloorsTo(wa.Width / sx, wa.Height / sy);

                // Step 2: clamp size, then nudge fully on-screen - all in physical pixels.
                if (!GetWindowRect(hwnd, out var r)) return;
                var w = Math.Max(1, r.Right - r.Left);
                var h = Math.Max(1, r.Bottom - r.Top);
                var newW = Math.Min(w, wa.Width);
                var newH = Math.Min(h, wa.Height);
                var newL = r.Left;
                var newT = r.Top;
                if (newL + newW > wa.Right) newL = wa.Right - newW;
                if (newT + newH > wa.Bottom) newT = wa.Bottom - newH;
                // Left/Top last: if the window still cannot fit (a floor we refused to go under),
                // the two corrections disagree, and keeping the TOP-LEFT corner on-screen is what
                // keeps the title bar draggable and the window recoverable.
                if (newL < wa.Left) newL = wa.Left;
                if (newT < wa.Top) newT = wa.Top;

                if (newW == w && newH == h && newL == r.Left && newT == r.Top) return;

                SetWindowPos(hwnd, IntPtr.Zero, newL, newT, newW, newH, SWP_NOZORDER | SWP_NOACTIVATE);

                // Information, not Debug: Serilog's field floor is Information, and this is the one
                // line that explains a window that came up smaller than the user expected.
                App.Logger?.Information(
                    "MainWindow work-area fit ({Reason}): {W}x{H}@{L},{T} -> {NW}x{NH}@{NL},{NT}; " +
                    "work area {WW}x{WH} on {Device} at {Sx:0.##}x/{Sy:0.##}x, floors now {MinW:0}x{MinH:0} DIP",
                    reason, w, h, r.Left, r.Top, newW, newH, newL, newT,
                    wa.Width, wa.Height, screen.DeviceName, sx, sy, MinWidth, MinHeight);
            }
            catch (Exception ex)
            {
                // Never let a placement tweak take the window down.
                App.Logger?.Warning(ex, "MainWindow work-area fit failed ({Reason})", reason);
            }
            finally
            {
                _workAreaFitInProgress = false;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // First point at which there is an HWND, so the first point at which we can ask which
            // monitor we are on and what its DPI is. Runs before the window is painted, so an
            // oversized window is corrected without ever flashing at the wrong size.
            try
            {
                EnsureDesignFloorsCaptured();
                FitToCurrentMonitorWorkArea("source-initialized");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "OnSourceInitialized work-area fit failed");
            }
        }

        /// <summary>
        /// Re-fit after a DPI change. Dispatched rather than run inline: WM_DPICHANGED hands WPF a
        /// suggested rect that it applies around this callback, so measuring mid-transition reads a
        /// size that is about to be overwritten.
        /// </summary>
        private void QueueWorkAreaFitAfterDpiChange(DpiScale newDpi)
        {
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    try
                    {
                        if (Dispatcher.HasShutdownStarted || !IsLoaded) return;
                        FitToCurrentMonitorWorkArea("dpi-changed", newDpi);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Deferred DPI work-area fit failed: {Error}", ex.Message);
                    }
                }));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Could not queue DPI work-area fit: {Error}", ex.Message);
            }
        }
    }
}
