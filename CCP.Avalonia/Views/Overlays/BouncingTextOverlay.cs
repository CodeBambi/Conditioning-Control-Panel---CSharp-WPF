using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The head half of WPF <c>BouncingTextService</c>
    /// (ConditioningControlPanel/Services/Subliminal/BouncingTextService.cs): one full-screen
    /// click-through override-redirect window per targeted screen, the logos drawn on it, moved
    /// every rendered frame by Core <see cref="BouncingTextEngine"/>. WPF drives the loop off
    /// <c>CompositionTarget.Rendering</c> (one callback per rendered frame, dt clamped at 0.1 s);
    /// the Avalonia twin is <c>TopLevel.RequestAnimationFrame</c>. Reached through the
    /// <see cref="CoreBouncingText"/> seam, which App seeds.
    ///
    /// <para>Coordinates are WPF's: the engine bounces in DIPs at ONE scale (WPF: the system DPI;
    /// here: the primary screen's scaling), each window draws the part on its screen.</para>
    ///
    /// <para>ponytail: not here yet - pause during mandatory video / BouncingTextAlwaysOnTop (no
    /// video service on this head), corner-hit achievement, bounce haptics, the 0.5 s topmost
    /// re-assert (override-redirect windows need no WM layer, but a later overlay maps above).</para>
    /// </summary>
    internal static class BouncingTextOverlay
    {
        private static readonly BouncingTextEngine Engine = new();
        private static readonly List<BouncingTextOverlayWindow> Windows = new();
        private static bool _running;
        private static TimeSpan? _last;
        private static Visual? _host;

        // CCP_FRAME_STATS=1: one stdout line per 10 s of animation (frame count and dt spread).
        private static readonly bool Stats = Environment.GetEnvironmentVariable("CCP_FRAME_STATS") == "1";
        private static readonly List<double> FrameMs = new();
        private static readonly Stopwatch Cpu = new();
        private static TimeSpan _cpuAtStart;

        public static bool IsRunning => _running;

        /// <summary>Is a logo (engine DIPs) close enough to this screen to draw on it? Padded the way
        /// WPF pads its OCR rects, (w+h)/2 + 80, which covers the scale/rotate effects; plus the
        /// 156-DIP half-width of a full-grown corner-burst ring.</summary>
        internal static bool IsNear(double x, double y, double w, double h, PixelRect screen, double k)
        {
            var pad = (w + h) / 2 + 80 + 156;
            return x + w + pad > screen.X / k && x - pad < screen.Right / k
                && y + h + pad > screen.Y / k && y - pad < screen.Bottom / k;
        }

        /// <summary>Virtual-desktop bounds of the targeted screens in engine DIPs (WPF CalculateScreenBounds).</summary>
        internal static (double MinX, double MinY, double MaxX, double MaxY) Bounds(IEnumerable<PixelRect> screens, double k)
        {
            var r = screens.ToList();
            return (r.Min(s => s.X) / k, r.Min(s => s.Y) / k, r.Max(s => s.Right) / k, r.Max(s => s.Bottom) / k);
        }

        /// <summary>A global engine position on one screen's canvas (WPF BouncingTextWindow.UpdatePosition).</summary>
        internal static Point ToLocal(double x, double y, PixelRect screen, double k) => new(x - screen.X / k, y - screen.Y / k);

        public static void Start(Visual host, IReadOnlyList<string>? pool = null)
        {
            if (_running) return;
            if (!X11Overlay.IsAvailable) { Log.Warning("Bouncing text: this platform cannot show click-through overlays; skipped"); return; }
            var screens = ScreenList.Enumerate(host);
            if (screens.Count == 0) return;

            var s = CoreSettings.Current;
            var primary = Math.Max(0, screens.ToList().FindIndex(x => x.IsPrimary));
            var k = screens[primary].Scaling > 0 ? screens[primary].Scaling : 1.0;
            var targets = PinkFilterOverlay.ResolveScreenIndices(s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary)
                .Select(i => screens[i]).ToList();

            var (minX, minY, maxX, maxY) = Bounds(targets.Select(t => t.Bounds), k);
            Engine.SetBounds(minX, minY, maxX, maxY);
            Engine.Measure = Measure;
            Engine.Start(s, pool);

            foreach (var screen in targets)
            {
                var w = new BouncingTextOverlayWindow(screen.Bounds, k, Engine.FontSize, s.BouncingTextOpacity, Engine.Logos.Count, s.BouncingTextOutline);
                if (!X11Overlay.SetClickThrough(w, true) || !X11Overlay.SetOpacity(w, 0) || !X11Overlay.SetOverrideRedirect(w, screen.Bounds))
                {
                    Log.Warning("Bouncing text: the platform refused a click-through topmost overlay window; skipped");
                    w.Close();
                    Stop();
                    return;
                }
                // The canvas works in engine DIPs; a screen at another scaling than the primary
                // gets them converted to its own DIPs so the text keeps its physical size.
                w.Canvas.RenderTransform = new ScaleTransform(k / w.DesktopScaling, k / w.DesktopScaling);
                w.Show();
                Windows.Add(w);
            }
            _host = host;
            _running = true;
            _last = null;
            for (var i = 0; i < Engine.Logos.Count; i++)
            {
                UpdateText(i);
                var l = Engine.Logos[i];
                foreach (var w in Windows) w.Move(i, l.PosX, l.PosY, 1, 1, 0);   // placed before the first paint
            }
            NextFrame();
            if (Stats) { FrameMs.Clear(); Cpu.Restart(); _cpuAtStart = Process.GetCurrentProcess().TotalProcessorTime; }
            Log.Information("Bouncing text started - Logos: {Count}", Engine.Logos.Count);
        }

        public static void Stop()
        {
            _running = false;
            foreach (var w in Windows) w.Close();
            Windows.Clear();
            Engine.Stop();
        }

        /// <summary>WPF Restart: rebuild for the settings that change the element (outline, second logo).</summary>
        public static void Restart()
        {
            if (!_running || _host is not { } host) return;
            var pool = Engine.PoolOverride?.ToList();
            Stop();
            Start(host, pool);
        }

        /// <summary>WPF Refresh: speed, size, family, opacity, fixed colour - live.</summary>
        public static void Refresh()
        {
            if (!_running) return;
            var s = CoreSettings.Current;
            Engine.RefreshSpeed(s);
            Engine.RefreshFontSize(s);   // both re-measure the logos when their setting moved
            Engine.RefreshFont(s);
            foreach (var w in Windows) w.Restyle(Engine.FontSize, Family(), s.BouncingTextOpacity);
            if (s.BouncingTextColorMode == 1) Engine.ApplyFixedColor(s);
            for (var i = 0; i < Engine.Logos.Count; i++) UpdateText(i);
        }

        internal static FontFamily Family()
        {
            var f = CoreSettings.Current.BouncingTextFont;
            return new FontFamily(string.IsNullOrWhiteSpace(f) ? "Segoe UI" : f + ", Segoe UI");
        }

        private static (double, double) Measure(string text, int size)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(Family(), FontStyle.Normal, FontWeight.Bold), size, Brushes.White);
            return (ft.WidthIncludingTrailingWhitespace, ft.Height);
        }

        private static void UpdateText(int i)
        {
            var l = Engine.Logos[i];
            var color = Color.FromRgb(l.Color.R, l.Color.G, l.Color.B);
            foreach (var w in Windows) w.SetText(i, l.Text, color);
        }

        /// <summary>One chain per run: a frame requested on a window a Restart has since closed
        /// must not keep a second loop going next to the new one.</summary>
        private static void NextFrame()
        {
            var w = Windows[0];
            w.RequestAnimationFrame(now => { if (_running && Windows.Count > 0 && Windows[0] == w) OnFrame(now); });
        }

        private static void OnFrame(TimeSpan now)
        {
            NextFrame();

            // WPF Animate: baseline on the first frame, skip duplicates, clamp a stall at 0.1 s,
            // and freeze while a display change settles.
            // Mapped at alpha 0 (the flash ordering); visible from the first painted frame on.
            if (_last is not { } last) { _last = now; foreach (var w in Windows) X11Overlay.SetOpacity(w, 1); return; }
            var dt = (now - last).TotalSeconds;
            _last = now;
            if (dt <= 0 || DisplayChangeCoordinator.SpawnsSuppressed) return;
            if (Stats) Sample(dt);
            dt = Math.Min(dt, 0.1);

            var s = CoreSettings.Current;
            Engine.Tick(dt);
            var logos = Engine.Logos;
            for (var i = 0; i < logos.Count; i++)
            {
                var l = logos[i];
                var step = Engine.Step(l, dt, s);
                if (step.CornerHit && s.BouncingTextFxCornerBurst)
                {
                    var c = step.ColorBeforeBounce;
                    foreach (var w in Windows) w.SpawnBurst(l.PosX + l.TextWidth / 2, l.PosY + l.TextHeight / 2, Color.FromRgb(c.R, c.G, c.B));
                }
                if (step.Bounced) UpdateText(i);
            }
            for (var i = 0; i < logos.Count; i++)
            {
                var l = logos[i];
                var (sx, sy, angle) = Engine.ComputeEffectTransform(l, s);
                foreach (var w in Windows) w.Move(i, l.PosX, l.PosY, sx, sy, angle, l.TextWidth, l.TextHeight);
            }
            foreach (var w in Windows) w.AgeBursts(dt);
        }

        private static void Sample(double dt)
        {
            FrameMs.Add(dt * 1000);
            if (Cpu.Elapsed.TotalSeconds < 10) return;
            var sorted = FrameMs.OrderBy(x => x).ToList();
            var cpu = (Process.GetCurrentProcess().TotalProcessorTime - _cpuAtStart).TotalMilliseconds / Cpu.Elapsed.TotalMilliseconds * 100;
            Console.WriteLine(FormattableString.Invariant(
                $"[BouncingText] {Cpu.Elapsed.TotalSeconds:F1} s: {sorted.Count} frames ({sorted.Count / Cpu.Elapsed.TotalSeconds:F1} fps), dt median {sorted[sorted.Count / 2]:F1} ms, p99 {sorted[(int)(sorted.Count * 0.99)]:F1} ms, max {sorted[^1]:F1} ms, >2x median {sorted.Count(x => x > 2 * sorted[sorted.Count / 2])}, process CPU {cpu:F0}% of one core"));
            FrameMs.Clear();
            Cpu.Restart();
            _cpuAtStart = Process.GetCurrentProcess().TotalProcessorTime;
        }
    }

    /// <summary>
    /// One screen of bouncing text: WPF <c>BouncingTextWindow</c>. Each logo is bold, in
    /// BouncingTextFont (Segoe UI fallback), at BouncingTextOpacity, either with WPF's black drop
    /// shadow (blur 10, depth 3) or - BouncingTextOutline - WPF OutlinedText's black stroke of
    /// max(2, size/22). Scale then rotate about the centre, as WPF's TransformGroup.
    /// </summary>
    internal sealed class BouncingTextOverlayWindow : Window
    {
        private sealed record Logo(Control Element, TextBlock? Tb, OutlinedTextBlock? Ot, ScaleTransform Scale, RotateTransform Rotate);

        private readonly List<Logo> _logos = new();
        private readonly List<(Ellipse Ring, ScaleTransform Scale, double Age)> _bursts = new();
        private readonly PixelRect _screen;
        private readonly double _k;
        internal readonly Canvas Canvas = new();

        /// <summary>--render-all only: both styles side by side at the user's size.</summary>
        internal BouncingTextOverlayWindow() : this(new PixelRect(0, 0, 1280, 720), 1, 72, 100, 2, false)
        {
            Width = 1280;
            Height = 720;
            Background = Brushes.DimGray;   // the proof PNG has no desktop; black shadow/stroke must show
            SetText(0, "DEEPER", Colors.HotPink);
            SetText(1, "OBEY", Colors.Cyan);
            Move(0, 200, 200, 1, 1, 0);
            Move(1, 700, 400, 1, 1, -8);
            var ot = new OutlinedTextBlock { Text = "OUTLINED", FontSize = 72, Family = BouncingTextOverlay.Family(), Fill = Brushes.HotPink };
            ot.Build();
            Canvas.SetLeft(ot, 200);
            Canvas.SetTop(ot, 450);
            Canvas.Children.Add(ot);
        }

        public BouncingTextOverlayWindow(PixelRect screen, double k, int fontSize, int opacity, int logoCount, bool outline)
        {
            _screen = screen;
            _k = k;
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            Canvas.RenderTransformOrigin = RelativePoint.TopLeft;
            Content = Canvas;

            var family = BouncingTextOverlay.Family();
            for (var i = 0; i < logoCount; i++)
            {
                var scale = new ScaleTransform(1, 1);
                var rotate = new RotateTransform(0);
                var transform = new TransformGroup { Children = { scale, rotate } };
                TextBlock? tb = null;
                OutlinedTextBlock? ot = null;
                if (outline)
                    ot = new OutlinedTextBlock { Family = family, FontSize = fontSize, Fill = Brushes.HotPink };
                else
                    tb = new TextBlock
                    {
                        FontFamily = family,
                        FontSize = fontSize,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brushes.HotPink,
                        Effect = new DropShadowDirectionEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 3 },
                    };
                Control e = (Control?)tb ?? ot!;
                e.Opacity = opacity / 100.0;
                e.RenderTransform = transform;
                e.IsHitTestVisible = false;
                Canvas.Children.Add(e);
                _logos.Add(new Logo(e, tb, ot, scale, rotate));
            }
        }

        public void SetText(int i, string text, Color color)
        {
            if (i < 0 || i >= _logos.Count) return;
            var l = _logos[i];
            if (l.Tb != null) { l.Tb.Text = text; l.Tb.Foreground = new SolidColorBrush(color); }
            else if (l.Ot != null) { l.Ot.Text = text; l.Ot.Fill = new SolidColorBrush(color); l.Ot.Build(); }
        }

        public void Restyle(int fontSize, FontFamily family, int opacity)
        {
            foreach (var l in _logos)
            {
                l.Element.Opacity = opacity / 100.0;
                if (l.Tb != null) { l.Tb.FontSize = fontSize; l.Tb.FontFamily = family; }
                else if (l.Ot != null) { l.Ot.FontSize = fontSize; l.Ot.Family = family; l.Ot.Build(); }
            }
        }

        private readonly HashSet<int> _near = new();

        /// <summary>Only a screen the logo is on, or just left, is touched: writing a position
        /// into every screen's canvas each frame re-renders every full-screen window.
        /// <paramref name="w"/>/<paramref name="h"/> 0 = always move (placement before the first frame).</summary>
        public void Move(int i, double x, double y, double sx, double sy, double angle, double w = 0, double h = 0)
        {
            if (i < 0 || i >= _logos.Count) return;
            var l = _logos[i];
            if (w > 0)
            {
                var near = BouncingTextOverlay.IsNear(x, y, w, h, _screen, _k);
                if (!near && !_near.Contains(i)) return;
                if (near) _near.Add(i); else _near.Remove(i);
            }
            var p = BouncingTextOverlay.ToLocal(x, y, _screen, _k);
            // OutlinedText's glyphs are inset by its padding; shift so they land where the engine measured.
            var pad = l.Ot?.Pad ?? 0;
            Canvas.SetLeft(l.Element, p.X - pad);
            Canvas.SetTop(l.Element, p.Y - pad);
            l.Scale.ScaleX = sx;
            l.Scale.ScaleY = sy;
            l.Rotate.Angle = angle;
        }

        /// <summary>WPF SpawnCornerBurst: a 240-DIP ring, stroke 6, scaling 0.1 -> 1.3 (quadratic
        /// ease-out) while fading 0.9 -> 0 over 550 ms. Aged by the frame loop.</summary>
        public void SpawnBurst(double x, double y, Color color)
        {
            const double size = 240;
            var p = BouncingTextOverlay.ToLocal(x, y, _screen, _k);
            var scale = new ScaleTransform(0.1, 0.1);
            var ring = new Ellipse { Width = size, Height = size, Stroke = new SolidColorBrush(color), StrokeThickness = 6, Opacity = 0.9, RenderTransform = scale, IsHitTestVisible = false };
            Canvas.SetLeft(ring, p.X - size / 2);
            Canvas.SetTop(ring, p.Y - size / 2);
            Canvas.Children.Add(ring);
            _bursts.Add((ring, scale, 0));
        }

        public void AgeBursts(double dt)
        {
            for (var i = _bursts.Count - 1; i >= 0; i--)
            {
                var (ring, scale, age) = _bursts[i];
                age += dt;
                var t = Math.Min(1, age / 0.55);
                if (t >= 1) { Canvas.Children.Remove(ring); _bursts.RemoveAt(i); continue; }
                var eased = 1 - (1 - t) * (1 - t);
                scale.ScaleX = scale.ScaleY = 0.1 + 1.2 * eased;
                ring.Opacity = 0.9 * (1 - t);
                _bursts[i] = (ring, scale, age);
            }
        }
    }

    /// <summary>WPF <c>OutlinedText</c> (ConditioningControlPanel/Windows/OutlinedText.cs): the glyph
    /// outline stroked at 2 x StrokeThickness with round joins, then filled, inset by StrokeThickness + 6.</summary>
    internal sealed class OutlinedTextBlock : Control
    {
        public string Text { get; set; } = "";
        public double FontSize { get; set; } = 60;
        public FontFamily Family { get; set; } = new("Segoe UI");
        public IBrush Fill { get; set; } = Brushes.White;
        public double Pad { get; private set; }
        private Geometry? _geo;

        public void Build()
        {
            var stroke = Math.Max(2.0, FontSize / 22.0);
            Pad = stroke + 6;
            var ft = new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(Family, FontStyle.Normal, FontWeight.Bold), FontSize, Fill);
            _geo = string.IsNullOrEmpty(Text) ? null : ft.BuildGeometry(new Point(Pad, Pad));
            Width = ft.WidthIncludingTrailingWhitespace + Pad * 2;
            Height = ft.Height + Pad * 2;
            InvalidateVisual();
        }

        public override void Render(DrawingContext dc)
        {
            if (_geo == null) return;
            dc.DrawGeometry(null, new Pen(Brushes.Black, (Pad - 6) * 2, lineJoin: PenLineJoin.Round), _geo);
            dc.DrawGeometry(Fill, null, _geo);
        }
    }
}
