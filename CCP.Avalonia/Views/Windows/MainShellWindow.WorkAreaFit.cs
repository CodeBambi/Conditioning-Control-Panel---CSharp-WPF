// PORTED from ConditioningControlPanel/MainWindow/MainWindow.WorkAreaFit.cs (297 lines).
//
// This is the one Win32-heavy partial that maps CLEANLY, so it is a real port rather than a stub.
// The problem it solves is unchanged on Linux: the window ships at a fixed 1563x943 DIP with
// 1131x620 floors, and on a TV at 300% scaling the work area is a third of that in DIP space, so
// the bottom bar (START, the status strip) lands off the desktop with no way to drag it back.
// Shrinking is visually safe because the whole UI is a Viewbox with Stretch="Fill" over a fixed
// 1585x901 design canvas: a smaller window scales the content down rather than clipping it.
//
// Win32 -> Avalonia, per the port's mapping table:
//   Screen.FromHandle / MonitorFromWindow -> Screens.ScreenFromWindow(this) ?? Screens.Primary
//   Screen.WorkingArea (physical px)       -> screen.WorkingArea (PixelRect, physical px)
//   VisualTreeHelper.GetDpi / DpiScale     -> screen.Scaling (one factor, not x/y - X11 and
//                                             Wayland both report a single scale)
//   GetWindowRect + SetWindowPos           -> Position (PixelPoint) + Width/Height. Avalonia's
//                                             Position IS physical-pixel screen space, which is
//                                             exactly the space the WPF version dropped to
//                                             SetWindowPos for, so the conversion the comment in
//                                             the original warns about disappears.
//   OnSourceInitialized                    -> OnOpened (the first point at which there is a
//                                             platform window to ask which screen it is on)
//   OnDpiChanged + dispatcher coalescing   -> ScalingChanged. There is no modal move loop and no
//                                             WM_DPICHANGED ping-pong to break here, so the
//                                             coalescing, the drag deferral and the 4-fit burst cap
//                                             are all dropped; the idempotence early-out below is
//                                             what stops a loop.
//
// Members dropped: RunWorkAreaFitDeferredByMove / QueueWorkAreaFitAfterDpiChange /
// RunQueuedWorkAreaFit and their five coalescing fields - all of them exist only to tame
// WM_DPICHANGED during an interactive move, which is a Win32-specific hazard.

using System;
using Avalonia;
using Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // The XAML floors, captured before we ever lower them so they can be restored.
        private double _designMinWidth = double.NaN;
        private double _designMinHeight = double.NaN;

        // Re-entrancy guard: relaxing MinWidth/MinHeight and moving the window both raise
        // size/position changes that could route back here.
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
                // 200 DIP is a sanity floor: below that the window stops being a window.
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
        /// Clamps the window to the work area of the monitor it is on and nudges it back on-screen.
        /// Safe to call repeatedly; a no-op when the window already fits.
        /// </summary>
        private void FitToCurrentMonitorWorkArea(string reason)
        {
            if (_workAreaFitInProgress) return;
            try
            {
                // Maximized/minimized windows are sized by the shell against this same work area,
                // and full-screen mode is deliberately larger than it. Only Normal is ours to clamp.
                if (WindowState != WindowState.Normal) return;

                // Null during display-topology churn: treat it as "don't touch the window" rather
                // than guessing at primary, exactly as the WPF original does.
                var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
                if (screen is null) return;

                var wa = screen.WorkingArea;
                if (wa.Width <= 0 || wa.Height <= 0) return;

                var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;

                _workAreaFitInProgress = true;

                // Step 1: let the floors down to the work area so step 2's resize is not vetoed.
                RelaxSizeFloorsTo(wa.Width / scale, wa.Height / scale);

                // Step 2: clamp size, then nudge fully on-screen - all in physical pixels, which
                // is the space both WorkingArea and Position already live in.
                var pos = Position;
                var w = Math.Max(1, (int)Math.Round(Width * scale));
                var h = Math.Max(1, (int)Math.Round(Height * scale));
                var newW = Math.Min(w, wa.Width);
                var newH = Math.Min(h, wa.Height);
                var newL = pos.X;
                var newT = pos.Y;
                if (newL + newW > wa.Right) newL = wa.Right - newW;
                if (newT + newH > wa.Bottom) newT = wa.Bottom - newH;
                // Left/Top last: if the window still cannot fit (a floor we refused to go under),
                // the two corrections disagree, and keeping the TOP-LEFT corner on-screen is what
                // keeps the title bar draggable and the window recoverable.
                if (newL < wa.X) newL = wa.X;
                if (newT < wa.Y) newT = wa.Y;

                if (newW == w && newH == h && newL == pos.X && newT == pos.Y) return;

                Width = newW / scale;
                Height = newH / scale;
                Position = new PixelPoint(newL, newT);

                Log.Information(
                    "MainShellWindow work-area fit ({Reason}): {W}x{H}@{L},{T} -> {NW}x{NH}@{NL},{NT}; " +
                    "work area {WW}x{WH} at {Scale:0.##}x, floors now {MinW:0}x{MinH:0} DIP",
                    reason, w, h, pos.X, pos.Y, newW, newH, newL, newT,
                    wa.Width, wa.Height, scale, MinWidth, MinHeight);
            }
            catch (Exception ex)
            {
                // Never let a placement tweak take the window down.
                Log.Warning(ex, "MainShellWindow work-area fit failed ({Reason})", reason);
            }
            finally
            {
                _workAreaFitInProgress = false;
            }
        }

        /// <summary>
        /// First point at which there is a platform window, so the first point at which we can ask
        /// which monitor we are on and what its scale is - the Avalonia twin of the WPF
        /// OnSourceInitialized hook, and early enough that an oversized window is corrected
        /// without ever flashing at the wrong size.
        /// </summary>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            try
            {
                EnsureDesignFloorsCaptured();
                FitToCurrentMonitorWorkArea("opened");
                ScalingChanged += (_, __) => FitToCurrentMonitorWorkArea("scaling-changed");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "OnOpened work-area fit failed");
            }
        }
    }
}
