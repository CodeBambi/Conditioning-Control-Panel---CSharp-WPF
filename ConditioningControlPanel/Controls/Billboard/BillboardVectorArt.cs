using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>A view the host can tint with the card's hue after it is made.</summary>
    internal interface IAccentedArt
    {
        Color Accent { set; }
    }

    /// <summary>
    /// Base for the deck's simple vector art (tables, wheel, spiral, calendar, tip). FX PERF RULES:
    /// the picture lives in ONE <see cref="DrawingGroup"/> that <see cref="OnRender"/> draws once;
    /// a frame re-opens that group and redraws into it, so a tick is a retained-drawing update with
    /// no arrange and no visual-tree invalidation. The clock is a <see cref="FrameClock"/> at 30 fps,
    /// running only between <see cref="Play"/> and <see cref="Pause"/> and only when ambient loops
    /// are allowed; otherwise the art holds one still frame.
    /// </summary>
    public abstract class BillboardVectorArt : FrameworkElement, IBillboardArtView, IAccentedArt
    {
        /// <summary>The frame a still card shows (Motion Off, or never played): far enough in that
        /// every entrance has landed.</summary>
        public const double StillSeconds = 1.6;

        private readonly DrawingGroup _drawing = new();
        private readonly Stopwatch _watch = new();
        private FrameClock? _clock;
        private double _offset;
        private bool _released;

        protected BillboardVectorArt()
        {
            IsHitTestVisible = false;
            SnapsToDevicePixels = false;
            SizeChanged += (_, _) => Redraw();
            Loaded += (_, _) => Redraw();
        }

        /// <summary>The card's hue.</summary>
        public Color Accent { get; set; } = Color.FromRgb(0xff, 0x4f, 0xa8);

        /// <summary>Seconds of animation shown so far.</summary>
        protected double Seconds => _watch.IsRunning ? _offset + _watch.Elapsed.TotalSeconds : (_offset > 0 ? _offset : StillSeconds);

        public void Play()
        {
            if (_released) return;
            if (!MotionFx.AllowAmbientLoops) { Pause(); Redraw(); return; }
            if (_offset <= 0 && !_watch.IsRunning) _offset = 0.0001;
            _watch.Start();
            _clock ??= new FrameClock { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
            _clock.Tick -= OnTick;
            _clock.Tick += OnTick;
            _clock.Start();
        }

        public void Pause()
        {
            _clock?.Stop();
            if (_watch.IsRunning)
            {
                _offset += _watch.Elapsed.TotalSeconds;
                _watch.Reset();
            }
        }

        public void Release()
        {
            Pause();
            if (_clock != null) _clock.Tick -= OnTick;
            _clock = null;
            _released = true;
            using (_drawing.Open()) { }
        }

        public virtual void Touch(Point normalized) { }

        private void OnTick(object? sender, EventArgs e) => Redraw();

        /// <summary>Redraws the picture at the current time.</summary>
        public void Redraw()
        {
            if (_released) return;
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0 || double.IsNaN(w) || double.IsNaN(h)) return;
            using var dc = _drawing.Open();
            try { Paint(dc, w, h, Seconds); }
            catch (Exception ex) { App.Logger?.Debug("Billboard art paint failed: {E}", ex.Message); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            dc.DrawDrawing(_drawing);
        }

        /// <summary>Draws one frame at time <paramref name="t"/> seconds.</summary>
        protected abstract void Paint(DrawingContext dc, double w, double h, double t);

        // ---- shared paint helpers ---------------------------------------------------------------

        /// <summary>The card ground: a radial pool of the hue's dark tone, off to the right where
        /// the art sits, fading to the well floor.</summary>
        protected static void Ground(DrawingContext dc, double w, double h, Color inner)
        {
            var g = new RadialGradientBrush(inner, Color.FromRgb(0x0c, 0x0d, 0x1a))
            {
                Center = new Point(0.72, 0.45),
                GradientOrigin = new Point(0.72, 0.45),
                RadiusX = 0.75,
                RadiusY = 0.75 * w / Math.Max(1, h),
            };
            g.Freeze();
            dc.DrawRectangle(g, null, new Rect(0, 0, w, h));
        }

        /// <summary>A dark tone of a hue for the ground (about a fifth of its brightness).</summary>
        protected static Color Deep(Color c) => Color.FromRgb((byte)(c.R * 0.2 + 8), (byte)(c.G * 0.16 + 9), (byte)(c.B * 0.22 + 18));

        protected static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

        protected static SolidColorBrush Solid(Color c, double a = 1)
        {
            var b = new SolidColorBrush(WithAlpha(c, a));
            b.Freeze();
            return b;
        }

        protected static Pen Stroke(Color c, double thickness, double a = 1)
        {
            var p = new Pen(Solid(c, a), Math.Max(0.5, thickness)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            p.Freeze();
            return p;
        }

        /// <summary>Ease-out cubic, clamped.</summary>
        protected static double EaseOut(double t)
        {
            t = Math.Clamp(t, 0, 1);
            return 1 - Math.Pow(1 - t, 3);
        }

        /// <summary>A soft round glow (no Effect): a radial brush that fades to clear.</summary>
        protected static void Glow(DrawingContext dc, Point c, double r, Color hue, double a)
        {
            var g = new RadialGradientBrush(WithAlpha(hue, a), WithAlpha(hue, 0));
            g.Freeze();
            dc.DrawEllipse(g, null, c, r, r);
        }

        public static Color ParseHue(string? hex, Color fallback)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color c) return c;
            }
            catch { }
            return fallback;
        }
    }
}
