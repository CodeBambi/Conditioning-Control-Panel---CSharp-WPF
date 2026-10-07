using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>
    /// The deck lane's art views, ported from the mockup's canvas drawings: a poster, open tables,
    /// a prize wheel, a spiral, a calendar and a tip. Each is decoration drawn from shapes, so a
    /// provider can wear one without shipping a picture.
    /// </summary>
    public static class BuiltInArt
    {
        private const string PackRoot = "pack://application:,,,/Resources/";

        /// <summary>Registers every built-in key. Idempotent.</summary>
        public static void Register()
        {
            BillboardArt.Register(BuiltInArtKeys.Poster, d => new PosterArtView(d as string));
            BillboardArt.Register(BuiltInArtKeys.Tables, d => new TablesArtView(Names(d), Count(d)));
            BillboardArt.Register(BuiltInArtKeys.Wheel, d => new WheelArtView(WheelData(d)));
            BillboardArt.Register(BuiltInArtKeys.Spiral, d => new SpiralArtView(d as string));
            BillboardArt.Register(BuiltInArtKeys.Calendar, d => new CalendarArtView(CalendarData(d)));
            BillboardArt.Register(BuiltInArtKeys.Tip, _ => new TipArtView());
            BillboardArt.Register(BuiltInArtKeys.Invite, _ => new InviteArtView());
        }

        /// <summary>
        /// A poster path made safe: relative, forward slashes, no parent hops, no scheme, an image
        /// extension. Anything else is null and the card shows its ground instead.
        /// </summary>
        public static string? PosterUri(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var p = path.Trim().Replace('\\', '/');
            if (p.StartsWith("/", StringComparison.Ordinal) || p.Contains("..", StringComparison.Ordinal) || p.Contains(':')) return null;
            var ext = System.IO.Path.GetExtension(p).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp")) return null;
            return PackRoot + p;
        }

        internal static IReadOnlyList<string> Names(object? data) => data switch
        {
            string s when !string.IsNullOrWhiteSpace(s) => new[] { s },
            IEnumerable<string> list => list.Where(n => !string.IsNullOrWhiteSpace(n)).Take(3).ToList(),
            _ => Array.Empty<string>(),
        };

        /// <summary>A value from the providers' plain {key: int} payload, or null.</summary>
        internal static int? Get(object? data, string key) =>
            data is IReadOnlyDictionary<string, int> d && d.TryGetValue(key, out var v) ? v : null;

        /// <summary>Tables: {count}, rows to draw (1..3), or how many names came.</summary>
        internal static int Count(object? data) =>
            Math.Clamp(Get(data, "count") ?? Math.Max(Names(data).Count, 3), 1, 3);

        /// <summary>Wheel: {done, total}, or (0, 0) for a plain wheel.</summary>
        internal static (int Done, int Total) WheelData(object? data)
        {
            int total = Math.Clamp(Get(data, "total") ?? 0, 0, 99);
            return (Math.Clamp(Get(data, "done") ?? 0, 0, total), total);
        }

        /// <summary>
        /// Calendar, from the providers' payloads: Locktober {counted, need, days, today}, a
        /// program {day, days} (days before today count, the bar runs to the last day), the old
        /// int[] {counted, needed}, or nothing (this month, today ringed, no bar). Every figure is
        /// clamped into a month.
        /// </summary>
        internal static CalendarInfo CalendarData(object? data, DateTime? nowLocal = null)
        {
            var now = nowLocal ?? DateTime.Now;
            int monthDays = DateTime.DaysInMonth(now.Year, now.Month);
            int counted = 0, needed = 0, days = monthDays, today = now.Day;
            if (data is int[] a && a.Length >= 2) { counted = a[0]; needed = a[1]; }
            else if (Get(data, "day") is int day && Get(data, "counted") == null)
            {
                today = day;
                days = Get(data, "days") ?? Math.Max(day, monthDays);
                counted = day - 1;
                needed = days;
            }
            else if (data is IReadOnlyDictionary<string, int>)
            {
                counted = Get(data, "counted") ?? 0;
                needed = Get(data, "need") ?? 0;
                days = Get(data, "days") ?? monthDays;
                today = Get(data, "today") ?? now.Day;
            }
            days = Math.Clamp(days, 1, 31);
            return new CalendarInfo(Math.Clamp(counted, 0, days), Math.Clamp(needed, 0, 31), days, Math.Clamp(today, 1, days));
        }
    }

    /// <summary>What the calendar draws: counted days filled, the bar toward needed, today ringed.</summary>
    public readonly record struct CalendarInfo(int Counted, int Needed, int Days, int Today);

    /// <summary>A 16:9 poster, cover-fitted, drifting in very slowly while it is on screen.</summary>
    public sealed class PosterArtView : Grid, IBillboardArtView
    {
        private const double DriftScale = 1.045;
        private readonly Image _image;
        private readonly ScaleTransform _zoom = new(1, 1);

        public PosterArtView(string? path)
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
            Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0d, 0x1a));
            _image = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.62, 0.5),
                RenderTransform = _zoom,
            };
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            var uri = BuiltInArt.PosterUri(path);
            if (uri != null)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(uri, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    _image.Source = bmp;
                }
                catch (Exception ex) { App.Logger?.Debug("Billboard poster {P} did not load: {E}", path, ex.Message); }
            }
            Children.Add(_image);
        }

        public void Play()
        {
            if (!MotionFx.AllowAmbientLoops) return;
            double from = _zoom.ScaleX;
            var drift = new DoubleAnimation(from, DriftScale,
                TimeSpan.FromSeconds(DashboardBillboard.HoldSeconds * Math.Max(0.1, (DriftScale - from) / (DriftScale - 1))))
            { FillBehavior = FillBehavior.HoldEnd };
            _zoom.BeginAnimation(ScaleTransform.ScaleXProperty, drift);
            _zoom.BeginAnimation(ScaleTransform.ScaleYProperty, drift);
        }

        public void Pause()
        {
            double now = _zoom.ScaleX;
            _zoom.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _zoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _zoom.ScaleX = _zoom.ScaleY = now;
        }

        public void Release()
        {
            Pause();
            _image.Source = null;
        }

        public void Touch(Point normalized) { }
    }

    /// <summary>Open tables sliding in one by one, each with a breathing "seat free" ring.</summary>
    public sealed class TablesArtView : BillboardVectorArt
    {
        private static readonly Color[] Dots = { Color.FromRgb(0xff, 0x4f, 0xa8), Color.FromRgb(0x9b, 0x7b, 0xff), Color.FromRgb(0x5f, 0xe3, 0xff) };
        private static readonly Typeface Face = new(new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#Fredoka, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private readonly IReadOnlyList<string> _names;
        private readonly int _rows;

        public TablesArtView(IReadOnlyList<string> names, int rows = 3)
        {
            _names = names;
            _rows = Math.Clamp(rows, 1, 3);
        }

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Mix(Color.FromRgb(0x1b, 0x2a, 0x3a), Deep(Accent), 0.4));
            Motes(dc, w, h, t, Accent, 12, 31, new Rect(0.48, 0.05, 0.5, 0.9), 0.05, 0.005, 0.4);
            double x0 = w * 0.5, rw = w * 0.44, rh = h * 0.17;
            double dip = 1;
            try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
            var rowFill = Fill(Color.FromRgb(28, 29, 58), 0.94);
            var green = Color.FromRgb(0x3c, 0xff, 0x7d);
            for (int i = 0; i < _rows; i++)
            {
                double k = BackOut(t * 2.2 - i * 0.25, 1.2);
                double bob = Bob(t, h * 0.006, i * 1.9);
                double y = h * 0.16 + i * h * 0.23 + bob, off = (1 - k) * w * 0.08;
                var row = new Rect(x0 + off, y, rw, rh);
                dc.PushOpacity(Math.Clamp(k, 0, 1));
                Plate(dc, row, rh * 0.3, rowFill, h * 0.014 - bob * 0.4);
                // A thin hue line along the top edge: the table's own light.
                dc.DrawLine(Stroke(Accent, Math.Max(1, h * 0.004), 0.45), new Point(row.X + rh * 0.35, row.Y + h * 0.006), new Point(row.Right - rh * 0.35, row.Y + h * 0.006));

                var dot = i == 0 ? Accent : Dots[i % Dots.Length];
                var ac = new Point(row.X + rh * 0.55, row.Y + rh / 2);
                dc.DrawEllipse(Fill(dot), null, ac, rh * 0.28, rh * 0.28);
                dc.DrawEllipse(null, Bevel(rh * 0.05), ac, rh * 0.26, rh * 0.26);
                if (i < _names.Count)
                {
                    var ft = new FormattedText(_names[i], CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, rh * 0.32, Fill(Color.FromRgb(0xec, 0xea, 0xff)), dip)
                    { MaxTextWidth = Math.Max(1, rw - rh * 2.2), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                    dc.DrawText(ft, new Point(row.X + rh * 1.05, row.Y + rh / 2 - ft.Height / 2));
                }
                else
                {
                    var bar = Fill(Color.FromRgb(0x9d, 0x9b, 0xc6), 0.35);
                    dc.DrawRoundedRectangle(bar, null, new Rect(row.X + rh * 1.05, row.Y + rh * 0.3, rw * 0.32, rh * 0.14), rh * 0.07, rh * 0.07);
                    dc.DrawRoundedRectangle(bar, null, new Rect(row.X + rh * 1.05, row.Y + rh * 0.58, rw * 0.22, rh * 0.11), rh * 0.06, rh * 0.06);
                }
                // The taken seat, and the free one: a breathing ring plus a ripple going out.
                double sx = row.Right - rh * 0.95, sy = row.Y + rh / 2;
                dc.DrawEllipse(Fill(dot), null, new Point(sx, sy), rh * 0.13, rh * 0.13);
                var free = new Point(sx + rh * 0.42, sy);
                double p = 0.5 + 0.5 * Math.Sin(t * 4 + i);
                double rp = ((t + i * 0.55) % 1.8) / 1.8;
                dc.DrawEllipse(null, Stroke(green, rh * 0.03, 0.5 * (1 - rp)), free, rh * (0.13 + rp * 0.26), rh * (0.13 + rp * 0.26));
                dc.DrawEllipse(Fill(green, 0.12 + 0.12 * p), Stroke(green, rh * 0.05, 0.35 + 0.55 * p), free, rh * 0.13, rh * 0.13);
                var shape = new RectangleGeometry(row, rh * 0.3, rh * 0.3);
                Sheen(dc, shape, row, t, 4.8, 4.8 - i * 0.45, 0.55);
                dc.Pop();
            }
            Motes(dc, w, h, t, Colors.White, 5, 37, new Rect(0.5, 0.1, 0.48, 0.85), 0.1, 0.008, 0.3);
        }
    }

    /// <summary>A prize wheel turning slowly under a fixed lamp, rim bulbs chasing, the pointer ticking.</summary>
    public sealed class WheelArtView : BillboardVectorArt
    {
        private readonly (int Done, int Total) _progress;

        public WheelArtView((int Done, int Total) progress = default) => _progress = progress;

        private static readonly Color[] Slices =
        {
            Color.FromRgb(0xff, 0x4f, 0xa8), Color.FromRgb(0x9b, 0x7b, 0xff), Color.FromRgb(0xff, 0xc9, 0x4a), Color.FromRgb(0x5f, 0xe3, 0xff),
            Color.FromRgb(0xff, 0x4f, 0xa8), Color.FromRgb(0x3c, 0xff, 0x7d), Color.FromRgb(0x9b, 0x7b, 0xff), Color.FromRgb(0xff, 0xc9, 0x4a),
        };

        private const double Spin = 0.5;

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Color.FromRgb(0x2b, 0x1a, 0x3d));
            Motes(dc, w, h, t, Accent, 12, 41, new Rect(0.5, 0.05, 0.48, 0.9), 0.05, 0.005, 0.4);
            double bob = Bob(t, h * 0.007);
            bool pips = _progress.Total > 0;
            var c = new Point(w * 0.73, h * (pips ? 0.46 : 0.5) + bob);
            double r = h * (pips ? 0.35 : 0.38), a0 = t * Spin;

            // Shadow on the wall behind, down and right of the lamp.
            var ink = Color.FromRgb(4, 4, 12);
            dc.DrawEllipse(Fill(ink, 0.22), null, new Point(c.X + r * 0.07, c.Y + r * 0.1), r * 1.12, r * 1.12);
            dc.DrawEllipse(Fill(ink, 0.3), null, new Point(c.X + r * 0.035, c.Y + r * 0.05), r * 1.1, r * 1.1);
            Glow(dc, c, r * 1.35, Accent, 0.16);

            // The slices turn as one cached drawing; the light on them does not.
            dc.PushTransform(new MatrixTransform(Rot(a0, c)));
            dc.DrawDrawing(Layer("slices", w, h, d => PaintSlices(d, r)));
            dc.Pop();
            dc.PushTransform(new TranslateTransform(c.X, c.Y));
            dc.DrawDrawing(Layer("lamp", w, h, d =>
            {
                var lamp = new RadialGradientBrush();
                lamp.Center = lamp.GradientOrigin = new Point(0.3, 0.28);
                lamp.RadiusX = lamp.RadiusY = 0.9;
                lamp.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, 255, 255, 255), 0));
                lamp.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0.45));
                lamp.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.62));
                lamp.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 0, 0, 0), 1));
                lamp.Freeze();
                d.DrawEllipse(lamp, null, new Point(0, 0), r, r);
                d.DrawEllipse(null, Stroke(Color.FromRgb(0xff, 0xe9, 0xa8), h * 0.014), new Point(0, 0), r, r);
                d.DrawEllipse(null, Bevel(h * 0.01), new Point(0, 0), r * 1.0, r * 1.0);
                // The hub: a raised boss.
                d.DrawEllipse(Fill(Color.FromRgb(0x1c, 0x1d, 0x35)), null, new Point(0, 0), r * 0.2, r * 0.2);
                d.DrawEllipse(null, Bevel(r * 0.04), new Point(0, 0), r * 0.18, r * 0.18);
                d.DrawEllipse(Fill(Color.FromRgb(0xff, 0xe9, 0xa8)), null, new Point(0, 0), r * 0.07, r * 0.07);
                d.DrawEllipse(Fill(Colors.White, 0.6), null, new Point(-r * 0.025, -r * 0.025), r * 0.025, r * 0.025);
            }));
            dc.Pop();

            // Rim bulbs chasing; a lit bulb gets a small halo.
            int step = (int)Math.Floor(t * 4);
            var bulbOn = Fill(Color.FromRgb(0xff, 0xf6, 0xcf));
            var bulbOff = Fill(Color.FromRgb(0x7a, 0x63, 0x30));
            for (int i = 0; i < 16; i++)
            {
                double a = i * Math.PI / 8 - t * 0.3;
                bool on = (step + i) % 2 == 0;
                var bp = new Point(c.X + Math.Cos(a) * r * 1.08, c.Y + Math.Sin(a) * r * 1.08);
                if (on) dc.DrawEllipse(Fill(Color.FromRgb(0xff, 0xe9, 0xa8), 0.22), null, bp, h * 0.026, h * 0.026);
                dc.DrawEllipse(on ? bulbOn : bulbOff, null, bp, h * 0.011, h * 0.011);
            }

            // The pointer kicks each time a slice edge passes under it, then settles.
            double edge = Math.PI / 4 / Spin;
            double since = (t + edge * 0.5) % edge;
            double kick = Math.Exp(-since * 7) * Math.Sin(since * 26) * 14;
            var tip = new StreamGeometry();
            using (var ctx = tip.Open())
            {
                ctx.BeginFigure(new Point(0, r * 0.25), true, true);
                ctx.LineTo(new Point(-r * 0.1, 0), false, false);
                ctx.LineTo(new Point(r * 0.1, 0), false, false);
            }
            tip.Freeze();
            var pivot = new Point(c.X, c.Y - r * 1.2);
            var pm = Matrix.Identity;
            pm.Rotate(kick);
            pm.Translate(pivot.X, pivot.Y);
            var sm = pm;
            sm.Translate(r * 0.03, r * 0.05);
            dc.PushTransform(new MatrixTransform(sm));
            dc.DrawGeometry(Fill(ink, 0.4), null, tip);
            dc.Pop();
            dc.PushTransform(new MatrixTransform(pm));
            dc.DrawGeometry(Fill(Color.FromRgb(0xff, 0xc9, 0x4a)), Bevel(r * 0.02), tip);
            dc.Pop();

            // {done, total}: one pip per slot under the wheel; done ones pressed in and lit.
            if (_progress.Total > 0)
            {
                int n = Math.Min(_progress.Total, 12);
                double pip = h * 0.02, gap = pip * 3.2, x0 = c.X - (n - 1) * gap / 2, y = c.Y - bob + r * 1.27;
                var lit = Color.FromRgb(0x3c, 0xff, 0x7d);
                for (int i = 0; i < n; i++)
                {
                    bool done = i < _progress.Done;
                    var pr = new Rect(x0 + i * gap - pip, y - pip, pip * 2, pip * 2);
                    double pop = done ? BackOut(t * 2 - 0.4 - i * 0.12, 2) : 1;
                    if (done)
                    {
                        Plate(dc, pr, pip, Fill(Color.FromRgb(20, 22, 40)), 0, pressed: true);
                        dc.DrawEllipse(Fill(lit), null, new Point(pr.X + pip, pr.Y + pip), pip * 0.62 * pop, pip * 0.62 * pop);
                    }
                    else Plate(dc, pr, pip, Fill(Color.FromRgb(40, 38, 74)), pip * 0.35);
                }
            }
            Twinkles(dc, w, h, t, Color.FromRgb(0xff, 0xe9, 0xa8), 4, 43, new Rect(0.52, 0.06, 0.42, 0.86), 0.028);
        }

        private static Matrix Rot(double radians, Point c)
        {
            var m = Matrix.Identity;
            m.Rotate(radians * 180 / Math.PI);
            m.Translate(c.X, c.Y);
            return m;
        }

        private void PaintSlices(DrawingContext dc, double r)
        {
            var c = new Point(0, 0);
            for (int i = 0; i < Slices.Length; i++)
            {
                double s = i * Math.PI / 4, e = s + Math.PI / 4;
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(c, true, true);
                    ctx.LineTo(new Point(Math.Cos(s) * r, Math.Sin(s) * r), false, false);
                    ctx.ArcTo(new Point(Math.Cos(e) * r, Math.Sin(e) * r), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
                }
                g.Freeze();
                dc.DrawGeometry(Fill(Slices[i], 0.88), null, g);
                // A thin divider and a pale band near the rim give each slice a face.
                dc.DrawLine(Stroke(Color.FromRgb(0x1c, 0x12, 0x2a), r * 0.018, 0.55), c, new Point(Math.Cos(s) * r, Math.Sin(s) * r));
                double m = s + Math.PI / 8;
                dc.DrawEllipse(Fill(Colors.White, 0.35), null, new Point(Math.Cos(m) * r * 0.74, Math.Sin(m) * r * 0.74), r * 0.045, r * 0.045);
            }
        }
    }

    /// <summary>A four-arm spiral turning inward, motes drawn down into it, a slower ghost behind.</summary>
    public sealed class SpiralArtView : BillboardVectorArt
    {
        private readonly Color _second;

        public SpiralArtView(string? secondHex) => _second = ParseHue(secondHex, Color.FromRgb(0x9b, 0x7b, 0xff));

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Deep(Accent));
            var c = new Point(w * 0.72, h * 0.48 + Bob(t, h * 0.006));
            double R = h * 0.5;
            Glow(dc, c, R * 0.9, Accent, 0.14);

            // Far layer: a thin ghost of the spiral, turning slower (parallax).
            dc.PushTransform(new MatrixTransform(Turn(-t * 0.45 + 0.4, c, 1.08)));
            dc.DrawDrawing(Layer("ghost", w, h, d => Arms(d, R, Stroke(_second, h * 0.008, 0.28), Stroke(Accent, h * 0.008, 0.2))));
            dc.Pop();

            // Shadow of the arms, down and right, then the arms themselves.
            double turn = -t * 1.1;
            dc.PushTransform(new MatrixTransform(Turn(turn, new Point(c.X + h * 0.012, c.Y + h * 0.018), 1)));
            dc.DrawDrawing(Layer("shade", w, h, d =>
            {
                var ink = Stroke(Color.FromRgb(4, 4, 12), h * 0.024, 0.45);
                Arms(d, R, ink, ink);
            }));
            dc.Pop();
            dc.PushTransform(new MatrixTransform(Turn(turn, c, 1)));
            dc.DrawDrawing(Layer("arms", w, h, d =>
            {
                Arms(d, R, Stroke(Accent, h * 0.02, 0.9), Stroke(_second, h * 0.02, 0.9));
                // A pale line along each arm: the lamp catching its top edge.
                Arms(d, R, Stroke(Colors.White, h * 0.005, 0.28), Stroke(Colors.White, h * 0.005, 0.22), -0.04);
            }));
            dc.Pop();

            // Motes drawn in along the arms, quickening and fading as they reach the eye.
            if (Particles)
            {
                for (int i = 0; i < 16; i++)
                {
                    double life = (Hash(51, i, 1) + t * (0.12 + Hash(51, i, 2) * 0.08)) % 1.0;
                    double rr = R * 0.95 * (1 - EaseOut(life));
                    double th = Hash(51, i, 3) * Math.PI * 2 + life * Math.PI * 3 - t * 0.6;
                    var p = new Point(c.X + Math.Cos(th) * rr, c.Y + Math.Sin(th) * rr);
                    double a = Math.Sin(life * Math.PI) * 0.8;
                    double s = h * (0.004 + 0.006 * (rr / R));
                    dc.PushOpacity(a);
                    dc.DrawEllipse(Fill(i % 3 == 0 ? Colors.White : (i % 2 == 0 ? Accent : _second)), null, p, s, s);
                    dc.Pop();
                }
            }

            // The eye: a small core breathing.
            double breath = 0.5 + 0.5 * Math.Sin(t * 2.2);
            Glow(dc, c, h * (0.05 + 0.02 * breath), Colors.White, 0.35 + 0.25 * breath);
            dc.DrawEllipse(Fill(Mix(Accent, Colors.White, 0.7)), null, c, h * 0.012, h * 0.012);
            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.4), 3, 57, new Rect(0.5, 0.1, 0.46, 0.8), 0.026);
        }

        private static Matrix Turn(double radians, Point c, double scale)
        {
            var m = Matrix.Identity;
            m.Scale(scale, scale);
            m.Rotate(radians * 180 / Math.PI);
            m.Translate(c.X, c.Y);
            return m;
        }

        /// <summary>Four arms round the origin, alternating two pens.</summary>
        private static void Arms(DrawingContext dc, double R, Pen a, Pen b, double lean = 0)
        {
            for (int arm = 0; arm < 4; arm++)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    for (int k = 0; k <= 100; k++)
                    {
                        double s = k / 100.0, th = s * Math.PI * 5 + arm * Math.PI / 2 + lean, rr = s * R;
                        var p = new Point(Math.Cos(th) * rr, Math.Sin(th) * rr);
                        if (k == 0) ctx.BeginFigure(p, false, false);
                        else ctx.LineTo(p, true, false);
                    }
                }
                g.Freeze();
                dc.DrawGeometry(null, arm % 2 == 1 ? b : a, g);
            }
        }
    }

    /// <summary>A month of days on a sheet: counted days pressed in and lit, today lifted and ringed, a bar toward what is needed.</summary>
    public sealed class CalendarArtView : BillboardVectorArt
    {
        private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private const double LandedAt = 1.3;
        private readonly int _counted;
        private readonly int _needed;
        private readonly int _days;
        private readonly int _today;

        public CalendarArtView(CalendarInfo data)
        {
            _counted = data.Counted;
            _needed = data.Needed;
            _days = data.Days;
            _today = data.Today;
        }

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Color.FromRgb(0x33, 0x1a, 0x2e));
            Motes(dc, w, h, t, Accent, 10, 61, new Rect(0.46, 0.05, 0.52, 0.9), 0.05, 0.005, 0.35);
            const int cols = 7;
            double cell = h * 0.115, gx = w * 0.5, gy = h * 0.14, pitch = cell * 1.08;
            double dip = 1;
            try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
            double bob = Bob(t, h * 0.005);

            // The sheet the days sit on.
            int rows = (_days + cols - 1) / cols;
            double pad = cell * 0.32;
            var sheet = new Rect(gx - pad, gy - pad + bob, cols * pitch - cell * 0.08 + pad * 2, rows * pitch - cell * 0.08 + pad * 2 + (_needed > 0 ? cell * 0.62 : 0));
            Plate(dc, sheet, cell * 0.28, Fill(Color.FromRgb(0x1e, 0x16, 0x30), 0.92), h * 0.02);

            dc.PushTransform(new TranslateTransform(0, bob));
            if (t >= LandedAt)
                dc.DrawDrawing(Layer("cells", w, h, d => PaintCells(d, 1, cell, gx, gy, pitch, dip, skipToday: true)));
            else
                PaintCells(dc, t, cell, gx, gy, pitch, dip, skipToday: true);

            // Today: lifted off the sheet, ringed, a glint now and then.
            {
                int i = _today - 1;
                double lift = cell * 0.08 + Math.Max(0, Math.Sin(t * 2)) * cell * 0.06;
                var rect = new Rect(gx + (i % cols) * pitch, gy + (i / cols) * pitch - lift, cell, cell);
                bool on = _today <= _counted;
                Plate(dc, rect, cell * 0.2, on ? Fill(Accent, 0.95) : Fill(Color.FromRgb(58, 52, 96)), lift + cell * 0.06);
                dc.DrawRoundedRectangle(null, Stroke(Colors.White, cell * 0.07, 0.6 + 0.4 * Math.Sin(t * 4)), rect, cell * 0.2, cell * 0.2);
                Number(dc, _today, rect, cell, on, dip);
                double gp = (t % 3.2) / 3.2;
                if (gp < 0.3) Spark(dc, new Point(rect.Right - cell * 0.05, rect.Top + cell * 0.05), cell * 0.32 * Math.Sin(gp / 0.3 * Math.PI), Colors.White, 0.95, gp * 2);
            }

            if (_needed > 0)
            {
                double by = gy + rows * pitch + cell * 0.12, bw = cols * pitch - cell * 0.08, bh = cell * 0.26;
                var track = new Rect(gx, by, bw, bh);
                Plate(dc, track, bh / 2, Fill(Color.FromRgb(16, 14, 30), 0.95), 0, pressed: true);
                double share = Math.Clamp(_counted / (double)_needed, 0, 1) * EaseOut(t * 1.5);
                if (share > 0)
                {
                    var fill = new Rect(gx, by, Math.Max(bh, bw * share), bh);
                    var fillBrush = new LinearGradientBrush(Mix(Accent, Colors.White, 0.3), Mix(Accent, Color.FromRgb(20, 10, 20), 0.15), 90);
                    fillBrush.Freeze();
                    dc.DrawRoundedRectangle(fillBrush, null, fill, bh / 2, bh / 2);
                    Sheen(dc, new RectangleGeometry(fill, bh / 2, bh / 2), fill, t, 3.4, 0.6, 0.8);
                    Spark(dc, new Point(fill.Right - bh * 0.4, fill.Y + bh / 2), bh * (0.7 + 0.25 * Math.Sin(t * 5)), Colors.White, 0.8, t * 0.8);
                }
            }
            dc.Pop();
            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.4), 3, 67, new Rect(0.48, 0.08, 0.5, 0.84), 0.024);
        }

        /// <summary>Every day but today: counted ones pressed in and lit (they pop in at the start), the rest raised.</summary>
        private void PaintCells(DrawingContext dc, double t, double cell, double gx, double gy, double pitch, double dip, bool skipToday)
        {
            const int cols = 7;
            var counted = Fill(Accent, 0.88);
            var empty = Fill(Color.FromRgb(46, 42, 82), 0.92);
            for (int d = 1; d <= _days; d++)
            {
                if (skipToday && d == _today) continue;
                int i = d - 1;
                var rect = new Rect(gx + (i % cols) * pitch, gy + (i / cols) * pitch, cell, cell);
                bool on = d <= _counted;
                if (on)
                {
                    double k = BackOut(t * 2.4 - i * 0.03, 2);
                    if (k <= 0.01) { Plate(dc, rect, cell * 0.2, empty, cell * 0.06); Number(dc, d, rect, cell, false, dip); continue; }
                    dc.PushTransform(new ScaleTransform(k, k, rect.X + cell / 2, rect.Y + cell / 2));
                    Plate(dc, rect, cell * 0.2, counted, 0, pressed: true);
                    Number(dc, d, rect, cell, true, dip);
                    dc.Pop();
                }
                else
                {
                    Plate(dc, rect, cell * 0.2, empty, cell * 0.06);
                    Number(dc, d, rect, cell, false, dip);
                }
            }
        }

        private void Number(DrawingContext dc, int day, Rect rect, double cell, bool on, double dip)
        {
            var ft = new FormattedText(day.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, cell * 0.32,
                on ? Fill(Color.FromRgb(0x1a, 0x0b, 0x18)) : Fill(Color.FromRgb(0xb4, 0xb0, 0xdc)), dip);
            dc.DrawText(ft, new Point(rect.X + (cell - ft.Width) / 2, rect.Y + (cell - ft.Height) / 2));
        }
    }

    /// <summary>Four tiles; a pointer taps one and it presses in (on is pressed in) with a ring and a few glints.</summary>
    public sealed class TipArtView : BillboardVectorArt
    {
        private const double Beat = 1.4;

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Mix(Color.FromRgb(0x1b, 0x1d, 0x3a), Deep(Accent), 0.4));
            Motes(dc, w, h, t, Accent, 10, 71, new Rect(0.5, 0.05, 0.48, 0.9), 0.05, 0.005, 0.35);
            double s = h * 0.26, ox = w * 0.56, oy = h * 0.18;
            int beat = (int)Math.Floor(t / Beat);
            double k = (t - beat * Beat) / Beat;
            bool on = beat % 2 == 0;
            var lit = Fill(Accent, 0.78);
            var dim = Fill(Color.FromRgb(44, 44, 78), 0.95);
            for (int i = 0; i < 4; i++)
            {
                double bob = i == 1 ? 0 : Bob(t, h * 0.004, i * 2.1);
                var rect = new Rect(ox + (i % 2) * s * 1.12, oy + (i / 2) * s * 1.12 + bob, s, s);
                bool isLit = i == 1 ? on : i != 2;
                // The tapped tile sinks for a moment right on the beat, whichever way it flips.
                double press = i == 1 ? Math.Max(0, 1 - k / 0.12) * s * 0.03 : 0;
                if (isLit)
                {
                    rect.Offset(0, s * 0.02 + press);
                    Plate(dc, rect, s * 0.16, lit, 0, pressed: true);
                    dc.DrawRoundedRectangle(null, Stroke(Mix(Accent, Colors.White, 0.6), s * 0.025, 0.55), rect, s * 0.16, s * 0.16);
                    Glyph(dc, rect, s, i);
                }
                else
                {
                    rect.Offset(0, press);
                    Plate(dc, rect, s * 0.16, dim, s * 0.06 - press);
                }
            }

            // The pointer: drifts, dips on the tap, a ring goes out and a few glints fly when a tile lights.
            double tx = ox + s * 1.12 + s * 0.6, ty = oy + s * 0.6;
            double dipTap = Math.Max(0, 1 - k / 0.18);
            tx += Math.Sin(t * 0.9) * s * 0.04;
            ty += Math.Cos(t * 1.1) * s * 0.03 + dipTap * s * 0.03;
            dc.DrawEllipse(null, Stroke(Colors.White, s * 0.03, 1 - k), new Point(tx, ty), s * 0.1 + k * s * 0.35, s * 0.1 + k * s * 0.35);
            if (on && k < 0.45 && Particles)
            {
                for (int i = 0; i < 6; i++)
                {
                    double a = -Math.PI / 2 + (i - 2.5) * 0.55, d = s * (0.18 + EaseOut(k / 0.45) * 0.45);
                    Spark(dc, new Point(tx + Math.Cos(a) * d, ty + Math.Sin(a) * d), s * 0.06 * (1 - k / 0.45), i % 2 == 0 ? Colors.White : Mix(Accent, Colors.White, 0.4), 1 - k / 0.45, k * 4);
                }
            }
            var arrow = new StreamGeometry();
            using (var ctx = arrow.Open())
            {
                ctx.BeginFigure(new Point(0, 0), true, true);
                ctx.LineTo(new Point(0, s * 0.32), false, false);
                ctx.LineTo(new Point(s * 0.09, s * 0.24), false, false);
                ctx.LineTo(new Point(s * 0.2, s * 0.24), false, false);
            }
            arrow.Freeze();
            double scale = 1 - dipTap * 0.1;
            var shadow = new Matrix(scale, 0, 0, scale, tx + s * 0.035, ty + s * 0.05);
            dc.PushTransform(new MatrixTransform(shadow));
            dc.DrawGeometry(Fill(Color.FromRgb(4, 4, 12), 0.4), null, arrow);
            dc.Pop();
            dc.PushTransform(new MatrixTransform(new Matrix(scale, 0, 0, scale, tx, ty)));
            dc.DrawGeometry(Brushes.White, Stroke(Color.FromRgb(0x14, 0x12, 0x28), s * 0.018), arrow);
            dc.Pop();
            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.4), 3, 73, new Rect(0.52, 0.08, 0.44, 0.84), 0.024);
        }

        /// <summary>A small mark on a lit tile so each reads as a different switch.</summary>
        private void Glyph(DrawingContext dc, Rect rect, double s, int i)
        {
            var c = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            var ink = Fill(Color.FromRgb(0x16, 0x10, 0x2a), 0.55);
            switch (i)
            {
                case 0: dc.DrawEllipse(null, Stroke(Color.FromRgb(0x16, 0x10, 0x2a), s * 0.05, 0.55), c, s * 0.15, s * 0.15); break;
                case 1: dc.DrawGeometry(ink, null, new RectangleGeometry(new Rect(c.X - s * 0.13, c.Y - s * 0.13, s * 0.26, s * 0.26), s * 0.04, s * 0.04)); break;
                default:
                    dc.DrawLine(Stroke(Color.FromRgb(0x16, 0x10, 0x2a), s * 0.05, 0.55), new Point(c.X - s * 0.14, c.Y), new Point(c.X + s * 0.14, c.Y));
                    dc.DrawLine(Stroke(Color.FromRgb(0x16, 0x10, 0x2a), s * 0.05, 0.55), new Point(c.X, c.Y - s * 0.14), new Point(c.X, c.Y + s * 0.14));
                    break;
            }
        }
    }
}
