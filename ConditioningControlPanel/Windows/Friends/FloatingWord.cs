using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.FriendsWindows;

/// <summary>
/// A poke thrown across the app window: the word pops in tilted, rises and fades, and a small
/// burst of sparks leaves it. Mint for a friendly poke, pink for a tease. Owned by the window it
/// flies over, never takes focus, never takes the mouse, closes itself.
/// Motion Reduced = half the travel and no sparks; Off = the word shows still and fades.
/// </summary>
internal sealed class FloatingWord : Window
{
    private const double Box = 460;

    private FloatingWord(string word, bool pink, double fontSize)
    {
        LandingChrome.Dress(this, null);
        SizeToContent = SizeToContent.Manual;
        Width = Box;
        Height = Box * 0.6;
        IsHitTestVisible = false;
        Focusable = false;
        LandingChrome.ClickThrough(this);

        var colour = pink ? LandingChrome.Pink : LandingChrome.Mint;
        var canvas = new Canvas { Width = Width, Height = Height, IsHitTestVisible = false };
        Content = canvas;

        var text = new TextBlock
        {
            Text = word,
            FontFamily = LandingChrome.Display,
            FontWeight = FontWeights.Bold,
            FontSize = fontSize,
            Foreground = LandingChrome.Brush(colour),
            Effect = new DropShadowEffect { Color = colour, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.7 },
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tw = text.DesiredSize.Width;
        var th = text.DesiredSize.Height;
        var cx = Width / 2;
        var cy = Height * 0.62;
        Canvas.SetLeft(text, cx - tw / 2);
        Canvas.SetTop(text, cy - th / 2);
        canvas.Children.Add(text);

        var level = MotionFx.Level;
        if (level == MotionLevel.Off)
        {
            text.Opacity = 1;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400)) { BeginTime = TimeSpan.FromMilliseconds(1100) };
            fade.Completed += (_, _) => SafeClose();
            text.BeginAnimation(OpacityProperty, fade);
            return;
        }

        var full = level == MotionLevel.Full;
        var rise = full ? th * 1.6 : th * 0.8;
        var scale = new ScaleTransform(0.6, 0.6);
        var rotate = new RotateTransform(-6);
        var shift = new TranslateTransform(0, th * 0.25);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(rotate);
        group.Children.Add(shift);
        text.RenderTransform = group;

        var total = TimeSpan.FromMilliseconds(1600);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var sx = new DoubleAnimationUsingKeyFrames { Duration = total };
        sx.KeyFrames.Add(new EasingDoubleKeyFrame(0.6, KeyTime.FromPercent(0)));
        sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.15, KeyTime.FromPercent(0.15), new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut }));
        sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), ease));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, sx);

        var rot = new DoubleAnimationUsingKeyFrames { Duration = total };
        rot.KeyFrames.Add(new EasingDoubleKeyFrame(-6, KeyTime.FromPercent(0)));
        rot.KeyFrames.Add(new EasingDoubleKeyFrame(2, KeyTime.FromPercent(0.15)));
        rot.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), ease));
        rotate.BeginAnimation(RotateTransform.AngleProperty, rot);

        var up = new DoubleAnimationUsingKeyFrames { Duration = total };
        up.KeyFrames.Add(new EasingDoubleKeyFrame(th * 0.25, KeyTime.FromPercent(0)));
        up.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.15)));
        up.KeyFrames.Add(new EasingDoubleKeyFrame(-rise, KeyTime.FromPercent(1), ease));
        shift.BeginAnimation(TranslateTransform.YProperty, up);

        var op = new DoubleAnimationUsingKeyFrames { Duration = total };
        op.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        op.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.15)));
        op.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.6)));
        op.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        op.Completed += (_, _) => SafeClose();
        text.BeginAnimation(OpacityProperty, op);

        if (full && MotionFx.AllowParticles) Sparks(canvas, cx, cy, colour);
    }

    /// <summary>A dozen short streaks thrown out of the word at the pop, gold and the word's colour.</summary>
    private static void Sparks(Canvas canvas, double cx, double cy, Color colour)
    {
        var rng = new Random();
        const int n = 12;
        for (var i = 0; i < n; i++)
        {
            var a = (i + rng.NextDouble() * 0.6) / n * Math.PI * 2;
            var dist = 60 + rng.NextDouble() * 70;
            var size = 3 + rng.NextDouble() * 3;
            var dot = new Ellipse
            {
                Width = size, Height = size,
                Fill = LandingChrome.Brush(i % 3 == 0 ? LandingChrome.Gold : colour),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(dot, cx - size / 2);
            Canvas.SetTop(dot, cy - size / 2);
            var move = new TranslateTransform();
            dot.RenderTransform = move;
            canvas.Children.Add(dot);

            var dur = TimeSpan.FromMilliseconds(520 + rng.Next(260));
            var delay = TimeSpan.FromMilliseconds(180);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            move.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, Math.Cos(a) * dist, dur) { BeginTime = delay, EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, Math.Sin(a) * dist + 18, dur) { BeginTime = delay, EasingFunction = ease });
            var fade = new DoubleAnimationUsingKeyFrames { BeginTime = delay, Duration = dur };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.1)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            dot.Opacity = 0;
            dot.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void SafeClose()
    {
        try { Close(); } catch (Exception ex) { App.Logger?.Debug("[Friends] floating word close: {E}", ex.Message); }
    }

    /// <summary>Throws <paramref name="word"/> over the middle of <paramref name="owner"/>.
    /// <paramref name="small"/> is the sender's own beat: smaller, lower.</summary>
    public static void Throw(Window owner, string word, bool pink, bool small = false)
    {
        if (owner == null || string.IsNullOrWhiteSpace(word)) return;
        try
        {
            var w = new FloatingWord(word, pink, small ? 22 : 34);
            if (owner.IsLoaded) w.Owner = owner;
            var r = LandingChrome.OwnerRect(owner);
            w.Left = r.Left + (r.Width - w.Width) / 2;
            w.Top = r.Top + r.Height * (small ? 0.62 : 0.48) - w.Height / 2;
            w.Show();
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] floating word: {E}", ex.Message); }
    }
}
