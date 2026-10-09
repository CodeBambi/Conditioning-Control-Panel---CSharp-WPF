using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The head half of WPF <c>SubliminalService.ShowSubliminalVisuals</c>
    /// (ConditioningControlPanel/Services/Subliminal/SubliminalService.cs:666): one card per
    /// targeted screen, full-screen, click-through, override-redirect, faded 50 ms in, held
    /// SubliminalDuration x 17 ms (min 100), faded 50 ms out, to SubliminalOpacity. Core
    /// <see cref="CoreSubliminal"/> decides when and which phrase and calls <see cref="Show"/>.
    ///
    /// <para>A new card replaces the one on screen (WPF reuses one keep-alive window per screen,
    /// and SubliminalLayer draws the most recent card only).</para>
    ///
    /// <para>Whisper audio, ducking, XP and the Bambi Freeze/Reset pair: <see cref="SubliminalWhisperShow"/>.
    /// ponytail: not here yet - SubliminalStealsFocus (always no-activate here), the solid-mode
    /// shared host (same look, separate windows here).</para>
    /// </summary>
    internal static class SubliminalOverlay
    {
        /// <summary>WPF <c>DefaultFadeMs</c>: 50 ms each side of the hold.</summary>
        internal const int FadeMs = 50;

        private static readonly List<SubliminalOverlayWindow> Active = new();
        private static bool _warnedUnavailable;

        /// <summary>WPF: "Duration in frames * ~16.6ms per frame, minimum 100ms".</summary>
        internal static int HoldMs(int frames) => Math.Max(100, frames * 17);

        /// <summary>Draw one card for <paramref name="text"/> on every targeted screen; <paramref name="opacity"/>
        /// (percent) overrides the setting, as WPF FlashSubliminalCustom's does.</summary>
        public static void Show(Visual host, string text, int? opacity = null)
        {
            if (!X11Overlay.IsAvailable)
            {
                if (!_warnedUnavailable) Log.Warning("Subliminal: this platform cannot show click-through overlays, so subliminals are skipped");
                _warnedUnavailable = true;
                return;
            }
            var screens = ScreenList.Enumerate(host);
            if (screens.Count == 0) return;

            CloseAll();
            CoreTubeEvents.RaiseSubliminalDisplayed();   // WPF SubliminalService.SubliminalDisplayed (tube#T5)
            var s = CoreSettings.Current;
            var primary = Math.Max(0, screens.ToList().FindIndex(x => x.IsPrimary));
            var hold = TimeSpan.FromMilliseconds(HoldMs(s.SubliminalDuration));
            var alpha = Math.Clamp((opacity ?? s.SubliminalOpacity) / 100.0, 0, 1);
            foreach (var i in PinkFilterOverlay.ResolveScreenIndices(s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary))
            {
                var w = new SubliminalOverlayWindow(text);
                if (!X11Overlay.SetClickThrough(w, true) || !X11Overlay.SetOpacity(w, 0) || !X11Overlay.SetOverrideRedirect(w, screens[i].Bounds))
                {
                    if (!_warnedUnavailable) Log.Warning("Subliminal: the platform refused a click-through topmost overlay window; skipped");
                    _warnedUnavailable = true;
                    w.Close();
                    return;
                }
                w.Closed += (_, _) => Active.Remove(w);
                Active.Add(w);
                w.Show();
                w.Run(alpha, hold);
            }
        }

        /// <summary>Take every card off screen now (WPF <c>TearDownSurfaces</c> on Stop).</summary>
        public static void CloseAll()
        {
            foreach (var w in Active.ToList()) w.Close();
        }
    }

    /// <summary>
    /// One subliminal card: WPF <c>BuildSubliminalContent</c> + <c>CreateTextBlock</c>
    /// (SubliminalService.cs:1028, :1335). Optional full-screen SubBackgroundColor, then 8 copies
    /// of the text in SubBorderColor at the WPF offsets, then the text in SubTextColor - bold,
    /// 120 DIP, SubliminalFont (Arial fallback), centred on the screen.
    /// </summary>
    internal sealed class SubliminalOverlayWindow : Window
    {
        /// <summary>WPF outline offsets in DIP (BuildSubliminalContent).</summary>
        internal static readonly (double X, double Y)[] Offsets =
        {
            (-3, -3), (3, -3), (-3, 3), (3, 3),
            (0, -4), (0, 4), (-4, 0), (4, 0),
        };

        /// <summary>--render-all only: a sample card at the user's settings.</summary>
        internal SubliminalOverlayWindow() : this("GOOD GIRL")
        {
            Width = 1280;
            Height = 720;
        }

        public SubliminalOverlayWindow(string text)
        {
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            Content = Build(text, CoreSettings.Current);
        }

        /// <summary>The card's visual tree, split out so a test can check it against WPF.</summary>
        internal static Grid Build(string text, Models.AppSettings s)
        {
            var grid = new Grid { IsHitTestVisible = false };
            if (!s.SubBackgroundTransparent)
                grid.Children.Add(new Rectangle { Fill = new SolidColorBrush(Parse(s.SubBackgroundColor, Colors.Black)) });

            var family = new FontFamily(string.IsNullOrWhiteSpace(s.SubliminalFont) ? "Arial" : s.SubliminalFont + ", Arial");
            TextBlock Text(Color c, double x, double y) => new()
            {
                Text = text,
                FontSize = 120,
                FontWeight = FontWeight.Bold,
                FontFamily = family,
                Foreground = new SolidColorBrush(c),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = new TranslateTransform(x, y),
            };
            var border = Parse(s.SubBorderColor, Colors.White);
            foreach (var (x, y) in Offsets) grid.Children.Add(Text(border, x, y));
            grid.Children.Add(Text(Parse(s.SubTextColor, Color.FromRgb(255, 0, 255)), 0, 0));
            return grid;
        }

        private static Color Parse(string? hex, Color fallback) => Color.TryParse(hex, out var c) ? c : fallback;

        /// <summary>WPF AnimateSubliminal: 0 -> alpha over 50 ms, hold, alpha -> 0 over 50 ms, then
        /// close. Compositor-side alpha (<see cref="X11Overlay.SetOpacity"/>), so the card renders once.</summary>
        public void Run(double alpha, TimeSpan hold)
        {
            var fade = TimeSpan.FromMilliseconds(SubliminalOverlay.FadeMs);
            var closed = false;
            Closed += (_, _) => closed = true;   // a newer card may have closed this one already
            // The envelope starts at the card's first frame, not at Show(): mapping and the first
            // full-screen paint take time a 100 ms hold cannot spare. A backstop from Show() caps the
            // lifetime anyway, so a late first frame shortens the card instead of stranding it.
            DispatcherTimer.RunOnce(() => { if (!closed) Close(); }, MaxLifetime(hold));
            RequestFrame(this, _ =>
            {
                if (closed) return;
                Ramp(0, alpha, TimeSpan.Zero, fade);
                Ramp(alpha, 0, fade + hold, fade);
                DispatcherTimer.RunOnce(() => { if (!closed) Close(); }, fade + hold + fade + TimeSpan.FromMilliseconds(1));
            });
        }

        /// <summary>Test seam: a test swaps in a first frame that never comes.</summary>
        internal static Action<TopLevel, Action<TimeSpan>> RequestFrame = (t, a) => t.RequestAnimationFrame(a);

        /// <summary>Show() to forced close: the whole envelope plus 100 ms of first-frame slack.</summary>
        internal static TimeSpan MaxLifetime(TimeSpan hold) => hold + TimeSpan.FromMilliseconds(4 * SubliminalOverlay.FadeMs);

        private void Ramp(double from, double to, TimeSpan start, TimeSpan span)
        {
            const int steps = 3;   // ~one step per 60 Hz frame across 50 ms
            for (var i = 1; i <= steps; i++)
            {
                var v = from + (to - from) * i / steps;
                DispatcherTimer.RunOnce(() => X11Overlay.SetOpacity(this, v), start + span * i / steps);
            }
        }
    }
}
