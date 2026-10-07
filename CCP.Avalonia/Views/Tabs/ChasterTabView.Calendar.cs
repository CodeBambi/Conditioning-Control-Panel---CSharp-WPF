// PORTED from ConditioningControlPanel/Views/Tabs/ChasterTabView.xaml.cs: the calendar (:434-560),
// CalendarSpan (:451), BuildCalendar (:466), Cell (:511), Cross (:576), on Core LockCalendar.
// ponytail: the cross is two straight marker strokes plus the splat (WPF bends them and dashes them
// in), the padlock stamp is a glyph not the padlock art mask, the key is the key glyph on gold not
// the foil sticker, tonight's ring a plain ellipse; the draw-in, sheet nudge, tonight's tag and its bob (Fx.cs) are not ported.
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
        private (DateTime Start, DateTime End, DateTime Today)? _calendarKey;

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
                Calendar.Children.Clear();
                CalendarRow.IsVisible = false;
                return;
            }
            var key = (lockSpan.Start.Date, lockSpan.End.Date, today.Date);
            if (_calendarKey == key && CalendarRow.IsVisible) return;   // a tick that changes nothing redraws nothing
            _calendarKey = key;
            Calendar.Children.Clear();
            var elided = LockCalendar.ElidedDays(lockSpan.Start, lockSpan.End);
            foreach (var day in LockCalendar.CellsFor(lockSpan.Start, lockSpan.End, today))
                Calendar.Children.Add(Cell(day, (day.Date - lockSpan.Start.Date).Days + 1, elided));
            CalendarRow.IsVisible = true;
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
                plate.Children.Add(new Border
                {
                    Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x52)),
                    Child = new Path { Data = KeyGlyph, Fill = CalInk, Stretch = Stretch.Uniform, Width = 24, Height = 10 },
                });
                tip = Loc.Get("chaster_chain_open");
            }
            else if (day.Served)
            {
                plate.Children.Add(Cross(dayNumber));
                tip = Loc.GetF("chaster_cal_served", dayNumber);
            }
            else if (day.Today)
            {
                plate.Children.Add(new Ellipse { Width = CellSize - 8, Height = CellSize - 10, Stroke = CalMarker, StrokeThickness = 2.4, Opacity = 0.9 });
                tip = Loc.Get("chaster_chain_tonight");
            }
            else
            {
                plate.Children.Add(new TextBlock
                {
                    Text = "\U0001F512", FontSize = 9, Opacity = 0.5, Margin = new Thickness(0, 0, 4, 3),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
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

        /// <summary>A crossed-out day in red marker, leaning a little differently day to day, with a splat.</summary>
        private static Canvas Cross(int seed)
        {
            var wobble = (seed % 3) - 1;
            var lean = ((seed * 7) % 5) - 2;
            double a = CrossInset + lean * 0.5, b = CellSize - CrossInset - lean * 0.5;
            var canvas = new Canvas { Width = CellSize, Height = CellSize, IsHitTestVisible = false };
            canvas.Children.Add(new Line { StartPoint = new Point(a, a + wobble), EndPoint = new Point(b, b - wobble), Stroke = CalMarker, StrokeThickness = 3, StrokeLineCap = PenLineCap.Round });
            var end = new Point(a + lean * 0.4, b);
            canvas.Children.Add(new Line { StartPoint = new Point(b - lean * 0.4, a), EndPoint = end, Stroke = CalMarker, StrokeThickness = 3, StrokeLineCap = PenLineCap.Round });
            var splat = new Ellipse { Width = 4.6, Height = 4.2, Fill = CalMarker, Opacity = 0.9 };
            Canvas.SetLeft(splat, end.X - 2.3 + lean * 0.3);
            Canvas.SetTop(splat, end.Y - 2.1 + wobble * 0.4);
            canvas.Children.Add(splat);
            return canvas;
        }
    }
}
