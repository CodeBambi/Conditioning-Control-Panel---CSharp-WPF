// PORTED from ConditioningControlPanel/Views/Tabs/ChasterTabView.xaml.cs: the calendar (:434-560),
// CalendarSpan (:451), BuildCalendar (:466), Cell (:511), Cross (:576), on Core LockCalendar.
// Cross (bent strokes + splat), Ring (two passes of the pen), Sticker (gold foil + key), the padlock
// stamp (the padlock art as an ink mask) and tonight's tag under today's square are WPF's (:526-700).
// ponytail: the marker draw-in, the sheet flutter and the tag's bob (Fx.cs) are not ported.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ChasterTabView
    {
        internal const double CellSize = 44;
        private const double CrossInset = 10;
        private static readonly IBrush CalInk = new SolidColorBrush(Color.FromRgb(0x24, 0x1A, 0x2E));
        private static readonly IBrush CalMarker = new SolidColorBrush(Color.FromRgb(0xC8, 0x24, 0x4A));
        private static readonly IBrush CalRule = new SolidColorBrush(Color.FromArgb(0x38, 0x24, 0x1A, 0x2E));
        private static readonly FontFamily CalMono = new("Consolas, Courier New, monospace");
        private static readonly Geometry KeyGlyph = Geometry.Parse(
            "M 8,0 A 8,8 0 1 0 8,16 A 8,8 0 1 0 8,0 Z M 8,4.5 A 3.5,3.5 0 1 0 8,11.5 A 3.5,3.5 0 1 0 8,4.5 Z M 15,5.5 H 42 V 11 H 38.5 V 8.5 H 34.5 V 12.5 H 30.5 V 8.5 H 15 Z");
        private static readonly IBrush Foil = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0xFF, 0xF0, 0xB0), 0),
                new GradientStop(Color.FromRgb(0xE0, 0xB0, 0x52), 0.45),
                new GradientStop(Color.FromRgb(0xB8, 0x86, 0x2E), 0.8),
                new GradientStop(Color.FromRgb(0xF3, 0xD2, 0x7A), 1),
            },
        };
        private static IBrush? _padlockMask;
        private static bool _padlockMaskTried;
        private (DateTime Start, DateTime End, DateTime Today)? _calendarKey;
        private Control? _tonightCell;

        /// <summary>WPF CalendarSpan: the lock's days in local time; nothing without an end or with a hidden timer.</summary>
        internal static (DateTime Start, DateTime End)? CalendarSpan(LockSnapshot? snapshot, DateTime localToday)
        {
            if (snapshot is not { TimerHidden: false, EndsAtUtc: { } endUtc }) return null;
            var start = snapshot.StartedAtUtc is { } startUtc ? startUtc.ToLocalTime() : localToday;
            return (start, endUtc.ToLocalTime());
        }

        internal void BuildCalendar(LockSnapshot? snapshot) => BuildCalendar(CalendarSpan(snapshot, DateTime.Now), DateTime.Now);

        internal void BuildCalendar((DateTime Start, DateTime End)? span, DateTime today)
        {
            if (span is not { } lockSpan)
            {
                _calendarKey = null;
                _tonightCell = null;
                Calendar.Children.Clear();
                CalendarRow.IsVisible = false;
                CalendarTag.IsVisible = false;
                return;
            }
            var key = (lockSpan.Start.Date, lockSpan.End.Date, today.Date);
            if (_calendarKey == key && CalendarRow.IsVisible)   // a tick that changes nothing redraws nothing
            {
                RefreshTag(Platform.ChasterHead.Service?.BalanceSeconds ?? 0);
                return;
            }
            _calendarKey = key;
            _tonightCell = null;
            Calendar.Children.Clear();
            var elided = LockCalendar.ElidedDays(lockSpan.Start, lockSpan.End);
            foreach (var day in LockCalendar.CellsFor(lockSpan.Start, lockSpan.End, today))
            {
                var square = Cell(day, (day.Date - lockSpan.Start.Date).Days + 1, elided);
                Calendar.Children.Add(square);
                if (day.Today) _tonightCell = square;
            }
            CalendarRow.IsVisible = true;
            RefreshTag(Platform.ChasterHead.Service?.BalanceSeconds ?? 0);
            global::Avalonia.Threading.Dispatcher.UIThread.Post(PlaceCalendarTag, global::Avalonia.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>WPF PlaceCalendarTag (:690): park the tag under tonight's square, wherever the sheet put it.</summary>
        internal void PlaceCalendarTag()
        {
            try
            {
                if (_tonightCell is not { } cell || cell.Bounds.Width <= 0) return;
                if (cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height), CalendarRow) is not { } foot) return;
                CalendarTag.Margin = new Thickness(Math.Max(0, foot.X - CalendarTag.Bounds.Width / 2), foot.Y - 14, 0, 0);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] calendar tag"); }
        }

        /// <summary>Tonight's tag: what is on the tab, in red under today's square. Only while something is owed.</summary>
        private void PaintCalendarTag(int balance)
        {
            TxtCalendarTag.Text = CircesTab.Format(balance);
            CalendarTag.IsVisible = balance > 0 && _tonightCell != null;
        }

        /// <summary>A ruled square with its number; a cross (served), padlock (to serve), red ring (tonight) or the key.</summary>
        private static Border Cell(LockDay day, int dayNumber, int elided)
        {
            var plate = new Panel { Width = CellSize, Height = CellSize };
            var square = new Border { BorderBrush = CalRule, BorderThickness = new Thickness(0, 0, 1, 1), Child = plate, Background = Brushes.Transparent };
            if (!day.IsKey)
                plate.Children.Add(new TextBlock
                {
                    Text = day.DayOfMonth.ToString(), FontSize = 10.5, FontWeight = FontWeight.Bold, FontFamily = CalMono,
                    Foreground = CalInk, Opacity = day.Served ? 0.5 : 0.8, Margin = new Thickness(4, 2, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                });

            string tip;
            if (day.IsKey)
            {
                plate.Children.Add(Sticker());
                tip = Loc.Get("chaster_chain_open");
            }
            else if (day.Served)
            {
                plate.Children.Add(Cross(dayNumber));
                tip = Loc.GetF("chaster_cal_served", dayNumber);
            }
            else if (day.Today)
            {
                plate.Children.Add(Ring(dayNumber));
                tip = Loc.Get("chaster_chain_tonight");
            }
            else
            {
                plate.Children.Add(new Rectangle
                {
                    Width = 8, Height = 11, Fill = CalInk, Opacity = 0.5, OpacityMask = PadlockMask(),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 5, 4),
                });
                tip = Loc.GetF("chaster_cal_locked", dayNumber);
            }

            if (day.Elided)
            {
                // the first square shown stands for every day before it
                plate.Children.Add(new TextBlock
                {
                    Text = "...", FontSize = 11, FontWeight = FontWeight.Bold, FontFamily = CalMono, Foreground = CalInk, Opacity = 0.7,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 4, 0),
                });
                tip = Loc.GetF("chaster_cal_elided", elided);
            }
            ToolTip.SetTip(square, tip);
            return square;
        }

        /// <summary>A crossed-out day in red marker: two strokes that bend a little and do not quite
        /// meet, different from one day to the next, with a splat where the pen lifted.</summary>
        private static Canvas Cross(int seed)
        {
            var wobble = (seed % 3) - 1;          // -1, 0, 1
            var lean = ((seed * 7) % 5) - 2;      // -2 .. 2
            double a = CrossInset + lean * 0.5, b = CellSize - CrossInset - lean * 0.5, mid = CellSize / 2;
            var canvas = new Canvas { Width = CellSize, Height = CellSize, IsHitTestVisible = false };
            var end = new Point(a + lean * 0.4, b);
            canvas.Children.Add(MarkerStroke(new Point(a, a + wobble), new Point(mid + wobble * 0.8, mid - 0.6), new Point(b, b - wobble)));
            canvas.Children.Add(MarkerStroke(new Point(b - lean * 0.4, a), new Point(mid - wobble * 0.7, mid + 0.8), end));
            var splat = new Ellipse { Width = 4.6, Height = 4.2, Fill = CalMarker, Opacity = 0.9 };
            Canvas.SetLeft(splat, end.X - 2.3 + lean * 0.3);
            Canvas.SetTop(splat, end.Y - 2.1 + wobble * 0.4);
            canvas.Children.Add(splat);
            return canvas;
        }

        private static Polyline MarkerStroke(params Point[] points) => new()
        {
            Points = points, Stroke = CalMarker, StrokeThickness = 3, StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round, Opacity = 0.9,
        };

        /// <summary>Tonight's number ringed in red ink: two passes of a slightly lopsided ellipse,
        /// the second lighter, the way a pen goes round twice.</summary>
        private static Canvas Ring(int seed)
        {
            var canvas = new Canvas { Width = CellSize, Height = CellSize, IsHitTestVisible = false };
            var tilt = -9 + (seed % 4) * 2;
            var first = new EllipseGeometry { Center = new Point(12.5, 9.5), RadiusX = 11.5, RadiusY = 8.2, Transform = new RotateTransform(tilt, 12.5, 9.5) };
            var second = new EllipseGeometry { Center = new Point(13.2, 10), RadiusX = 12, RadiusY = 7.6, Transform = new RotateTransform(tilt + 11, 13.2, 10) };
            canvas.Children.Add(new Path { Data = first, Stroke = CalMarker, StrokeThickness = 1.9, Opacity = 0.9 });
            canvas.Children.Add(new Path { Data = second, Stroke = CalMarker, StrokeThickness = 1.2, Opacity = 0.5 });
            return canvas;
        }

        /// <summary>The day it opens: a gold foil sticker with the key pressed into it.</summary>
        private static Border Sticker() => new()
        {
            Width = 30, Height = 19, CornerRadius = new CornerRadius(4), Background = Foil,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xF4, 0xC0)), BorderThickness = new Thickness(0.8),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), RenderTransform = new RotateTransform(-7),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 1, Blur = 4, Color = Color.FromArgb(0x66, 0, 0, 0) }),
            Child = new Path
            {
                Data = KeyGlyph, Fill = new SolidColorBrush(Color.FromRgb(0x4A, 0x32, 0x10)), Stretch = Stretch.Uniform, Width = 20, Height = 8,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85,
            },
        };

        /// <summary>The title's padlock, as the mask an ink rectangle is stamped through.</summary>
        private static IBrush? PadlockMask()
        {
            if (_padlockMaskTried) return _padlockMask;
            _padlockMaskTried = true;
            if (Helpers.ModArt.TryLoad(global::ConditioningControlPanel.Avalonia.Controls.LockTitle.PadlockArt, 64) is { } art)
                _padlockMask = new ImageBrush(art) { Stretch = Stretch.Uniform };
            return _padlockMask;
        }
    }
}
