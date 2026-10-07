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
            Ground(dc, w, h, Color.FromRgb(0x1b, 0x2a, 0x3a));
            double x0 = w * 0.5, rw = w * 0.44, rh = h * 0.17;
            double dip = 1;
            try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
            var rowFill = Solid(Color.FromRgb(28, 29, 58), 0.92);
            var rowRim = Stroke(Color.FromRgb(0x9b, 0x7b, 0xff), Math.Max(1, h * 0.004), 0.35);
            var green = Color.FromRgb(0x3c, 0xff, 0x7d);
            for (int i = 0; i < _rows; i++)
            {
                double k = EaseOut(t * 2.2 - i * 0.25);
                double y = h * 0.16 + i * h * 0.23, off = (1 - k) * w * 0.08;
                dc.PushOpacity(k);
                dc.DrawRoundedRectangle(rowFill, rowRim, new Rect(x0 + off, y, rw, rh), rh * 0.3, rh * 0.3);
                var dot = Dots[i % Dots.Length];
                dc.DrawEllipse(Solid(dot), null, new Point(x0 + off + rh * 0.55, y + rh / 2), rh * 0.28, rh * 0.28);
                if (i < _names.Count)
                {
                    var ft = new FormattedText(_names[i], CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, rh * 0.32, Solid(Color.FromRgb(0xec, 0xea, 0xff)), dip)
                    { MaxTextWidth = Math.Max(1, rw - rh * 2.2), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                    dc.DrawText(ft, new Point(x0 + off + rh * 1.05, y + rh / 2 - ft.Height / 2));
                }
                else
                {
                    var bar = Solid(Color.FromRgb(0x9d, 0x9b, 0xc6), 0.35);
                    dc.DrawRoundedRectangle(bar, null, new Rect(x0 + off + rh * 1.05, y + rh * 0.3, rw * 0.32, rh * 0.14), rh * 0.07, rh * 0.07);
                    dc.DrawRoundedRectangle(bar, null, new Rect(x0 + off + rh * 1.05, y + rh * 0.58, rw * 0.22, rh * 0.11), rh * 0.06, rh * 0.06);
                }
                double sx = x0 + off + rw - rh * 0.95, sy = y + rh / 2;
                dc.DrawEllipse(Solid(dot), null, new Point(sx, sy), rh * 0.13, rh * 0.13);
                double p = 0.5 + 0.5 * Math.Sin(t * 4 + i);
                dc.DrawEllipse(null, Stroke(green, rh * 0.05, 0.35 + 0.55 * p), new Point(sx + rh * 0.42, sy), rh * 0.13, rh * 0.13);
                dc.Pop();
            }
        }
    }

    /// <summary>A prize wheel turning slowly, rim bulbs chasing round it.</summary>
    public sealed class WheelArtView : BillboardVectorArt
    {
        private readonly (int Done, int Total) _progress;

        public WheelArtView((int Done, int Total) progress = default) => _progress = progress;

        private static readonly Color[] Slices =
        {
            Color.FromRgb(0xff, 0x4f, 0xa8), Color.FromRgb(0x9b, 0x7b, 0xff), Color.FromRgb(0xff, 0xc9, 0x4a), Color.FromRgb(0x5f, 0xe3, 0xff),
            Color.FromRgb(0xff, 0x4f, 0xa8), Color.FromRgb(0x3c, 0xff, 0x7d), Color.FromRgb(0x9b, 0x7b, 0xff), Color.FromRgb(0xff, 0xc9, 0x4a),
        };

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            Ground(dc, w, h, Color.FromRgb(0x2b, 0x1a, 0x3d));
            var c = new Point(w * 0.73, h * 0.5);
            double r = h * 0.38, a0 = t * 0.5;
            Glow(dc, c, r * 1.35, Color.FromRgb(0xff, 0x4f, 0xa8), 0.32);
            for (int i = 0; i < Slices.Length; i++)
            {
                double s = a0 + i * Math.PI / 4, e = s + Math.PI / 4;
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(c, true, true);
                    ctx.LineTo(new Point(c.X + Math.Cos(s) * r, c.Y + Math.Sin(s) * r), false, false);
                    ctx.ArcTo(new Point(c.X + Math.Cos(e) * r, c.Y + Math.Sin(e) * r), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
                }
                g.Freeze();
                dc.DrawGeometry(Solid(Slices[i], 0.82), null, g);
            }
            dc.DrawEllipse(null, Stroke(Color.FromRgb(0xff, 0xe9, 0xa8), h * 0.012), c, r, r);
            int step = (int)Math.Floor(t * 4);
            for (int i = 0; i < 16; i++)
            {
                double a = i * Math.PI / 8 - t * 0.3;
                bool on = (step + i) % 2 == 0;
                dc.DrawEllipse(Solid(on ? Color.FromRgb(0xff, 0xf6, 0xcf) : Color.FromRgb(0x7a, 0x63, 0x30)), null,
                    new Point(c.X + Math.Cos(a) * r * 1.08, c.Y + Math.Sin(a) * r * 1.08), h * 0.011, h * 0.011);
            }
            dc.DrawEllipse(Solid(Color.FromRgb(0x1c, 0x1d, 0x35)), null, c, r * 0.2, r * 0.2);
            var tip = new StreamGeometry();
            using (var ctx = tip.Open())
            {
                ctx.BeginFigure(new Point(c.X, c.Y - r * 0.95), true, true);
                ctx.LineTo(new Point(c.X - r * 0.09, c.Y - r * 1.2), false, false);
                ctx.LineTo(new Point(c.X + r * 0.09, c.Y - r * 1.2), false, false);
            }
            tip.Freeze();
            dc.DrawGeometry(Solid(Color.FromRgb(0xff, 0xc9, 0x4a)), null, tip);

            // {done, total}: one pip per slot under the wheel, the done ones lit.
            if (_progress.Total > 0)
            {
                int n = Math.Min(_progress.Total, 12);
                double pip = h * 0.018, gap = pip * 3.2, x0 = c.X - (n - 1) * gap / 2, y = c.Y + r * 1.24;
                for (int i = 0; i < n; i++)
                {
                    bool done = i < _progress.Done;
                    dc.DrawEllipse(done ? Solid(Color.FromRgb(0x3c, 0xff, 0x7d)) : Solid(Color.FromRgb(40, 38, 74), 0.9), null, new Point(x0 + i * gap, y), pip, pip);
                }
            }
        }
    }

    /// <summary>A four-arm spiral turning inward, in the card's hue and a second hue.</summary>
    public sealed class SpiralArtView : BillboardVectorArt
    {
        private readonly Color _second;

        public SpiralArtView(string? secondHex) => _second = ParseHue(secondHex, Color.FromRgb(0x9b, 0x7b, 0xff));

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            Ground(dc, w, h, Deep(Accent));
            var c = new Point(w * 0.72, h * 0.48);
            double R = h * 0.5;
            Glow(dc, c, R * 0.9, Accent, 0.18);
            for (int arm = 0; arm < 4; arm++)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    for (int k = 0; k <= 100; k++)
                    {
                        double s = k / 100.0, th = s * Math.PI * 5 + arm * Math.PI / 2 - t * 1.1, rr = s * R;
                        var p = new Point(c.X + Math.Cos(th) * rr, c.Y + Math.Sin(th) * rr);
                        if (k == 0) ctx.BeginFigure(p, false, false);
                        else ctx.LineTo(p, true, false);
                    }
                }
                g.Freeze();
                dc.DrawGeometry(null, Stroke(arm % 2 == 1 ? _second : Accent, h * 0.02, 0.85), g);
            }
        }
    }

    /// <summary>A month of days: counted days filled, today ringed, a bar toward what is needed.</summary>
    public sealed class CalendarArtView : BillboardVectorArt
    {
        private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
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
            Ground(dc, w, h, Color.FromRgb(0x33, 0x1a, 0x2e));
            const int cols = 7;
            double cell = h * 0.115, gx = w * 0.5, gy = h * 0.14, pitch = cell * 1.08;
            double dip = 1;
            try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
            int today = _today, days = _days;
            var counted = Solid(Accent, 0.85);
            var empty = Solid(Color.FromRgb(40, 38, 74), 0.8);
            for (int d = 1; d <= days; d++)
            {
                int i = d - 1;
                var rect = new Rect(gx + (i % cols) * pitch, gy + (i / cols) * pitch, cell, cell);
                bool on = d <= _counted;
                dc.DrawRoundedRectangle(on ? counted : empty, null, rect, cell * 0.2, cell * 0.2);
                if (d == today)
                    dc.DrawRoundedRectangle(null, Stroke(Colors.White, cell * 0.07, 0.6 + 0.4 * Math.Sin(t * 4)), rect, cell * 0.2, cell * 0.2);
                var ft = new FormattedText(d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, cell * 0.32,
                    on ? Solid(Color.FromRgb(0x1a, 0x0b, 0x18)) : Solid(Color.FromRgb(0x9d, 0x9b, 0xc6)), dip);
                dc.DrawText(ft, new Point(rect.X + (cell - ft.Width) / 2, rect.Y + (cell - ft.Height) / 2));
            }
            if (_needed > 0)
            {
                double by = gy + 5 * pitch + cell * 0.15, bw = cols * pitch - cell * 0.08;
                dc.DrawRoundedRectangle(Solid(Color.FromRgb(40, 38, 74), 0.9), null, new Rect(gx, by, bw, cell * 0.22), cell * 0.11, cell * 0.11);
                double share = Math.Clamp(_counted / (double)_needed, 0, 1) * EaseOut(t * 1.5);
                if (share > 0)
                    dc.DrawRoundedRectangle(Solid(Accent), null, new Rect(gx, by, Math.Max(cell * 0.22, bw * share), cell * 0.22), cell * 0.11, cell * 0.11);
            }
        }
    }

    /// <summary>Four tiles, one blinking on and off under a click ring and a pointer.</summary>
    public sealed class TipArtView : BillboardVectorArt
    {
        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            Ground(dc, w, h, Color.FromRgb(0x1b, 0x1d, 0x3a));
            double s = h * 0.26, ox = w * 0.56, oy = h * 0.18;
            bool on = Math.Floor(t / 1.4) % 2 == 0;
            var lit = Solid(Accent, 0.55);
            var dim = Solid(Color.FromRgb(40, 40, 70), 0.9);
            var rim = Stroke(Color.FromRgb(0xc7, 0xb6, 0xff), s * 0.03);
            for (int i = 0; i < 4; i++)
            {
                var rect = new Rect(ox + (i % 2) * s * 1.12, oy + (i / 2) * s * 1.12, s, s);
                bool isLit = i == 1 ? on : i != 2;
                dc.DrawRoundedRectangle(isLit ? lit : dim, isLit ? rim : null, rect, s * 0.16, s * 0.16);
            }
            double tx = ox + s * 1.12 + s * 0.6, ty = oy + s * 0.6, k = (t % 1.4) / 1.4;
            dc.DrawEllipse(null, Stroke(Colors.White, s * 0.03, 1 - k), new Point(tx, ty), s * 0.1 + k * s * 0.35, s * 0.1 + k * s * 0.35);
            var arrow = new StreamGeometry();
            using (var ctx = arrow.Open())
            {
                ctx.BeginFigure(new Point(tx, ty), true, true);
                ctx.LineTo(new Point(tx, ty + s * 0.32), false, false);
                ctx.LineTo(new Point(tx + s * 0.09, ty + s * 0.24), false, false);
                ctx.LineTo(new Point(tx + s * 0.2, ty + s * 0.24), false, false);
            }
            arrow.Freeze();
            dc.DrawGeometry(Brushes.White, null, arrow);
        }
    }
}
