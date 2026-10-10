// PORTED from ConditioningControlPanel/Services/KeywordHighlightService.cs (7.1.5): a rounded box
// around each matched word on its own screen. Same look and numbers: 10 px pad, radius 6, a 4 px stroke
// in the user's colour over a 50% fill of it, held 60% of KeywordHighlightDurationMs then faded over
// the rest. Click-through, never activates, and (unless "visible in captures" is on) excluded from
// screen capture so the next screen read does not see its own box.
//
// WPF kept one overlay window alive per screen and faded each box; here each fire gets a short-lived
// window per screen whose compositor alpha fades (the port's overlay rule: render once, fade the
// window), closed when the fade ends. So the capture switch applies from the next fire, with no live
// windows to refresh.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Services.KeywordTriggers;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    internal static class KeywordHighlightOverlay
    {
        internal const double Pad = 10;
        internal const double Radius = 6;
        internal const double Stroke = 4;
        internal const double HoldShare = 0.60;
        internal static readonly Color DefaultColor = Color.FromRgb(0xFF, 0x69, 0xB4);

        private static readonly List<Window> Active = new();

        /// <summary>WPF ParseHighlightColor: #RRGGBB or #AARRGGBB, neon pink when unreadable.</summary>
        internal static Color ParseColor(string? hex) =>
            !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex.Trim(), out var c) ? c : DefaultColor;

        /// <summary>WPF AnimateHighlight: (hold, fade) in ms for a total duration.</summary>
        internal static (int HoldMs, int FadeMs) Envelope(int totalMs)
        {
            var hold = (int)(totalMs * HoldShare);
            return (hold, Math.Max(1, totalMs - hold));
        }

        /// <summary>WPF AddHighlightElement's mapping: the word's offset inside the screen grab, scaled to
        /// the canvas (which covers the same screen), padded by 10 on every side.</summary>
        internal static Rect LocalBox(OcrWordHit word, PixelRect screen, double canvasW, double canvasH)
        {
            double bw = Math.Max(1, screen.Width), bh = Math.Max(1, screen.Height);
            var x = (word.X - screen.X) * canvasW / bw;
            var y = (word.Y - screen.Y) * canvasH / bh;
            var w = word.Width * canvasW / bw;
            var h = word.Height * canvasH / bh;
            return new Rect(x - Pad, y - Pad, w + Pad * 2, h + Pad * 2);
        }

        /// <summary>The screen a word sits on: the one holding its centre, else the first.</summary>
        internal static int ScreenIndexOf(OcrWordHit word, IReadOnlyList<PixelRect> screens)
        {
            var centre = new PixelPoint(word.X + word.Width / 2, word.Y + word.Height / 2);
            for (var i = 0; i < screens.Count; i++)
                if (screens[i].Contains(centre)) return i;
            return 0;
        }

        /// <summary>The boxes for one screen, split out so a test can check them against WPF.</summary>
        internal static Canvas Build(IEnumerable<OcrWordHit> words, PixelRect screen, double canvasW, double canvasH, Color color)
        {
            var canvas = new Canvas { Background = Brushes.Transparent, ClipToBounds = true, IsHitTestVisible = false };
            var stroke = new SolidColorBrush(color);
            var fill = new SolidColorBrush(Color.FromArgb(0x80, color.R, color.G, color.B));
            foreach (var word in words)
            {
                var box = LocalBox(word, screen, canvasW, canvasH);
                var rect = new Rectangle
                {
                    Width = box.Width, Height = box.Height, RadiusX = Radius, RadiusY = Radius,
                    Stroke = stroke, StrokeThickness = Stroke, Fill = fill, IsHitTestVisible = false,
                };
                Canvas.SetLeft(rect, box.X);
                Canvas.SetTop(rect, box.Y);
                canvas.Children.Add(rect);
            }
            return canvas;
        }

        /// <summary>WPF ShowHighlight. UI thread.</summary>
        internal static void Show(Visual host, IReadOnlyList<OcrWordHit>? words)
        {
            if (words == null || words.Count == 0) return;
            var s = CoreSettings.Current;
            if (s?.KeywordHighlightEnabled != true) return;
            if (!X11Overlay.IsAvailable) return;
            try
            {
                var screens = ScreenList.Enumerate(host);
                if (screens.Count == 0) return;
                var bounds = screens.Select(x => x.Bounds).ToList();
                var color = ParseColor(s.KeywordHighlightColor);
                var (holdMs, fadeMs) = Envelope(s.KeywordHighlightDurationMs > 0 ? s.KeywordHighlightDurationMs : 1500);
                foreach (var group in words.GroupBy(w => ScreenIndexOf(w, bounds)))
                {
                    var screen = screens[group.Key];
                    var scale = screen.Scaling > 0 ? screen.Scaling : 1;
                    var w = new Window
                    {
                        WindowDecorations = WindowDecorations.None,
                        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                        Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
                        CanResize = false, Focusable = false, IsHitTestVisible = false,
                        Content = Build(group, screen.Bounds, screen.Bounds.Width / scale, screen.Bounds.Height / scale, color),
                    };
                    if (!X11Overlay.SetClickThrough(w, true) || !X11Overlay.SetOverrideRedirect(w, screen.Bounds, passive: true))
                    {
                        w.Close();
                        continue;
                    }
                    var closed = false;
                    w.Closed += (_, _) => { closed = true; Active.Remove(w); };
                    Active.Add(w);
                    w.Show();
                    if (!s.OcrHighlightVisibleInCapture) ExcludeFromCapture(w);
                    const int steps = 8;
                    for (var i = 1; i <= steps; i++)
                    {
                        var v = 1.0 - (double)i / steps;
                        DispatcherTimer.RunOnce(() => { if (!closed) X11Overlay.SetOpacity(w, v); },
                            TimeSpan.FromMilliseconds(holdMs + fadeMs * (double)i / steps));
                    }
                    DispatcherTimer.RunOnce(() => { if (!closed) w.Close(); }, TimeSpan.FromMilliseconds(holdMs + fadeMs + 20));
                }
            }
            catch (Exception ex) { Log.Warning("Keyword highlight: could not show: {Error}", ex.Message); }
        }

        /// <summary>Panic / shutdown: every box off the screen now.</summary>
        internal static void CloseAll()
        {
            foreach (var w in Active.ToList()) { try { w.Close(); } catch { } }
        }

        internal static int ActiveCount => Active.Count;

        private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        /// <summary>WPF: WDA_EXCLUDEFROMCAPTURE so the scanner never reads its own highlight. Windows
        /// only; X11 has no per-window capture exclusion, so on Linux the box is always in captures.</summary>
        private static void ExcludeFromCapture(Window w)
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                var hwnd = w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd != IntPtr.Zero) SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
            }
            catch (Exception ex) { Log.Debug("Keyword highlight: capture exclusion failed: {Error}", ex.Message); }
        }
    }
}
