using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// The house book's THUD: cubic-bezier(.2, 1.5, .4, 1). WPF has no bezier ease, so this solves
/// the curve for t on each call (a few Newton steps, it is a 4-point curve).
/// </summary>
public sealed class BezierEase : EasingFunctionBase
{
    public double X1 { get; init; } = 0.2;
    public double Y1 { get; init; } = 1.5;
    public double X2 { get; init; } = 0.4;
    public double Y2 { get; init; } = 1.0;

    public BezierEase() { EasingMode = EasingMode.EaseIn; }

    protected override double EaseInCore(double normalizedTime)
    {
        double x = Math.Clamp(normalizedTime, 0, 1);
        double t = x;
        for (int i = 0; i < 8; i++)
        {
            double cx = Bez(t, X1, X2) - x;
            double d = Deriv(t, X1, X2);
            if (Math.Abs(cx) < 1e-5 || Math.Abs(d) < 1e-6) break;
            t = Math.Clamp(t - cx / d, 0, 1);
        }
        return Bez(t, Y1, Y2);
    }

    private static double Bez(double t, double a, double b)
        => 3 * a * t * (1 - t) * (1 - t) + 3 * b * t * t * (1 - t) + t * t * t;

    private static double Deriv(double t, double a, double b)
        => 3 * a * (1 - t) * (1 - t) + 6 * (b - a) * t * (1 - t) + 3 * (1 - b) * t * t;

    protected override Freezable CreateInstanceCore() => new BezierEase();

    /// <summary>The house THUD duration.</summary>
    public static readonly TimeSpan Thud = TimeSpan.FromMilliseconds(340);
}

/// <summary>
/// The leash's juice. Every move reads <see cref="MotionFx.Level"/>: Full plays it, Reduced at
/// half size, Off leaves everything at rest (and still readable: nothing here carries meaning
/// that the static picture does not). Sounds go through the app's one-shot player and stay
/// silent when output is off.
/// </summary>
internal static class LeashFx
{
    private static readonly Random Rng = new();

    /// <summary>Holds every leash surface at rest (the suite's offscreen renders, which have no
    /// clock to advance an animation past its first frame). Same picture as Motion Off.</summary>
    internal static bool ForceStill { get; set; }

    public static double Amount => ForceStill ? 0 : MotionFx.Level switch
    {
        MotionLevel.Off => 0,
        MotionLevel.Reduced => 0.5,
        _ => 1,
    };

