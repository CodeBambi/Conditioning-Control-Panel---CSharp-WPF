using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>
/// The drawer's juice, after the launcher's recipe: the card springs up from the chip, rows
/// stagger in, a sheen crosses a row on hover, online dots breathe, a poke chip pops and throws
/// sparks, Add sends a shockwave, a friend who comes online hops. Every move reads
/// <see cref="MotionFx.Level"/>: Full plays it, Reduced plays it at half the size and speed,
/// Off leaves everything still and in its resting place.
/// </summary>
public sealed partial class FriendsDrawer
{
    /// <summary>1 at Full, 0.5 at Reduced, 0 at Off: every amplitude is multiplied by it.</summary>
    internal static double Amount => MotionFx.Level switch
    {
        MotionLevel.Off => 0,
        MotionLevel.Reduced => 0.5,
        _ => 1,
    };

    private readonly List<UIElement> _breathing = new();
    private readonly Random _rng = new();

    /// <summary>The spring the card opens with: up from the chip, a little overshoot.</summary>
    private void PlayEntrance()
    {
        double k = Amount;
        if (k <= 0) { Opacity = 1; RenderTransform = null; return; }

        RenderTransformOrigin = new Point(0, 1);
        var scale = new ScaleTransform(1, 1);
        var slide = new TranslateTransform();
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(slide);
        RenderTransform = group;

        var ease = new BackEase { Amplitude = 0.45 * k, EasingMode = EasingMode.EaseOut };
        var ms = TimeSpan.FromMilliseconds(220 / Math.Max(k, 0.5) * (k < 1 ? 0.8 : 1));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1 - 0.06 * k, 1, ms) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1 - 0.06 * k, 1, ms) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14 * k, 0, ms) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));

        var rows = new List<FrameworkElement>();
        foreach (var id in _rowOrder) if (_rows.TryGetValue(id, out var r)) rows.Add(r);
        MotionFx.StaggerIn(rows);
    }

    /// <summary>A card that just opened grows in under its row.</summary>
    private static void CardIn(FrameworkElement row)
    {
        if (Amount <= 0) return;
        MotionFx.StaggerIn(new[] { row });
    }

    /// <summary>Breathes every online dot in the list. Ambient: only at Full, like the rest of
    /// the app's loops, and stopped whenever the list is rebuilt or the drawer folds.</summary>
    private void StartAmbient()
    {
        foreach (var id in _rowOrder)
        {
            if (!_avatars.TryGetValue(id, out var av) || av is not Grid g) continue;
            foreach (UIElement c in g.Children)
            {
                if (c is Ellipse e && e.Tag is string s && s == "friends-dot-on")
                {
                    MotionFx.GlowBreath(e, 0.45, 1.0, 1.8);
                    _breathing.Add(e);
                }
            }
        }
    }

    private void StopAmbient()
    {
        foreach (var e in _breathing) MotionFx.Stop(e);
        _breathing.Clear();
    }

    /// <summary>The hover sheen: a soft lilac band that crosses the row once on the way in,
    /// and the row's own hover wash. The band is a child of the row's own content grid, laid
    /// over the rest and never hit-testable.</summary>
    private void AttachSheen(Border row, bool open)
    {
        var rest = row.Background;
        row.MouseEnter += (_, _) =>
        {
            if (!open) row.Background = FriendsLook.HoverBrush;
            Sweep(row);
        };
        row.MouseLeave += (_, _) => { if (!open) row.Background = rest; };
    }

    private void Sweep(Border row)
    {
        double k = Amount;
        if (k <= 0 || row.Child is not Grid host) return;
        double w = Math.Max(row.ActualWidth, 200);
        var band = new Rectangle
        {
            Width = 70,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            Opacity = 0.9 * k,
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0, 0xB9, 0x9C, 0xFF), 0),
                    new(Color.FromArgb(0x30, 0xB9, 0x9C, 0xFF), 0.5),
                    new(Color.FromArgb(0, 0xB9, 0x9C, 0xFF), 1),
                }, 0),
        };
        Grid.SetRowSpan(band, Math.Max(1, host.RowDefinitions.Count));
        if (host.ColumnDefinitions.Count > 0) Grid.SetColumnSpan(band, host.ColumnDefinitions.Count);
        var slide = new TranslateTransform(-90, 0);
        band.RenderTransform = slide;
        host.Children.Add(band);
        var a = new DoubleAnimation(-90, w + 20, TimeSpan.FromMilliseconds(520 / Math.Max(k, 0.5)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        };
        a.Completed += (_, _) => host.Children.Remove(band);
        slide.BeginAnimation(TranslateTransform.XProperty, a);
    }

    /// <summary>A friend came online: their avatar hops once.</summary>
    private void HopAvatar(string friendId)
    {
        double k = Amount;
        if (k <= 0 || !_avatars.TryGetValue(friendId, out var av)) return;
        if (av.RenderTransform is not TranslateTransform t)
        {
            t = new TranslateTransform();
            av.RenderTransform = t;
        }
        var hop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(560) };
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(-10 * k, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170)),
            new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560)),
            new BounceEase { Bounces = 2, Bounciness = 3, EasingMode = EasingMode.EaseOut }));
        t.BeginAnimation(TranslateTransform.YProperty, hop);
        Sparks(av, FriendsLook.Mint, 8);
    }

    /// <summary>The chip pop: the pressed element swells and settles, and throws a few sparks.</summary>
    private void Pop(FrameworkElement el, Color color)
    {
        double k = Amount;
        if (k <= 0) return;
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        var s = el.RenderTransform as ScaleTransform;
        if (s == null || s.IsFrozen)
        {
            s = new ScaleTransform(1, 1);
            el.RenderTransform = s;
        }
        var pop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(380) };
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1 + 0.22 * k, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110)),
            new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380)),
            new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut }));
        s.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        Sparks(el, color, (int)Math.Round(12 * k));
    }

    /// <summary>Little glowing dots flung out of an element's centre, drawn on the drawer's own
    /// fx layer. A handful of ellipses, never a particle system.</summary>
    private void Sparks(FrameworkElement from, Color color, int count)
    {
        if (count <= 0 || !MotionFx.AllowTransitions) return;
        Point c;
        try { c = from.TranslatePoint(new Point(from.ActualWidth / 2, from.ActualHeight / 2), _fx); }
        catch { return; }
        var brush = FriendsLook.Frozen(color);
        for (int i = 0; i < count; i++)
        {
            double size = 3 + _rng.NextDouble() * 3;
            var dot = new Ellipse { Width = size, Height = size, Fill = brush, Effect = FriendsLook.Glow(color, 6, 0.9) };
            Canvas.SetLeft(dot, c.X - size / 2);
            Canvas.SetTop(dot, c.Y - size / 2);
            var t = new TranslateTransform();
            dot.RenderTransform = t;
            _fx.Children.Add(dot);

            double ang = _rng.NextDouble() * Math.PI * 2;
            double dist = (24 + _rng.NextDouble() * 30) * Amount;
            var dur = TimeSpan.FromMilliseconds(420 + _rng.Next(260));
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Math.Cos(ang) * dist, dur) { EasingFunction = ease });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Math.Sin(ang) * dist + 6, dur) { EasingFunction = ease });
            var fade = new DoubleAnimation(1, 0, dur);
            fade.Completed += (_, _) => _fx.Children.Remove(dot);
            dot.BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>The shockwave on Add: two rings that grow out of the button and fade.</summary>
    private void Shockwave(FrameworkElement from, Color color)
    {
        double k = Amount;
        if (k <= 0) return;
        Point c;
        try { c = from.TranslatePoint(new Point(from.ActualWidth / 2, from.ActualHeight / 2), _fx); }
        catch { return; }
        for (int i = 0; i < 2; i++)
        {
            var ring = new Ellipse
            {
                Width = 20,
                Height = 20,
                Stroke = FriendsLook.Frozen(color),
                StrokeThickness = 2.5,
                Effect = FriendsLook.Glow(color, 12, 0.9),
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            Canvas.SetLeft(ring, c.X - 10);
            Canvas.SetTop(ring, c.Y - 10);
            var s = new ScaleTransform(1, 1);
            ring.RenderTransform = s;
            _fx.Children.Add(ring);
            var dur = TimeSpan.FromMilliseconds(620 / Math.Max(k, 0.5));
            var begin = TimeSpan.FromMilliseconds(i * 120);
            var grow = new DoubleAnimation(0.4, 1 + 9 * k, dur)
            {
                BeginTime = begin,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            s.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            var fade = new DoubleAnimation(0.95, 0, dur) { BeginTime = begin };
            fade.Completed += (_, _) => _fx.Children.Remove(ring);
            ring.Opacity = 0;
            ring.BeginAnimation(OpacityProperty, fade);
        }
        Sparks(from, color, (int)Math.Round(14 * k));
    }
}
