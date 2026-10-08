using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Serilog;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>
/// PORTED from ConditioningControlPanel/Windows/Friends/FloatingWord.cs: a word thrown over the
/// window, popping in tilted, rising and fading, with a dozen sparks leaving it. Mint for a friendly
/// word, pink for a tease. Owned by the window it flies over, topmost, never takes focus or the
/// mouse, closes itself. Motion Reduced = half the travel and no sparks; Off = still, then fades.
/// WPF's keyframes are evaluated by <see cref="Pose"/>/<see cref="Spark"/> once per rendered frame
/// (<c>RequestAnimationFrame</c>), so the loop lives exactly as long as the window.
/// </summary>
internal sealed class FloatingWord : Window
{
    private const double Box = 460;
    internal const double TotalMs = 1600, OffHoldMs = 1100, OffFadeMs = 400, SparkDelayMs = 180;

    private readonly TextBlock _text;
    private readonly ScaleTransform _scale = new(0.6, 0.6);
    private readonly RotateTransform _rotate = new(-6);
    private readonly TranslateTransform _shift = new();
    private readonly MotionLevel _level;
    private readonly double _th;
    private readonly List<(Ellipse Dot, TranslateTransform Move, double A, double Dist, double Dur)> _sparks = new();
    private TimeSpan? _start;
    private bool _closed;

    /// <summary>--render-all only: a sample word mid-flight.</summary>
    internal FloatingWord() : this("sent", false, 34, MotionLevel.Full, true) => Step(400);

    private FloatingWord(string word, bool pink, double fontSize, MotionLevel level, bool particles)
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        IsHitTestVisible = false;
        Width = Box;
        Height = Box * 0.6;
        _level = level;

        var colour = pink ? Color.FromRgb(0xFF, 0x5F, 0xB4) : Color.FromRgb(0x5F, 0xFF, 0xD0);
        var canvas = new Canvas { Width = Width, Height = Height, IsHitTestVisible = false };
        Content = canvas;
        _text = new TextBlock
        {
            Text = word,
            FontFamily = FriendsDrawer.Display,
            FontWeight = FontWeight.Bold,
            FontSize = fontSize,
            Foreground = new SolidColorBrush(colour),
            Effect = new DropShadowEffect { Color = colour, BlurRadius = 18, OffsetX = 0, OffsetY = 0, Opacity = 0.7 },
            RenderTransformOrigin = RelativePoint.Center,
            Tag = "floating-word",
        };
        _text.Measure(Size.Infinity);
        var tw = _text.DesiredSize.Width;
        _th = _text.DesiredSize.Height;
        var cx = Width / 2;
        var cy = Height * 0.62;
        Canvas.SetLeft(_text, cx - tw / 2);
        Canvas.SetTop(_text, cy - _th / 2);
        canvas.Children.Add(_text);
        if (level != MotionLevel.Off)
            _text.RenderTransform = new TransformGroup { Children = { _scale, _rotate, _shift } };

