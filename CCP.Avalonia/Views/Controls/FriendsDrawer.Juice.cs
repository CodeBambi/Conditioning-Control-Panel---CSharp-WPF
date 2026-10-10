// PORTED from ConditioningControlPanel/Controls/Friends/FriendsDrawer.Juice.cs (7.1.5): the drawer's
// juice after the launcher's recipe. The card springs up from the chip, rows stagger in, a sheen
// crosses a row on hover, online dots breathe, a friend who comes online hops, a sent chip pops.
// Every move reads the motion level: Full plays it, Reduced at half the size, Off leaves it still.
// Avalonia rules: transforms move through TransformTween (never Animation.RunAsync on a Transform),
// the breathing dots ride one BreathClock (never a Forever Animation), opacity stays inside 0..1.
// Sparks and the Add shockwave draw on the drawer's own fx Canvas; their glow is a BoxShadow, never an Effect.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    /// <summary>Test seam: the motion level (WPF MotionFx.Level).</summary>
    internal static Func<MotionLevel> MotionLevelNow { get; set; } = () => AmbientFxCanvas.Env.Level;
    /// <summary>1 at Full, 0.5 at Reduced, 0 at Off: every amplitude is multiplied by it.</summary>
    internal static double Amount => MotionLevelNow() switch { MotionLevel.Off => 0, MotionLevel.Reduced => 0.5, _ => 1 };

    private readonly Dictionary<string, Control> _avatars = new();
    private readonly List<Control> _rowsInOrder = new();
    private BreathClock? _breath;
    /// <summary>WPF FriendsLook.Glow as a BoxShadow: no offset, the colour at the glow's opacity.</summary>
    internal static BoxShadows CountGlow(Color c, double blur = 8, double opacity = 0.7) =>
        new(new BoxShadow { Blur = blur, Color = Color.FromArgb((byte)Math.Round(255 * opacity), c.R, c.G, c.B) });

    internal static readonly Color MintC = Friends.FriendsLook.Mint, PinkC = Friends.FriendsLook.Pink, LilacC = Friends.FriendsLook.Lilac, GoldC = Friends.FriendsLook.Gold;

    /// <summary>The spring the card opens with: up from the chip, a little overshoot, rows staggering in.</summary>
    private void PlayEntrance()
    {
        double k = Amount;
        if (k <= 0 || AsPage) { Opacity = 1; RenderTransform = null; return; }
        RenderTransformOrigin = new RelativePoint(0, 1, RelativeUnit.Relative);
        var scale = new ScaleTransform(1, 1);
        var slide = new TranslateTransform();
        RenderTransform = new TransformGroup { Children = { scale, slide } };
        var ms = TimeSpan.FromMilliseconds(220 / Math.Max(k, 0.5) * (k < 1 ? 0.8 : 1));
        var ease = new BackEaseOut();
        TransformTween.Run(scale, ms, new (double, AvaloniaProperty, double)[]
        {
            (0, ScaleTransform.ScaleXProperty, 1 - 0.06 * k), (1, ScaleTransform.ScaleXProperty, 1),
            (0, ScaleTransform.ScaleYProperty, 1 - 0.06 * k), (1, ScaleTransform.ScaleYProperty, 1),
        }, ease);
        TransformTween.Run(slide, ms, new (double, AvaloniaProperty, double)[] { (0, TranslateTransform.YProperty, 14 * k), (1, TranslateTransform.YProperty, 0) }, ease);
        TransformTween.Run(this, TimeSpan.FromMilliseconds(160), new (double, AvaloniaProperty, double)[] { (0, OpacityProperty, 0), (1, OpacityProperty, 1) });
        int i = 0;
        foreach (var row in _rowsInOrder) StaggerIn(row, TimeSpan.FromMilliseconds(40 * Math.Min(i++, 6)));
    }

    /// <summary>WPF MotionFx.StaggerIn for one row: fade in from a 10 px rise after a delay.</summary>
    private static void StaggerIn(Control row, TimeSpan delay)
    {
        var rise = row.RenderTransform as TranslateTransform ?? new TranslateTransform();
        row.RenderTransform = rise;
        row.Opacity = 0;
        rise.Y = 10;
        DateTime? start = null;
        global::Avalonia.Threading.DispatcherTimer? timer = null;
        timer = new global::Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(16), global::Avalonia.Threading.DispatcherPriority.Render, (_, _) =>
        {
            start ??= DateTime.UtcNow + delay;
            var ms = (DateTime.UtcNow - start.Value).TotalMilliseconds;
            if (ms < 0) return;
            double o = Math.Min(1, ms / 220), y = Math.Min(1, ms / 260);
            row.Opacity = 1 - (1 - o) * (1 - o);
            rise.Y = 10 * (1 - y) * (1 - y);
            if (o >= 1 && y >= 1) timer!.Stop();
        });
        timer.Start();
    }

    /// <summary>Breathes every online dot in the list (Full only, like the app's other loops); stopped
    /// whenever the list is rebuilt or the drawer folds.</summary>
    private void StartAmbient()
    {
        StopAmbient();
        if (MotionLevelNow() != MotionLevel.Full) return;
        var dots = new List<(Visual, double, double)>();
        foreach (var av in _avatars.Values)
            if (av is Panel g) foreach (var c in g.Children) if (c is Control e && e.Tag is "friends-dot-on") dots.Add((e, 0.45, 1.0));
        if (dots.Count == 0) return;
        _breath ??= new BreathClock(this, 1.8);
        _breath.Start(dots.ToArray());
    }

    private void StopAmbient()
    {
        if (_breath == null) return;
        _breath.Stop();
        foreach (var av in _avatars.Values)
            if (av is Panel g) foreach (var c in g.Children) if (c is Control e && e.Tag is "friends-dot-on") e.Opacity = 1;
    }

    /// <summary>The hover wash and a soft lilac band that crosses the row once on the way in. The band
    /// lives in an overlay grid over the row's content and is never hit-testable.</summary>
    private void AttachSheen(Border row, bool open)
    {
        var rest = row.Background;
        if (row.Child is { } content && row.Child is not SheenLayer)
        {
            row.Child = null;
            row.Child = new SheenLayer { Children = { content } };
        }
        row.PointerEntered += (_, _) => { if (!open) row.Background = HoverWash; Sweep(row); };
        row.PointerExited += (_, _) => { if (!open) row.Background = rest; };
    }

    /// <summary>WPF FriendsLook.HoverBrush.</summary>
    internal static readonly IBrush HoverWash = Friends.FriendsLook.HoverBrush;

    private sealed class SheenLayer : Grid { }

    private static void Sweep(Border row)
    {
        double k = Amount;
        if (k <= 0 || row.Child is not SheenLayer host) return;
        double w = Math.Max(row.Bounds.Width, 200);
        var band = new Rectangle
        {
            Width = 70, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, Opacity = Math.Clamp(0.9 * k, 0, 1),
            Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 0xB9, 0x9C, 0xFF), 0), new GradientStop(Color.FromArgb(0x30, 0xB9, 0x9C, 0xFF), 0.5),
                    new GradientStop(Color.FromArgb(0, 0xB9, 0x9C, 0xFF), 1),
                },
            },
            Tag = "friends-sheen",
        };
        var slide = new TranslateTransform(-90, 0);
        band.RenderTransform = slide;
        host.Children.Add(band);
        var t = TransformTween.Run(slide, TimeSpan.FromMilliseconds(520 / Math.Max(k, 0.5)),
            new (double, AvaloniaProperty, double)[] { (0, TranslateTransform.XProperty, -90), (1, TranslateTransform.XProperty, w + 20) }, new QuadraticEaseInOut());
        var done = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(560 / Math.Max(k, 0.5)) };
        done.Tick += (_, _) => { done.Stop(); t.Stop(); host.Children.Remove(band); };
        done.Start();
    }

    /// <summary>A friend came online: their avatar hops once.</summary>
    private void HopAvatar(string friendId)
    {
        double k = Amount;
        if (k <= 0 || !_avatars.TryGetValue(friendId, out var av)) return;
        if (av.RenderTransform is not TranslateTransform t) av.RenderTransform = t = new TranslateTransform();
        TransformTween.Run(t, TimeSpan.FromMilliseconds(560), new (double, AvaloniaProperty, double)[]
        {
            (0, TranslateTransform.YProperty, 0), (0.3, TranslateTransform.YProperty, -10 * k), (0.62, TranslateTransform.YProperty, 0),
            (0.78, TranslateTransform.YProperty, -3 * k), (0.9, TranslateTransform.YProperty, 0), (0.95, TranslateTransform.YProperty, -1 * k),
            (1, TranslateTransform.YProperty, 0),
        });
        Sparks(av, MintC, 8);
    }

    /// <summary>The chip pop: the pressed element swells and settles, and throws a few sparks.</summary>
    internal void Pop(Control el, Color color)
    {
        double k = Amount;
        if (k <= 0) return;
        el.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        if (el.RenderTransform is not ScaleTransform s) el.RenderTransform = s = new ScaleTransform(1, 1);
        double peak = 1 + 0.22 * k;
        TransformTween.Run(s, TimeSpan.FromMilliseconds(380), new (double, AvaloniaProperty, double)[]
        {
            (0, ScaleTransform.ScaleXProperty, 1), (0.29, ScaleTransform.ScaleXProperty, peak), (0.6, ScaleTransform.ScaleXProperty, 0.96), (1, ScaleTransform.ScaleXProperty, 1),
            (0, ScaleTransform.ScaleYProperty, 1), (0.29, ScaleTransform.ScaleYProperty, peak), (0.6, ScaleTransform.ScaleYProperty, 0.96), (1, ScaleTransform.ScaleYProperty, 1),
        });
        Sparks(el, color, (int)Math.Round(12 * k));
    }

    /// <summary>The drawer's own fx layer over every row (WPF _fx): never hit-testable.</summary>
    private readonly Canvas _fx = new() { IsHitTestVisible = false, Tag = "friends-fx" };
    private readonly Random _rng = new();

    /// <summary>The centre of <paramref name="from"/> on the fx layer, or null when it is not on screen.</summary>
    private Point? FxCentre(Control from)
    {
        try
        {
            if (from.TranslatePoint(new Point(from.Bounds.Width / 2, from.Bounds.Height / 2), _fx) is { } p) return p;
            // Not in a window yet (or a detached host): add the offsets up to the drawer, which _fx fills.
            double x = from.Bounds.Width / 2, y = from.Bounds.Height / 2;
            Visual? v = from;
            while (v != null && !ReferenceEquals(v, this)) { x += v.Bounds.X; y += v.Bounds.Y; v = v.GetVisualParent() ?? (v as Control)?.Parent as Visual; }
            return v == null ? null : new Point(x, y);
        }
        catch { return null; }
    }

    /// <summary>Removes <paramref name="c"/> from the fx layer after <paramref name="ms"/>.</summary>
    private void Retire(Control c, double ms)
    {
        var t = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms + 40) };
        t.Tick += (_, _) => { t.Stop(); _fx.Children.Remove(c); };
        t.Start();
    }

    /// <summary>Little glowing dots flung out of an element's centre. The glow is a BoxShadow on a
    /// round Border (never an Effect); a handful of dots, never a particle system.</summary>
    internal void Sparks(Control from, Color color, int count)
    {
        if (count <= 0 || Amount <= 0 || FxCentre(from) is not { } c) return;
        var brush = new SolidColorBrush(color);
        var glow = new BoxShadows(new BoxShadow { Blur = 6, Color = Color.FromArgb(0xE6, color.R, color.G, color.B) });
        for (int i = 0; i < count; i++)
        {
            double size = 3 + _rng.NextDouble() * 3;
            var dot = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = brush, BoxShadow = glow, Tag = "friends-spark" };
            Canvas.SetLeft(dot, c.X - size / 2);
            Canvas.SetTop(dot, c.Y - size / 2);
            var t = new TranslateTransform();
            dot.RenderTransform = t;
            _fx.Children.Add(dot);
            double ang = _rng.NextDouble() * Math.PI * 2, dist = (24 + _rng.NextDouble() * 30) * Amount;
            double ms = 420 + _rng.Next(260);
            var ease = new QuadraticEaseOut();
            TransformTween.Run(t, TimeSpan.FromMilliseconds(ms), new (double, AvaloniaProperty, double)[]
            {
                (0, TranslateTransform.XProperty, 0), (1, TranslateTransform.XProperty, Math.Cos(ang) * dist),
                (0, TranslateTransform.YProperty, 0), (1, TranslateTransform.YProperty, Math.Sin(ang) * dist + 6),
            }, ease);
            TransformTween.Run(dot, TimeSpan.FromMilliseconds(ms), new (double, AvaloniaProperty, double)[] { (0, OpacityProperty, 1), (1, OpacityProperty, 0) });
            Retire(dot, ms);
        }
    }

    /// <summary>The shockwave on Add: two rings that grow out of the button and fade, then sparks.</summary>
    internal void Shockwave(Control from, Color color)
    {
        double k = Amount;
        if (k <= 0 || FxCentre(from) is not { } c) return;
        for (int i = 0; i < 2; i++)
        {
            var ring = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(2.5),
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 12, Color = Color.FromArgb(0xE6, color.R, color.G, color.B) }),
                RenderTransformOrigin = RelativePoint.Center, Opacity = 0, Tag = "friends-shockwave",
            };
            Canvas.SetLeft(ring, c.X - 10);
            Canvas.SetTop(ring, c.Y - 10);
            var s = new ScaleTransform(0.4, 0.4);
            ring.RenderTransform = s;
            _fx.Children.Add(ring);
            double ms = 620 / Math.Max(k, 0.5), begin = i * 120;
            var ease = new CubicEaseOut();
            void Go()
            {
                TransformTween.Run(s, TimeSpan.FromMilliseconds(ms), new (double, AvaloniaProperty, double)[]
                {
                    (0, ScaleTransform.ScaleXProperty, 0.4), (1, ScaleTransform.ScaleXProperty, 1 + 9 * k),
                    (0, ScaleTransform.ScaleYProperty, 0.4), (1, ScaleTransform.ScaleYProperty, 1 + 9 * k),
                }, ease);
                TransformTween.Run(ring, TimeSpan.FromMilliseconds(ms), new (double, AvaloniaProperty, double)[] { (0, OpacityProperty, 0.95), (1, OpacityProperty, 0) });
            }
            if (begin <= 0) Go();
            else
            {
                var d = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(begin) };
                d.Tick += (_, _) => { d.Stop(); Go(); };
                d.Start();
            }
            Retire(ring, ms + begin);
        }
        Sparks(from, color, (int)Math.Round(14 * k));
    }

    /// <summary>A card that just opened grows in under its row (WPF CardIn -> MotionFx.StaggerIn).</summary>
    private static void CardIn(Control row) { if (Amount > 0) StaggerIn(row, TimeSpan.Zero); }

    /// <summary>WPF LandLine: a feed line that just landed slides in from the left (280 ms, a small
    /// back-ease on the slide; the opacity stays linear inside 0..1).</summary>
    private static void LandLine(Control row)
    {
        double k = Amount;
        if (k <= 0) return;
        var slide = new TranslateTransform(-12 * k, 0);
        row.RenderTransform = slide;
        TransformTween.Run(slide, TimeSpan.FromMilliseconds(280), new (double, AvaloniaProperty, double)[] { (0, TranslateTransform.XProperty, -12 * k), (1, TranslateTransform.XProperty, 0) }, new BackEaseOut());
        TransformTween.Run(row, TimeSpan.FromMilliseconds(280), new (double, AvaloniaProperty, double)[] { (0, OpacityProperty, 0), (1, OpacityProperty, 1) });
    }
}
