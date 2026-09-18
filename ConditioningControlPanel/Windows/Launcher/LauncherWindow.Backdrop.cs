using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Launcher;

/// <summary>
/// The backdrop's idle life: two spiral watermarks turning behind the content, every backdrop
/// layer nudged by the cursor for depth, and a slow Ken-Burns drift on each tile's art. The
/// ember layer lives in <see cref="AmbientFxCanvas"/> and is only configured from here.
///
/// <para>Same rails as the Fx partial: everything gated on <see cref="MotionFx"/>, ambient
/// clocks at 12 fps or lower, parked and unparked with the window, every callback wrapped. The
/// Performance tier keeps the static backdrop and loses all of the motion in this file.</para>
/// </summary>
public partial class LauncherWindow
{
    private const int ParallaxMinGapMs = 33;
    private const int ParallaxFps = 30;
    private const int ParallaxEaseMs = 260;
    private const double ParallaxGlowPx = 18;
    private const double ParallaxPoolPx = 14;
    private const double ParallaxSpiralPx = 12;
    private const double ParallaxAmbientPx = 8;

    private const double SpiralTurns = 7;
    private const double SpiralInnerRadius = 14;
    private const double SpiralTurnSeconds = 90;
    private const double SpiralSmallTurnSeconds = 140;
    private const int SpiralFps = 10;

    private const double KenBurnsTo = 1.07;
    private const double KenBurnsSeconds = 14;
    private const int KenBurnsFps = 12;

    private bool _backdropHooked;
    private bool _backdropParked = true;
    private bool _spiralBuilt;
    private DateTime _lastParallax = DateTime.MinValue;
    private int _kenBurnsSeed;
    private readonly List<(Image Art, ScaleTransform Scale, bool Outward)> _kenBurns = new();

    // ------------------------------------------------------------------ seams (called from Fx)