        if (level == MotionLevel.Full && particles)
        {
            var rng = new Random();
            const int n = 12;
            for (var i = 0; i < n; i++)
            {
                var a = (i + rng.NextDouble() * 0.6) / n * Math.PI * 2;
                var dist = 60 + rng.NextDouble() * 70;
                var size = 3 + rng.NextDouble() * 3;
                var move = new TranslateTransform();
                var dot = new Ellipse
                {
                    Width = size, Height = size, Opacity = 0, IsHitTestVisible = false, RenderTransform = move,
                    Fill = new SolidColorBrush(i % 3 == 0 ? Color.FromRgb(0xFF, 0xCF, 0x6B) : colour),
                };
                Canvas.SetLeft(dot, cx - size / 2);
                Canvas.SetTop(dot, cy - size / 2);
                canvas.Children.Add(dot);
                _sparks.Add((dot, move, a, dist, 520 + rng.Next(260)));
            }
        }
        Step(0);
        Closed += (_, _) => _closed = true;
    }

    internal bool IsDone { get; private set; }

    /// <summary>The word's pose <paramref name="ms"/> after the throw: WPF's keyframes, verbatim.
    /// Full/Reduced: pop 0.6->1.15 (BackEase 0.6) and -6->2 deg and up from th/4 in the first 15%,
    /// then settle to 1.0/0 deg and rise (th*1.6 Full, th*0.8 Reduced, cubic out); opacity in by
    /// 15%, held to 60%, out by 100%. Off: shown still for 1.1 s, faded over 0.4 s.</summary>
    internal static (double Scale, double Angle, double Y, double Opacity) Pose(double ms, MotionLevel level, double th)
    {
        if (level == MotionLevel.Off)
            return (1, 0, 0, Math.Clamp(1 - (ms - OffHoldMs) / OffFadeMs, 0, 1));
        var p = Math.Clamp(ms / TotalMs, 0, 1);
        var rise = level == MotionLevel.Full ? th * 1.6 : th * 0.8;
        var op = p < 0.15 ? p / 0.15 : p < 0.6 ? 1 : 1 - (p - 0.6) / 0.4;
        if (p < 0.15)
        {
            var t = p / 0.15;
            return (0.6 + 0.55 * BackOut(t, 0.6), -6 + 8 * t, th * 0.25 * (1 - t), op);
        }
        var c = CubicOut((p - 0.15) / 0.85);
        return (1.15 - 0.15 * c, 2 - 2 * c, -rise * c, op);
    }

    /// <summary>One spark's (dx, dy, opacity) <paramref name="ms"/> after the throw (WPF Sparks:
    /// 180 ms delay, quadratic-out flight, in over 10%, out by the end).</summary>
    internal static (double X, double Y, double Opacity) Spark(double ms, double a, double dist, double dur)
    {
        var t = (ms - SparkDelayMs) / dur;
        if (t <= 0 || t >= 1) return (0, 0, 0);
        var e = 1 - (1 - t) * (1 - t);
        return (Math.Cos(a) * dist * e, (Math.Sin(a) * dist + 18) * e, t < 0.1 ? t / 0.1 : 1 - (t - 0.1) / 0.9);
    }

    private static double CubicOut(double t) => 1 - Math.Pow(1 - t, 3);

    private static double BackOut(double t, double amp)
    {
        var u = 1 - t;
        return 1 - (u * u * u - amp * u * Math.Sin(Math.PI * u));
    }

    /// <summary>Applies the pose <paramref name="ms"/> after the throw; closes the window at the end.</summary>
    internal void Step(double ms)
    {
        var (s, angle, y, op) = Pose(ms, _level, _th);
        _scale.ScaleX = _scale.ScaleY = s;
        _rotate.Angle = angle;
        _shift.Y = y;
        _text.Opacity = op;
        foreach (var (dot, move, a, dist, dur) in _sparks)
        {
            var (dx, dy, o) = Spark(ms, a, dist, dur);
            (move.X, move.Y, dot.Opacity) = (dx, dy, o);
        }
        var end = _level == MotionLevel.Off ? OffHoldMs + OffFadeMs : TotalMs;
        if (ms < end || IsDone) return;
        IsDone = true;
        try { Close(); } catch (Exception ex) { Log.Debug("[Friends] floating word close: {E}", ex.Message); }
    }

    private void OnFrame(TimeSpan now)
    {
        if (_closed) return;
        _start ??= now;
        Step((now - _start.Value).TotalMilliseconds);
        if (!IsDone) RequestAnimationFrame(OnFrame);
    }

    /// <summary>Throws <paramref name="word"/> over the middle of <paramref name="owner"/>.
    /// <paramref name="small"/> is the sender's own beat: smaller, lower.</summary>
    public static FloatingWord? Throw(Window? owner, string word, bool pink, bool small = false)
    {
        if (owner == null || !owner.IsVisible || string.IsNullOrWhiteSpace(word)) return null;
        try
        {
            var w = new FloatingWord(word, pink, small ? 22 : 34, AmbientFxCanvas.Env.Level, AmbientFxCanvas.Env.AllowParticles);
            var k = owner.DesktopScaling > 0 ? owner.DesktopScaling : 1;
            var size = owner.ClientSize;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Position = owner.Position + new PixelPoint(
                (int)Math.Round((size.Width - w.Width) / 2 * k),
                (int)Math.Round((size.Height * (small ? 0.62 : 0.48) - w.Height / 2) * k));
            X11Overlay.SetClickThrough(w, true);
            w.Show(owner);
            w.RequestAnimationFrame(w.OnFrame);
            return w;
        }
        catch (Exception ex) { Log.Debug("[Friends] floating word: {E}", ex.Message); return null; }
    }
}
