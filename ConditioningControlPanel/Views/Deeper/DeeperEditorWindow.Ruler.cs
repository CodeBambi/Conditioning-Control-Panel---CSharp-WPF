using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ConditioningControlPanel.Views.Deeper
{
    // Timeline timestamp ruler: a thin overlay strip at the top of the
    // TimelineCanvas with adaptive tick spacing. Major ticks carry a time
    // label (m:ss or h:mm:ss); minor ticks fill at 1/5 of major and stay
    // unlabeled. The interval auto-picks from a "nice numbers" preset list
    // (0.1s → 1h) so labels keep at least ~70px between them at any zoom
    // level. Renders inside TimelineCanvas (not a separate strip) to avoid
    // duplicating the ScrollViewer / horizontal-sync plumbing.
    public partial class DeeperEditorWindow
    {
        private readonly List<UIElement> _rulerVisuals = new();
        private const double RulerStripHeight = 14.0;
        private const double RulerMinLabelSpacingPx = 70.0;

        // "Nice" major-tick intervals in seconds. The picker finds the first
        // value ≥ the minimum spacing needed at the current zoom.
        private static readonly double[] NiceTickIntervalsSec =
        {
            0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30,
            60, 120, 300, 600, 1800, 3600, 7200, 18000, 36000
        };

        private void RebuildTimelineRuler()
        {
            try
            {
                ClearRulerVisuals();

                if (TimelineCanvas == null) return;
                var w = TimelineCanvas.ActualWidth;
                if (w <= 0 || _totalSeconds <= 0) return;

                // Semi-transparent strip behind the labels so regions sitting
                // under the top of the lane area don't make the text unreadable.
                var stripBrush = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0));
                stripBrush.Freeze();
                var strip = new Rectangle
                {
                    Width = w,
                    Height = RulerStripHeight,
                    Fill = stripBrush,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(strip, 0);
                Canvas.SetTop(strip, 0);
                Panel.SetZIndex(strip, 50);
                TimelineCanvas.Children.Add(strip);
                _rulerVisuals.Add(strip);

                var pxPerSec = w / _totalSeconds;
                if (pxPerSec <= 0) return;
                var minSecPerLabel = RulerMinLabelSpacingPx / pxPerSec;
                var majorSec = PickNiceTickInterval(minSecPerLabel);
                var minorSec = majorSec / 5.0;

                var majorBrush = new SolidColorBrush(Color.FromArgb(200, 230, 230, 230));
                majorBrush.Freeze();
                var minorBrush = new SolidColorBrush(Color.FromArgb(110, 230, 230, 230));
                minorBrush.Freeze();
                var labelBrush = new SolidColorBrush(Color.FromArgb(255, 235, 235, 235));
                labelBrush.Freeze();

                // Minor ticks first so majors render on top. Skip indices that
                // coincide with majors (avoid drawing two lines on the same x).
                if (minorSec > 0)
                {
                    int n = (int)Math.Ceiling(_totalSeconds / minorSec);
                    for (int i = 0; i <= n; i++)
                    {
                        double t = i * minorSec;
                        if (t > _totalSeconds) break;
                        if (i % 5 == 0) continue; // major
                        var x = (t / _totalSeconds) * w;
                        var line = new Line
                        {
                            X1 = x, X2 = x,
                            Y1 = RulerStripHeight - 4, Y2 = RulerStripHeight,
                            Stroke = minorBrush,
                            StrokeThickness = 1,
                            IsHitTestVisible = false
                        };
                        Panel.SetZIndex(line, 51);
                        TimelineCanvas.Children.Add(line);
                        _rulerVisuals.Add(line);
                    }
                }

                // Major ticks + labels.
                int m = (int)Math.Floor(_totalSeconds / majorSec);
                for (int i = 0; i <= m; i++)
                {
                    double t = i * majorSec;
                    var x = (t / _totalSeconds) * w;

                    var tick = new Line
                    {
                        X1 = x, X2 = x,
                        Y1 = 0, Y2 = RulerStripHeight,
                        Stroke = majorBrush,
                        StrokeThickness = 1,
                        IsHitTestVisible = false
                    };
                    Panel.SetZIndex(tick, 52);
                    TimelineCanvas.Children.Add(tick);
                    _rulerVisuals.Add(tick);

                    // Skip the t=0 label — it overlaps the lane-chrome border
                    // and the "0" carries no information the user doesn't get
                    // from the playhead's TxtCurrentTime readout. Skip the
                    // final tick label too if it would clip past the canvas
                    // edge (most labels fit; this avoids the rare overflow).
                    if (i == 0) continue;
                    var label = new TextBlock
                    {
                        Text = FormatRulerTime(t, majorSec),
                        Foreground = labelBrush,
                        FontSize = 9,
                        FontFamily = new FontFamily("Consolas"),
                        IsHitTestVisible = false
                    };
                    label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double labelW = label.DesiredSize.Width;
                    double labelX = x + 2;
                    if (labelX + labelW > w) labelX = Math.Max(0, x - labelW - 2);
                    Canvas.SetLeft(label, labelX);
                    Canvas.SetTop(label, 1);
                    Panel.SetZIndex(label, 53);
                    TimelineCanvas.Children.Add(label);
                    _rulerVisuals.Add(label);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: RebuildTimelineRuler error: {Error}", ex.Message);
            }
        }

        private void ClearRulerVisuals()
        {
            foreach (var v in _rulerVisuals)
            {
                try { TimelineCanvas.Children.Remove(v); } catch { }
            }
            _rulerVisuals.Clear();
        }

        private static double PickNiceTickInterval(double minSecondsPerTick)
        {
            foreach (var n in NiceTickIntervalsSec)
                if (n >= minSecondsPerTick) return n;
            return NiceTickIntervalsSec[^1];
        }

        private static string FormatRulerTime(double seconds, double intervalSec)
        {
            if (seconds < 0) seconds = 0;
            // Sub-second resolution: show e.g. "1.2s".
            if (intervalSec < 1.0)
                return seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
            var t = TimeSpan.FromSeconds(Math.Round(seconds));
            if (t.TotalHours >= 1)
                return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}";
            return $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
        }
    }
}
