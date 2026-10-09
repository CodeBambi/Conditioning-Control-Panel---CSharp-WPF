// PORTED from WPF 7.1.5 Controls/Leash/LeashFx.cs: the leash's juice. Every move reads the motion
// level: Full plays it, Reduced at half size, Off leaves everything at rest (nothing here carries
// meaning the still picture lacks). Sounds are the same files at the same scales through the
// one-shot player, gated by Core LeashSfxRules, silent at master volume 0.
// Avalonia traps: transforms move through Helpers/TransformTween (never Animation.RunAsync on a
// Transform); sparks are plain ellipses (no Effect); the Swing loop rides a 30 fps DispatcherTimer
// and only at Full motion, stopped when the tag leaves the tree.
using System;
using System.IO;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal static class LeashFx
{
    private static readonly Random Rng = new();

    /// <summary>Holds every leash surface at rest (headless tests). Same picture as Motion Off.</summary>
    internal static bool ForceStill { get; set; }

    /// <summary>Test seam: the motion level (defaults to the app's resolved level).</summary>
    internal static Func<MotionLevel> Level = () =>
    {
        try { return global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level; }
        catch { return MotionLevel.Full; }
    };

    public static double Amount => ForceStill ? 0 : Level() switch
    {
        MotionLevel.Off => 0,
        MotionLevel.Reduced => 0.5,
        _ => 1,
    };

    /// <summary>The house THUD duration.</summary>
    public static readonly TimeSpan ThudTime = TimeSpan.FromMilliseconds(340);

    private static ScaleTransform Scale(Control el)
    {
        el.RenderTransformOrigin = RelativePoint.Center;
        if (el.RenderTransform is ScaleTransform s) return s;
        s = new ScaleTransform(1, 1);
        el.RenderTransform = s;
        return s;
    }

    /// <summary>The THUD: the element lands from a little larger with the house overshoot.</summary>
    public static void Thud(Control el, double from = 1.35)
    {
        double k = Amount;
        if (k <= 0) return;
        var s = Scale(el);
        double f = 1 + (from - 1) * k;
        TransformTween.Run(s, ThudTime, new (double, AvaloniaProperty, double)[]
        {
            (0.0, ScaleTransform.ScaleXProperty, f), (0.0, ScaleTransform.ScaleYProperty, f),
            (1.0, ScaleTransform.ScaleXProperty, 1.0), (1.0, ScaleTransform.ScaleYProperty, 1.0),
        }, new BackEaseOut());
    }

    /// <summary>The tug: a yank to the left, a small swing back, rest.</summary>
    public static void Tug(Control el, double strength = 1)
    {
        double k = Amount * strength;
        if (k <= 0) return;
        el.RenderTransformOrigin = RelativePoint.Center;
        var move = new TranslateTransform();
        var turn = new RotateTransform();
        el.RenderTransform = new TransformGroup { Children = { turn, move } };
        var dur = TimeSpan.FromMilliseconds(520);
        TransformTween.Run(move, dur, new (double, AvaloniaProperty, double)[]
        {
            (0.0, TranslateTransform.XProperty, 0.0), (0.3, TranslateTransform.XProperty, -12 * k),
            (0.6, TranslateTransform.XProperty, 5 * k), (1.0, TranslateTransform.XProperty, 0.0),
        });
        TransformTween.Run(turn, dur, new (double, AvaloniaProperty, double)[]
        {
            (0.0, RotateTransform.AngleProperty, 0.0), (0.3, RotateTransform.AngleProperty, -2.2 * k),
            (0.6, RotateTransform.AngleProperty, 1.1 * k), (1.0, RotateTransform.AngleProperty, 0.0),
        });
    }

    /// <summary>A heart tag that swings on its ring: an ambient loop, Full motion only (WPF 24 fps;
    /// here the 30 fps house beat).</summary>
    public static void Swing(Control tag)
    {
        if (ForceStill || Level() != MotionLevel.Full) return;
        tag.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
        var rot = new RotateTransform();
        tag.RenderTransform = rot;
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) =>
        {
            double t = (DateTime.UtcNow - started).TotalSeconds;
            rot.Angle = -8 * Math.Cos(t * Math.PI / 1.5);   // -8 .. 8 and back over 3 s, sine in/out
        });
        tag.DetachedFromVisualTree += (_, _) => timer.Stop();
        tag.AttachedToVisualTree += (_, _) => timer.Start();
        if (tag.IsAttachedToVisualTree()) timer.Start();
    }

    /// <summary>A soft pop on a pressed chip.</summary>
    public static void Pop(Control el)
    {
        double k = Amount;
        if (k <= 0) return;
        var s = Scale(el);
        double up = 1 + 0.18 * k;
        TransformTween.Run(s, TimeSpan.FromMilliseconds(360), new (double, AvaloniaProperty, double)[]
        {
            (0.0, ScaleTransform.ScaleXProperty, 1.0), (0.0, ScaleTransform.ScaleYProperty, 1.0),
            (0.28, ScaleTransform.ScaleXProperty, up), (0.28, ScaleTransform.ScaleYProperty, up),
            (0.64, ScaleTransform.ScaleXProperty, 1 - 0.04 * k), (0.64, ScaleTransform.ScaleYProperty, 1 - 0.04 * k),
            (1.0, ScaleTransform.ScaleXProperty, 1.0), (1.0, ScaleTransform.ScaleYProperty, 1.0),
        });
    }

    /// <summary>Sparks flung from <paramref name="from"/>'s centre onto <paramref name="layer"/>.
    /// Gold and pink by default, the snap's colours.</summary>
    public static void Sparks(Canvas layer, Control from, int count, params Color[] colors)
    {
        if (count <= 0 || Amount <= 0) return;
        if (colors.Length == 0) colors = new[] { GoldC, GoldC, PinkC };
        var p = from.TranslatePoint(new Point(from.Bounds.Width / 2, from.Bounds.Height / 2), layer);
        if (p is not { } c) return;
        SparksAt(layer, c, count, 60, colors);
    }

    internal static readonly Color GoldC = Color.FromRgb(0xFF, 0xCF, 0x6B), PinkC = Color.FromRgb(0xFF, 0x5F, 0xB4),
        MintC = Color.FromRgb(0x5F, 0xFF, 0xD0), LilacC = Color.FromRgb(0xB9, 0x9C, 0xFF);

    public static void SparksAt(Canvas layer, Point c, int count, double reach, params Color[] colors)
    {
        if (count <= 0 || Amount <= 0) return;
        if (colors.Length == 0) colors = new[] { GoldC, GoldC, PinkC };
        double k = Amount;
        for (int i = 0; i < count; i++)
        {
            var color = colors[i % colors.Length];
            double size = 3 + Rng.NextDouble() * 4;
            var dot = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(color), IsHitTestVisible = false, Tag = "leash-spark" };
            Canvas.SetLeft(dot, c.X - size / 2);
            Canvas.SetTop(dot, c.Y - size / 2);
            var t = new TranslateTransform();
            dot.RenderTransform = t;
            layer.Children.Add(dot);
            double ang = Rng.NextDouble() * Math.PI * 2;
            double dist = (reach * 0.4 + Rng.NextDouble() * reach) * k;
            var dur = TimeSpan.FromMilliseconds(520 + Rng.Next(320));
            var ease = new QuadraticEaseOut();
            TransformTween.Run(t, dur, new (double, AvaloniaProperty, double)[]
            {
                (0.0, TranslateTransform.XProperty, 0.0), (1.0, TranslateTransform.XProperty, Math.Cos(ang) * dist),
                (0.0, TranslateTransform.YProperty, 0.0), (1.0, TranslateTransform.YProperty, Math.Sin(ang) * dist + 10),
            }, ease);
            // Opacity stays within 0..1 (Avalonia opacity > 1 trap): a linear fade, then the dot goes.
            TransformTween.Run(dot, dur, new (double, AvaloniaProperty, double)[] { (0.0, Visual.OpacityProperty, 1.0), (1.0, Visual.OpacityProperty, 0.0) });
            DispatcherTimer.RunOnce(() => layer.Children.Remove(dot), dur + TimeSpan.FromMilliseconds(40));
        }
    }

    // ---- sound (same files and scales as WPF) ------------------------------------------

    private static readonly LeashSfxRules Rules = new();

    /// <summary>Seam: true while the punishment window is up (r10's LeashPunishWindow fills it).</summary>
    internal static Func<bool> PunishWindowOpen = () => false;

    /// <summary>Test seam: the last cue that reached the player (tag), or null.</summary>
    internal static string? LastPlayed { get; private set; }

    public static void Jingle() => Play(LeashCue.Jingle, "chaos/chain_pop.mp3", 0.22f);
    public static void Snap() => Play(LeashCue.Snap, "chaos/collar_save.mp3", 0.28f);
    public static void Sent() => Play(LeashCue.Sent, "chaos/chip_pop.mp3", 0.16f);
    public static void Stamp() => Play(LeashCue.Stamp, "chaos/shield_thunk.mp3", 0.2f);
    public static void Ask() => Play(LeashCue.Ask, "chaos/reveal_chime.mp3", 0.18f);
    public static void Refused() => Play(LeashCue.Refused, "chaos/ui_unequip.mp3", 0.14f);
    public static void Gift() => Play(LeashCue.Gift, "chaos/ui_unlock.mp3", 0.18f);
    public static void Scold() => Play(LeashCue.Scold, "chaos/thud.mp3", 0.2f);
    public static void Assigned() => Play(LeashCue.Assigned, "chaos/cards_in.mp3", 0.16f);
    public static void Done() => Play(LeashCue.Done, "chaos/sin_accept.mp3", 0.18f);
    public static void Denied() => Play(LeashCue.Denied, "chaos/ui_denied.mp3", 0.16f);
    public static void Tick() => Play(LeashCue.Tick, "chaos/countdown_tick.mp3", 0.10f);
    public static void Cut() => Play(LeashCue.Cut, "chaos/snap.mp3", 0.22f);
    public static void CapReached() => Play(LeashCue.CapReached, "chaos/sink.mp3", 0.14f);

    /// <summary>Plays a cue on the one-shot player. Never blocks, never throws.</summary>
    private static void Play(LeashCue cue, string rel, float scale)
    {
        var tag = "leash-" + cue.ToString().ToLowerInvariant();
        try
        {
            int level = CoreSettings.Current?.MasterVolume ?? 0;
            if (level <= 0) return;
            bool video = global::ConditioningControlPanel.CoreEngine.Video?.IsPlaying == true;
            if (!Rules.TryPlay(cue, DateTime.UtcNow, PunishWindowOpen(), video)) return;
            var path = ContentLocator.Resolve(System.IO.Path.Combine("Resources", "sounds", rel.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            LastPlayed = tag;
            CoreAudio.PlayOneShot(path, Math.Clamp(level / 100f * scale, 0f, 1f), tag);
        }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] sfx {Tag} failed: {E}", tag, ex.Message); }
    }
}