    private void BackdropOnShown()
    {
        try
        {
            BuildSpirals();
            HookBackdropEvents();
            _backdropParked = false;
            StartSpiralTurn();
            // Tiles were just rebuilt; their art is in the tree but not laid out yet.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, StartKenBurns);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] BackdropOnShown failed"); }
    }

    private void BackdropPark()
    {
        if (_backdropParked) return;
        _backdropParked = true;
        try
        {
            StopSpiralTurn();
            StopKenBurns();
            SettleParallax();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] BackdropPark failed"); }
    }

    private void BackdropUnpark()
    {
        if (!_backdropParked) return;
        _backdropParked = false;
        try
        {
            StartSpiralTurn();
            StartKenBurns();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] BackdropUnpark failed"); }
    }

    private void BackdropOnClosed()
    {
        try
        {
            BackdropPark();
            UnhookBackdropEvents();
            _kenBurns.Clear();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] BackdropOnClosed failed"); }
    }

    /// <summary>
    /// Registers a tile's art for the Ken-Burns drift. The scale lives in its own group on the
    /// image's RenderTransform; HoverPop wraps that group on the first hover, and refuses to when
    /// the group is mid-animation, so the drift is paused for the hover (it is popping anyway)
    /// and resumed on leave. This handler is subscribed before HoverPop's, which is what makes
    /// the pause land first.
    /// </summary>
    private void BackdropDecorateArt(Image art)
    {
        try
        {
            var scale = new ScaleTransform(1, 1);
            art.RenderTransformOrigin = new Point(0.5, 0.5);
            art.RenderTransform = new TransformGroup { Children = { scale } };
            bool outward = (_kenBurnsSeed++ & 1) == 0;
            _kenBurns.Add((art, scale, outward));

            art.MouseEnter += (_, _) => PauseKenBurns(scale);
            art.MouseLeave += (_, _) => { if (!_backdropParked) ResumeKenBurns(scale, outward); };
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] BackdropDecorateArt failed"); }
    }

    // ------------------------------------------------------------------ events

    private void HookBackdropEvents()
    {
        if (_backdropHooked) return;
        _backdropHooked = true;
        MouseMove += OnBackdropMouseMove;
        MouseLeave += OnBackdropMouseLeave;
    }

    private void UnhookBackdropEvents()
    {
        if (!_backdropHooked) return;
        _backdropHooked = false;
        MouseMove -= OnBackdropMouseMove;
        MouseLeave -= OnBackdropMouseLeave;
    }

    // ------------------------------------------------------------------ the spirals

    private void BuildSpirals()
    {
        if (_spiralBuilt) return;
        _spiralBuilt = true;
        SpiralPath.Data = BuildSpiral(SpiralLayer.Width, SpiralTurns, SpiralInnerRadius);
        SpiralSmallPath.Data = BuildSpiral(SpiralSmall.Width, SpiralTurns - 1.5, SpiralInnerRadius);
    }

    /// <summary>An Archimedean spiral (r grows linearly with the angle) as one frozen polyline.</summary>
    private static Geometry BuildSpiral(double size, double turns, double innerRadius)
    {
        var geometry = new StreamGeometry();
        double c = size / 2;
        double maxTheta = turns * Math.PI * 2;
        double growth = (c - innerRadius - 4) / maxTheta;
        const double step = Math.PI / 45;
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(c + innerRadius, c), false, false);
            for (double theta = step; theta <= maxTheta; theta += step)
            {
                double r = innerRadius + growth * theta;
                ctx.LineTo(new Point(c + r * Math.Cos(theta), c + r * Math.Sin(theta)), true, true);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private void StartSpiralTurn()
    {
        try
        {
            if (!MotionFx.AllowAmbientLoops || _perfLow) { StopSpiralTurn(); return; }
            Spin(SpiralTurn, SpiralTurnSeconds, clockwise: true);
            Spin(SpiralSmallTurn, SpiralSmallTurnSeconds, clockwise: false);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] spiral turn failed"); }
    }

    private static void Spin(RotateTransform turn, double secondsPerTurn, bool clockwise)
    {
        double from = turn.Angle;
        var spin = new DoubleAnimation(from, from + (clockwise ? 360 : -360), TimeSpan.FromSeconds(secondsPerTurn))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Timeline.SetDesiredFrameRate(spin, SpiralFps);
        turn.BeginAnimation(RotateTransform.AngleProperty, spin);
    }

    /// <summary>Freezes each spiral where it is; the next start continues from that angle.</summary>
    private void StopSpiralTurn()
    {
        Hold(SpiralTurn);
        Hold(SpiralSmallTurn);
    }

    private static void Hold(RotateTransform turn)
    {
        double angle = turn.Angle % 360;
        turn.BeginAnimation(RotateTransform.AngleProperty, null);
        turn.Angle = angle;
    }

    // ------------------------------------------------------------------ parallax

    private void OnBackdropMouseMove(object sender, MouseEventArgs e)
    {
        try
        {
            if (_backdropParked || _perfLow || !MotionFx.AllowTransitions) return;
            var now = DateTime.UtcNow;
            if ((now - _lastParallax).TotalMilliseconds < ParallaxMinGapMs) return;
            _lastParallax = now;

            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            var at = e.GetPosition(this);
            double nx = Math.Clamp(at.X / ActualWidth * 2 - 1, -1, 1);
            double ny = Math.Clamp(at.Y / ActualHeight * 2 - 1, -1, 1);
            ApplyParallax(nx, ny);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] parallax failed"); }
    }

    private void OnBackdropMouseLeave(object sender, MouseEventArgs e) => SettleParallax();

    /// <summary>
    /// Near layers follow the cursor, far layers move against it; the difference is the depth.
    /// The glow and the ember canvas ride with the hand, the spirals lean away.
    /// </summary>
    private void ApplyParallax(double nx, double ny)
    {
        Ease(GlowShift, nx * ParallaxGlowPx, ny * ParallaxGlowPx);
        Ease(PoolShift, nx * ParallaxPoolPx, ny * ParallaxPoolPx);
        Ease(AmbientShift, nx * ParallaxAmbientPx, ny * ParallaxAmbientPx);
        Ease(SpiralShift, -nx * ParallaxSpiralPx, -ny * ParallaxSpiralPx);
        Ease(SpiralSmallShift, -nx * ParallaxSpiralPx * 0.7, -ny * ParallaxSpiralPx * 0.7);
    }

    private void SettleParallax()
    {
        try
        {
            if (MotionFx.AllowTransitions && !_perfLow && IsVisible) ApplyParallax(0, 0);
            else
            {
                foreach (var shift in new[] { GlowShift, PoolShift, AmbientShift, SpiralShift, SpiralSmallShift })
                {
                    shift.BeginAnimation(TranslateTransform.XProperty, null);
                    shift.BeginAnimation(TranslateTransform.YProperty, null);
                    shift.X = shift.Y = 0;
                }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] parallax settle failed"); }
    }

    /// <summary>Retargets the shift with a short ease; a fresh target mid-flight hands off smoothly.</summary>
    private static void Ease(TranslateTransform shift, double x, double y)
    {
        var dx = new DoubleAnimation(x, TimeSpan.FromMilliseconds(ParallaxEaseMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        var dy = new DoubleAnimation(y, TimeSpan.FromMilliseconds(ParallaxEaseMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        Timeline.SetDesiredFrameRate(dx, ParallaxFps);
        Timeline.SetDesiredFrameRate(dy, ParallaxFps);
        shift.BeginAnimation(TranslateTransform.XProperty, dx);
        shift.BeginAnimation(TranslateTransform.YProperty, dy);
    }

    // ------------------------------------------------------------------ Ken-Burns

    private void StartKenBurns()
    {
        try
        {
            // Tiles rebuild on every show; entries whose art left the tree are dropped here.
            _kenBurns.RemoveAll(k => PresentationSource.FromVisual(k.Art) == null);
            if (!MotionFx.AllowAmbientLoops || _perfLow || _backdropParked) { StopKenBurns(); return; }
            foreach (var (art, scale, outward) in _kenBurns)
            {
                if (art.IsMouseOver) continue;
                ResumeKenBurns(scale, outward);
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] ken-burns failed"); }
    }

    private void StopKenBurns()
    {
        foreach (var (_, scale, _) in _kenBurns) PauseKenBurns(scale);
    }

    /// <summary>1.00 to 1.07 and back over 14 s each way; odd tiles start from the far end.</summary>
    private void ResumeKenBurns(ScaleTransform scale, bool outward)
    {
        if (!MotionFx.AllowAmbientLoops || _perfLow) return;
        double from = scale.ScaleX;
        double to = outward ? KenBurnsTo : 1.0;
        if (Math.Abs(from - to) < 0.002) to = outward ? 1.0 : KenBurnsTo;
        var drift = new DoubleAnimation(from, to, TimeSpan.FromSeconds(KenBurnsSeconds * Math.Abs(to - from) / (KenBurnsTo - 1.0)))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(drift, KenBurnsFps);
        drift.Completed += (_, _) =>
        {
            try
            {
                if (_backdropParked || scale.ScaleX != to) return;
                Loop(scale, to);
            }
            catch { }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, drift);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, drift);
    }

    /// <summary>The steady state after the first leg: a full 14 s auto-reversing swing.</summary>
    private void Loop(ScaleTransform scale, double at)
    {
        double other = Math.Abs(at - 1.0) < 0.002 ? KenBurnsTo : 1.0;
        var swing = new DoubleAnimation(at, other, TimeSpan.FromSeconds(KenBurnsSeconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(swing, KenBurnsFps);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, swing);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, swing);
    }

    /// <summary>Holds the art at its current zoom with no clock on it.</summary>
    private static void PauseKenBurns(ScaleTransform scale)
    {
        try
        {
            double at = scale.ScaleX;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = scale.ScaleY = at;
        }
        catch { }
    }
}
