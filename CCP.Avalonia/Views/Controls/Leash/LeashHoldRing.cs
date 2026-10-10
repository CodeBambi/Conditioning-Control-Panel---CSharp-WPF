// PORTED from WPF 7.1.5 Controls/Leash/LeashHoldRing.cs: the hold-to-cut countdown. While the
// panic key is held on a leashed account, a small topmost pill near the bottom of the primary
// screen counts 5..1 round a ring, so the hold can be seen and not only heard. It never takes focus
// or clicks and has no animation of its own: number and ring move once a second (reads the same
// with motion off). Gone on key-up and when the question comes up. The panic-hold hook that calls
// ShowHeld / Dismiss is r11's (panic-hold Ask path); this file is only the pill.
// Also carries the minutes ring art (WPF LeashLook.Ring) the holder card draws; fold it into
// LeashLook after the wave merges. No glow Effect on the arc (Avalonia effect/cache rule).
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal sealed class LeashHoldRing : Window
{
    private static LeashHoldRing? _open;
    private readonly Border _ringHost = new() { Width = 46, Height = 46 };
    private int _shown = -1;

    /// <summary>Test seam: false keeps the pill off screen (headless tests paint it without showing).</summary>
    internal static bool ShowWindows { get; set; } = true;

    /// <summary>Show (or move) the ring for a hold that has run <paramref name="held"/>.</summary>
    public static void ShowHeld(TimeSpan held)
    {
        try
        {
            _open ??= Create();
            _open.Paint(held);
        }
        catch (Exception ex) { Serilog.Log.Debug("Leash hold ring failed: {E}", ex.Message); }
    }

    public static void Dismiss()
    {
        var w = _open;
        _open = null;
        try { w?.Close(); } catch { }
    }

    internal static bool IsShown => _open != null;
    internal static int Number => _open?._shown ?? -1;

    private static LeashHoldRing Create()
    {
        var w = new LeashHoldRing();
        if (ShowWindows) w.Show();
        return w;
    }

    private LeashHoldRing()
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = Loc.Get("leash_title");

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 16, 8) };
        row.Children.Add(_ringHost);
        var words = FriendsDrawer.Label(Loc.Get("leash_hold_ring"), 14, FriendsDrawer.Text, FriendsDrawer.Display, FontWeight.SemiBold);
        words.Margin = new Thickness(10, 0, 0, 0);
        row.Children.Add(words);
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1A, 0x0E, 0x2E)),
            BorderBrush = FriendsDrawer.Mint,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(999),
            Child = row,
            Tag = "leash-hold-ring",
        };

        Opened += (_, _) =>
        {
            ClickThrough();
            try
            {
                if (Screens.Primary is { } s)
                {
                    var wa = s.WorkingArea;
                    var scale = s.Scaling;
                    var w = (int)(Bounds.Width * scale);
                    var h = (int)(Bounds.Height * scale);
                    Position = new PixelPoint(wa.X + (wa.Width - w) / 2, wa.Bottom - h - (int)(48 * scale));
                }
            }
            catch { }
        };
    }

    /// <summary>WPF sets WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW so the pill never
    /// takes a click or focus. Windows only; elsewhere IsHitTestVisible + ShowActivated carry it.</summary>
    private void ClickThrough()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (TryGetPlatformHandle()?.Handle is not { } h || h == IntPtr.Zero) return;
            var ex = GetWindowLong(h, GWL_EXSTYLE);
            SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
        }
        catch { }
    }

    private void Paint(TimeSpan held)
    {
        var n = LeashHoldTick.SecondsLeft(held);
        if (n == _shown) return;
        _shown = n;
        _ringHost.Child = Ring(LeashHoldTick.Fraction(held), n.ToString(CultureInfo.InvariantCulture), Color.FromRgb(0x5F, 0xFF, 0xD0), 46);
    }

    /// <summary>WPF LeashLook.Ring: a track, an arc for <paramref name="fraction"/>, the figure inside.</summary>
    internal static Control Ring(double fraction, string figure, Color color, double size = 50)
    {
        var g = new Grid { Width = size, Height = size, Tag = "leash-ring" };
        double stroke = size * 0.11, r = (size - stroke) / 2;
        g.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)), StrokeThickness = stroke, Margin = new Thickness(stroke / 2) });
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction > 0.001)
        {
            g.Children.Add(new Path
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = stroke,
                StrokeLineCap = PenLineCap.Round,
                Data = ArcGeometry(size / 2, size / 2, r, fraction),
                Tag = "leash-ring-arc",
            });
        }
        g.Children.Add(new TextBlock
        {
            Text = figure,
            FontFamily = FriendsDrawer.Mono,
            FontWeight = FontWeight.Bold,
            FontSize = size * 0.28,
            Foreground = FriendsDrawer.Text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return g;
    }

    private static Geometry ArcGeometry(double cx, double cy, double r, double fraction)
    {
        if (fraction >= 0.999) return new EllipseGeometry(new Rect(cx - r, cy - r, r * 2, r * 2));
        double a = fraction * Math.PI * 2;
        var fig = new PathFigure { StartPoint = new Point(cx, cy - r), IsClosed = false, IsFilled = false, Segments = new PathSegments() };
        fig.Segments.Add(new ArcSegment
        {
            Point = new Point(cx + r * Math.Sin(a), cy - r * Math.Cos(a)),
            Size = new Size(r, r),
            IsLargeArc = fraction > 0.5,
            SweepDirection = SweepDirection.Clockwise,
        });
        var geo = new PathGeometry { Figures = new PathFigures() };
        geo.Figures.Add(fig);
        return geo;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