    /// <summary>The THUD: the element lands from a little larger with the house overshoot.</summary>
    public static void Thud(FrameworkElement el, double from = 1.35)
    {
        double k = Amount;
        if (k <= 0) return;
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        var s = new ScaleTransform(1, 1);
        el.RenderTransform = s;
        var a = new DoubleAnimation(1 + (from - 1) * k, 1, BezierEase.Thud) { EasingFunction = new BezierEase() };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>The tug: a yank to the left, a small swing back, rest. Also the leashed window's wobble.</summary>
    public static void Tug(FrameworkElement el, double strength = 1)
    {
        double k = Amount * strength;
        if (k <= 0) return;
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        var move = new TranslateTransform();
        var turn = new RotateTransform();
        var g = new TransformGroup();
        // Keep whatever transform the element already wears (a window root may carry its own).
        var prior = el.RenderTransform;
        if (prior is TransformGroup pg && pg.Children.Count == 3 && pg.Children[1] is RotateTransform && pg.Children[2] is TranslateTransform)
            prior = pg.Children[0];
        g.Children.Add(prior is { } pr && !pr.Value.IsIdentity ? pr : Transform.Identity);
        g.Children.Add(turn);
        g.Children.Add(move);
        el.RenderTransform = g;
        var x = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(520) };
        x.KeyFrames.Add(new EasingDoubleKeyFrame(-12 * k, KeyTime.FromPercent(0.3), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        x.KeyFrames.Add(new EasingDoubleKeyFrame(5 * k, KeyTime.FromPercent(0.6), new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
        x.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new BezierEase()));
        var r = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(520) };
        r.KeyFrames.Add(new EasingDoubleKeyFrame(-2.2 * k, KeyTime.FromPercent(0.3)));
        r.KeyFrames.Add(new EasingDoubleKeyFrame(1.1 * k, KeyTime.FromPercent(0.6)));
        r.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        move.BeginAnimation(TranslateTransform.XProperty, x);
        turn.BeginAnimation(RotateTransform.AngleProperty, r);
    }

    /// <summary>A heart tag that swings on its ring: an ambient loop, Full motion only.</summary>
    public static void Swing(FrameworkElement tag)
    {
        if (ForceStill || !MotionFx.AllowAmbientLoops) return;
        tag.RenderTransformOrigin = new Point(0.5, 0);
        var rot = new RotateTransform();
        tag.RenderTransform = rot;
        var a = new DoubleAnimation(-8, 8, TimeSpan.FromSeconds(1.5))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(a, 24);
        rot.BeginAnimation(RotateTransform.AngleProperty, a);
    }

    /// <summary>A soft pop on a pressed chip.</summary>
    public static void Pop(FrameworkElement el)
    {
        double k = Amount;
        if (k <= 0) return;
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        var s = new ScaleTransform(1, 1);
        el.RenderTransform = s;
        var pop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(360) };
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1 + 0.18 * k, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360)), new ElasticEase { Oscillations = 1, Springiness = 4 }));
        s.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    /// <summary>Glowing sparks flung from <paramref name="from"/>'s centre onto <paramref name="layer"/>.
    /// Gold and pink by default, the snap's colours.</summary>
    public static void Sparks(Canvas layer, FrameworkElement from, int count, params Color[] colors)
    {
        if (count <= 0 || Amount <= 0) return;
        if (colors.Length == 0) colors = new[] { FriendsLook.Gold, FriendsLook.Gold, FriendsLook.Pink };
        Point c;
        try { c = from.TranslatePoint(new Point(from.ActualWidth / 2, from.ActualHeight / 2), layer); }
        catch { return; }
        SparksAt(layer, c, count, 60, colors);
    }

    public static void SparksAt(Canvas layer, Point c, int count, double reach, params Color[] colors)
    {
        if (count <= 0 || Amount <= 0) return;
        double k = Amount;
        for (int i = 0; i < count; i++)
        {
            var color = colors[i % colors.Length];
            double size = 3 + Rng.NextDouble() * 4;
            var dot = new Ellipse { Width = size, Height = size, Fill = FriendsLook.Frozen(color), Effect = FriendsLook.Glow(color, 6, 0.9), IsHitTestVisible = false };
            Canvas.SetLeft(dot, c.X - size / 2);
            Canvas.SetTop(dot, c.Y - size / 2);
            var t = new TranslateTransform();
            dot.RenderTransform = t;
            layer.Children.Add(dot);
            double ang = Rng.NextDouble() * Math.PI * 2;
            double dist = (reach * 0.4 + Rng.NextDouble() * reach) * k;
            var dur = TimeSpan.FromMilliseconds(520 + Rng.Next(320));
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Math.Cos(ang) * dist, dur) { EasingFunction = ease });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Math.Sin(ang) * dist + 10, dur) { EasingFunction = ease });
            var fade = new DoubleAnimation(1, 0, dur);
            fade.Completed += (_, _) => layer.Children.Remove(dot);
            dot.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }

    // ---- sound ------------------------------------------------------------------------

    /// <summary>The chain jingle under a tug.</summary>
    public static void Jingle() => Play("chaos/chain_pop.mp3", 0.22f, "leash-tug");

    /// <summary>The snap: the collar clicks shut.</summary>
    public static void Snap() => Play("chaos/collar_save.mp3", 0.28f, "leash-snap");

    /// <summary>A soft click for a sent preset.</summary>
    public static void Sent() => Play("chaos/chip_pop.mp3", 0.16f, "leash-sent");

    /// <summary>The gate's stamp landing.</summary>
    public static void Stamp() => Play("chaos/shield_thunk.mp3", 0.2f, "leash-stamp");

    private static void Play(string rel, float scale, string tag)
    {
        try
        {
            var audio = App.Audio;
            if (audio == null || audio.IsOutputSuppressed) return;
            int level = App.Settings?.Current?.MasterVolume ?? 0;
            if (level <= 0) return;
            var path = ModResourceResolver.ResolveAudioPath(rel);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            audio.PlayOneShot(path, Math.Clamp(level / 100f * scale, 0f, 1f), tag);
        }
        catch (Exception ex) { App.Logger?.Debug("[Leash] sfx {Tag} failed: {E}", tag, ex.Message); }
    }
}
