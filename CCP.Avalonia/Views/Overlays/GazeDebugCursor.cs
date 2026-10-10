// PORTED from ConditioningControlPanel/Services/Tracking/GazeDebugCursorService.cs (7.1.5), the part the
// Devices "debug cursor" switch drives: a dot that follows the projected gaze on the tracking monitor.
// Reference counted by reason like WPF Show(reason) / Hide(reason). Click-through, never activates. The
// dot only appears while tracking runs with a calibration (no gaze point arrives otherwise).
// ponytail: WPF's cursor also smooths with its own spring and draws a trail; this draws the tracker's
// already-smoothed point (GazeEngine.Follow) as a plain ring.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    internal static class GazeDebugCursor
    {
        internal const double Size = 28;
        private static readonly HashSet<string> Reasons = new();
        private static Window? _window;
        private static Ellipse? _dot;
        private static bool _subscribed;

        internal static bool IsShown => Reasons.Count > 0;
        internal static bool HasWindow => _window != null;
        /// <summary>Tests: the last point the dot was moved to (window-local DIPs), null while hidden.</summary>
        internal static Point? LastPoint { get; private set; }

        /// <summary>UI thread. <paramref name="host"/> supplies the screen list.</summary>
        internal static void Show(string reason, Visual host)
        {
            Reasons.Add(reason);
            if (!_subscribed)
            {
                WebcamTracker.Instance.OnGazeMove += OnGaze;
                WebcamTracker.Instance.StateChanged += OnState;
                _subscribed = true;
            }
            EnsureWindow(host);
        }

        internal static void Hide(string reason)
        {
            Reasons.Remove(reason);
            if (Reasons.Count == 0) Close();
        }

        /// <summary>Panic / shutdown / consent revoked: off the screen now, whatever asked for it.</summary>
        internal static void HideAll() { Reasons.Clear(); Close(); }

        private static void Close()
        {
            if (_subscribed)
            {
                WebcamTracker.Instance.OnGazeMove -= OnGaze;
                WebcamTracker.Instance.StateChanged -= OnState;
                _subscribed = false;
            }
            var w = _window; _window = null; _dot = null; LastPoint = null;
            try { w?.Close(); } catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private static void EnsureWindow(Visual host)
        {
            if (_window != null) return;
            try
            {
                if (!X11Overlay.IsAvailable) return;   // no overlay backend (headless, Wayland): the switch stays a no-op
                var screen = WebcamScreen.Resolve(TopLevel.GetTopLevel(host)?.Screens);
                if (screen == null) return;
                var scale = screen.Scaling > 0 ? screen.Scaling : 1;
                var pink = Color.FromRgb(0xFF, 0x69, 0xB4);
                _dot = new Ellipse
                {
                    Width = Size, Height = Size, IsVisible = false, IsHitTestVisible = false,
                    Stroke = new SolidColorBrush(pink), StrokeThickness = 3,
                    Fill = new SolidColorBrush(Color.FromArgb(0x55, pink.R, pink.G, pink.B)),
                };
                var canvas = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = false, ClipToBounds = true };
                canvas.Children.Add(_dot);
                var w = new Window
                {
                    WindowDecorations = WindowDecorations.None,
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                    Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
                    CanResize = false, Focusable = false, IsHitTestVisible = false,
                    Width = screen.Bounds.Width / scale, Height = screen.Bounds.Height / scale,
                    Content = canvas,
                };
                if (!X11Overlay.SetClickThrough(w, true) || !X11Overlay.SetOverrideRedirect(w, screen.Bounds, passive: true))
                {
                    w.Close(); _dot = null;
                    return;
                }
                w.Closed += (_, _) => { if (ReferenceEquals(_window, w)) { _window = null; _dot = null; } };
                _window = w;
                w.Show();
            }
            catch (Exception ex) { Log.Warning("Gaze debug cursor: could not show: {Error}", ex.Message); _window = null; _dot = null; }
        }

        internal static void OnGaze(Point p)
        {
            if (Reasons.Count == 0) return;
            LastPoint = p;
            if (_dot == null) return;
            Canvas.SetLeft(_dot, p.X - Size / 2);
            Canvas.SetTop(_dot, p.Y - Size / 2);
            _dot.IsVisible = true;
        }

        // Tracking stopped: no stale dot left hanging on the screen.
        private static void OnState()
        {
            if (WebcamTracker.Instance.IsRunning) return;
            LastPoint = null;
            if (_dot != null) _dot.IsVisible = false;
        }
    }
}
