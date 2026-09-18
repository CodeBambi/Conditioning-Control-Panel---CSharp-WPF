using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Behaviors;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using Serilog;

namespace ConditioningControlPanel.Launcher;

/// <summary>
/// The juice. Fog, dust and an aurora wash behind the content; a sparkle trail under the cursor;
/// a comet lapping the hovered tile; sheen on the CTA and the wordmark; the croupier Emi waving at
/// the Back Room tile. The event beats (Play, the CTA, the return, the running engine, the
/// wandering sheen, the veil) live in the Choreo partial and hang off the seams here.
///
/// <para>Rails, all of them: every loop is gated on <see cref="MotionFx.AllowAmbientLoops"/> or
/// <see cref="MotionFx.AllowParticles"/>, everything parks on Deactivated and on minimize, ambient
/// timelines run at 24 fps or lower, <c>element.BeginAnimation</c> only, and every callback is
/// wrapped: decoration is never why the launcher throws. The Performance tier keeps the fog and
/// the bursts and loses the trail and the comet.</para>
/// </summary>
public partial class LauncherWindow
{
    private const int AmbientFps = 24;
    private const int TrailMinGapMs = 30;
    private const int TrailMaxLive = 48;
    private const int TrailLifeMs = 460;
    private const int OpenBurstCount = 70;
    private const double WordmarkDriftTo = 1.04;
    private const double WordmarkDriftSeconds = 40;
    private const int WordmarkDriftFps = 10;
    private const double WordmarkSheenSeconds = 1.1;
    private const int WordmarkSheenMinGapSeconds = 25;
    private const int WordmarkSheenMaxGapSeconds = 40;
    private const int TransitionGuardMs = 650;

    private readonly Random _fxRng = new();
    private bool _fxHooked;
    private bool _perfLow;
    private bool _fxParked;
    private bool _transitionBusy;
    private bool _dotBreathing;
    private DateTime _lastTrail = DateTime.MinValue;
    private int _trailLive;
    private PerimeterCometAdorner? _comet;
    private CardSheenAdorner? _ctaSheen;
    private DispatcherTimer? _wordmarkSheenTimer;
    private DispatcherTimer? _transitionTimer;

    // ------------------------------------------------------------------ seams

    partial void FxOnShown(bool firstShow)
    {
        try
        {
            _perfLow = PerformanceProfile.CurrentTier == PerformanceTier.Performance;
            HookFxWindowEvents();
            _fxParked = false;
            MarkTransition();
            StartAmbient();
            StartWordmarkDrift();
            StartWordmarkSheenTimer();
            // Adorners need a laid-out tree; the tiles were just rebuilt.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                StartSheens();
                BurstAt(WordmarkImage, FxColor("FxGlowColor"), OpenBurstCount);
                ChoreoOnShown(firstShow);
            });
            BackdropOnShown();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] FxOnShown failed"); }
    }

    partial void FxOnHidden()
    {
        try { ParkFx(); }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] FxOnHidden failed"); }
    }

    partial void FxOnClosed()
    {
        try
        {
            ParkFx();
            UnhookFxWindowEvents();
            BackdropOnClosed();
            ChoreoOnClosed();
            Ambient.Stop();
            BurstLayer.Stop();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] FxOnClosed failed"); }
    }

    partial void FxOnEngineState(bool running)
    {
        try
        {
            ChoreoEngineState(running);
            bool breathe = running && MotionFx.AllowAmbientLoops && !_fxParked;
            if (breathe == _dotBreathing) return;
            _dotBreathing = breathe;
            if (breathe) MotionFx.GlowBreath(RunningDot, 0.55, 1.0, 2.6);
            else
            {
                MotionFx.Stop(RunningDot);
                RunningDot.Opacity = running ? 1.0 : 0.35;
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] dot breath failed"); }
    }

    partial void FxOnPanelLaunch()
    {
        try { ChoreoPanelLaunch(); }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] CTA beat failed"); }
    }

    partial void FxOnPlay(Border tile, LauncherEntry entry)
    {
        try { ChoreoPlay(tile, entry); }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] play beat failed"); }
    }

    partial void FxOnTileHover(Border tile, LauncherEntry entry, bool on)
    {
        try
        {
            PerimeterCometAdorner.Detach(_comet);
            _comet = null;
            if (on && !_perfLow && !_fxParked && MotionFx.AllowAmbientLoops)
                _comet = PerimeterCometAdorner.Attach(tile, TileRadius, 7.0);

            if (string.Equals(entry.Id, "backroom", StringComparison.OrdinalIgnoreCase))
                MascotWave(on);
            ChoreoTileHover(tile, entry, on);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] tile hover fx failed"); }
    }

    partial void FxDecorateTile(Border tile, LauncherEntry entry)
    {
        try
        {
            if (!entry.Locked) return;
            TierFxBorder.SetRimThickness(tile, tile.BorderThickness.Left);
            TierFxBorder.SetTier(tile, 2);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] tier rim failed"); }
    }

    partial void FxDecorateArt(Image art)
    {
        try { BackdropDecorateArt(art); HoverPop.SetIsEnabled(art, true); }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] hover pop failed"); }
    }

    // ------------------------------------------------------------------ window state

    private void HookFxWindowEvents()
    {
        if (_fxHooked) return;
        _fxHooked = true;
        Activated += OnFxActivated;
        Deactivated += OnFxDeactivated;
        StateChanged += OnFxStateChanged;
        MouseMove += OnFxMouseMove;
    }

    private void UnhookFxWindowEvents()
    {
        if (!_fxHooked) return;
        _fxHooked = false;
        Activated -= OnFxActivated;
        Deactivated -= OnFxDeactivated;
        StateChanged -= OnFxStateChanged;
        MouseMove -= OnFxMouseMove;
    }

    private void OnFxActivated(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized) UnparkFx();
    }

    private void OnFxDeactivated(object? sender, EventArgs e) => ParkFx();

    private void OnFxStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized) ParkFx();
        else if (IsActive) UnparkFx();
    }

    /// <summary>Everything with a clock stops. The composed state stays where it is.</summary>
    private void ParkFx()
    {
        if (_fxParked) return;
        _fxParked = true;
        try
        {
            Ambient.Pause();
            _wordmarkSheenTimer?.Stop();
            StopWordmarkDrift();
            _ctaSheen?.Stop();
            ChoreoPark();
            PerimeterCometAdorner.Detach(_comet);
            _comet = null;
            MascotWave(false);
            TrailCanvas.Children.Clear();
            _trailLive = 0;
            BackdropPark();
            FxOnEngineState(App.IsEngineRunning);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] ParkFx failed"); }
    }

    private void UnparkFx()
    {
        if (!_fxParked || !IsVisible) return;
        _fxParked = false;
        try
        {
            Ambient.Resume();
            StartWordmarkDrift();
            StartWordmarkSheenTimer();
            if (MotionFx.AllowAmbientLoops) _ctaSheen?.Start();
            BackdropUnpark();
            ChoreoUnpark();
            FxOnEngineState(App.IsEngineRunning);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] UnparkFx failed"); }
    }

    /// <summary>The trail stays off while a Storyboard-heavy entrance is still moving things.</summary>
    private void MarkTransition()
    {
        _transitionBusy = true;
        _transitionTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TransitionGuardMs) };
        _transitionTimer.Tick -= OnTransitionGuardTick;
        _transitionTimer.Tick += OnTransitionGuardTick;
        _transitionTimer.Stop();
        _transitionTimer.Start();
    }

    private void OnTransitionGuardTick(object? sender, EventArgs e)
    {
        _transitionTimer?.Stop();
        _transitionBusy = false;
    }

    // ------------------------------------------------------------------ ambient + bursts

    private void StartAmbient()
    {
        if (!MotionFx.AllowAmbientLoops) { Ambient.Stop(); return; }
        Ambient.StartLayers(new AmbientFxConfig
        {
            Layers = AmbientFxLayers.FogDrift | AmbientFxLayers.DustField | AmbientFxLayers.AuroraWash
                   | AmbientFxLayers.Embers,
            Intensity = 0.95,
            FogPuffs = 4,
            DustDensity = 1.4,
        });
    }

    /// <summary>
    /// A burst at the centre of <paramref name="anchor"/>, mapped into the burst layer with
    /// TransformToVisual (the only correct mapping) and vetted by <see cref="FxBurstAnchor"/> so a
    /// tile scrolled out of view or a collapsed control never fires off-canvas.
    /// </summary>
    private void BurstAt(FrameworkElement? anchor, Color color, int count)
    {
        try
        {
            if (anchor == null || !MotionFx.AllowParticles) return;
            if (!anchor.IsVisible || anchor.ActualWidth <= 0 || anchor.ActualHeight <= 0) return;
            var bounds = anchor.TransformToVisual(BurstLayer)
                               .TransformBounds(new Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));
            if (!FxBurstAnchor.TryResolve(bounds, new Size(BurstLayer.ActualWidth, BurstLayer.ActualHeight),
                                          FxBurstSpot.Center, out var origin))
                return;
            BurstLayer.Burst(origin.X, origin.Y, color, count);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] burst failed"); }
    }

    private Color FxColor(string key)
    {
        try
        {
            if (TryFindResource(key) is Color c) return c;
            if (TryFindResource("PinkColor") is Color p) return p;
        }
        catch { }
        return Colors.HotPink;
    }

    // ------------------------------------------------------------------ the sparkle trail

    /// <summary>
    /// Three to five tiny sparks under the cursor, throttled to one spawn per 30 ms and capped at
    /// 48 live, in the hovered tile's hue when there is one and every eighth a star. Plain
    /// ellipses with a short fade and drift rather than a canvas burst: the burst sim has a
    /// 60-particle floor, which is a firework, not a trail.
    /// </summary>
    private void OnFxMouseMove(object sender, MouseEventArgs e)
    {
        try
        {
            ClearStaleHover();
            if (_perfLow || _fxParked || _transitionBusy || !MotionFx.AllowParticles) return;
            if (LockdownVeil.Visibility == Visibility.Visible) return;
            var now = DateTime.UtcNow;
            if ((now - _lastTrail).TotalMilliseconds < TrailMinGapMs) return;
            _lastTrail = now;

            var at = e.GetPosition(TrailCanvas);
            var tint = _trailTint ?? FxColor("FxParticleColor");
            int n = 3 + _fxRng.Next(3);
            for (int i = 0; i < n && _trailLive < TrailMaxLive; i++)
            {
                if (++_sparkSerial % StarEvery == 0) SpawnStar(at, tint);
                else SpawnSpark(at, tint);
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] trail failed"); }
    }

    private void SpawnSpark(Point at, Color tint)
    {
        double size = 2.5 + _fxRng.NextDouble() * 3.5;
        var spark = new Ellipse
        {
            Width = size, Height = size,
            Fill = new SolidColorBrush(tint),
            Opacity = 0.9,
            IsHitTestVisible = false,
        };
        var drift = new TranslateTransform();
        spark.RenderTransform = drift;
        Canvas.SetLeft(spark, at.X - size / 2 + (_fxRng.NextDouble() - 0.5) * 12);
        Canvas.SetTop(spark, at.Y - size / 2 + (_fxRng.NextDouble() - 0.5) * 12);
        TrailCanvas.Children.Add(spark);
        _trailLive++;

        var life = TimeSpan.FromMilliseconds(TrailLifeMs);
        var fade = new DoubleAnimation(0.9, 0, life) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        var dx = new DoubleAnimation(0, (_fxRng.NextDouble() - 0.5) * 28, life);
        var dy = new DoubleAnimation(0, -8 - _fxRng.NextDouble() * 22, life);
        Timeline.SetDesiredFrameRate(fade, 30);
        Timeline.SetDesiredFrameRate(dx, 30);
        Timeline.SetDesiredFrameRate(dy, 30);
        fade.Completed += (_, _) =>
        {
            try
            {
                TrailCanvas.Children.Remove(spark);
                _trailLive = Math.Max(0, _trailLive - 1);
            }
            catch { }
        };
        spark.BeginAnimation(UIElement.OpacityProperty, fade);
        drift.BeginAnimation(TranslateTransform.XProperty, dx);
        drift.BeginAnimation(TranslateTransform.YProperty, dy);
    }

    // ------------------------------------------------------------------ sheens

    private void StartSheens()
    {
        try
        {
            DetachSheen(ref _ctaSheen);
            StopWanderingSheen();
            if (!MotionFx.AllowAmbientLoops || _fxParked) return;
            _ctaSheen = AttachSheen(PanelCta, 12);
            StartWanderingSheen();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] sheens failed"); }
    }

    private static CardSheenAdorner? AttachSheen(UIElement host, double radius)
    {
        var layer = AdornerLayer.GetAdornerLayer(host);
        if (layer == null) return null;
        var sheen = new CardSheenAdorner(host, radius);
        layer.Add(sheen);
        sheen.Start();
        return sheen;
    }

    private static void DetachSheen(ref CardSheenAdorner? sheen)
    {
        if (sheen == null) return;
        try
        {
            sheen.Stop();
            AdornerLayer.GetAdornerLayer(sheen.AdornedElement)?.Remove(sheen);
        }
        catch { }
        sheen = null;
    }

    // ------------------------------------------------------------------ the wordmark

    /// <summary>Ken-Burns drift on the logo: 1.00 to 1.04 over 40 s and back, 10 fps.</summary>
    private void StartWordmarkDrift()
    {
        try
        {
            if (!MotionFx.AllowAmbientLoops) { StopWordmarkDrift(); return; }
            var drift = new DoubleAnimation(1.0, WordmarkDriftTo, TimeSpan.FromSeconds(WordmarkDriftSeconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(drift, WordmarkDriftFps);
            WordmarkScale.BeginAnimation(ScaleTransform.ScaleXProperty, drift);
            WordmarkScale.BeginAnimation(ScaleTransform.ScaleYProperty, drift);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] wordmark drift failed"); }
    }

    private void StopWordmarkDrift()
    {
        WordmarkScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        WordmarkScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        WordmarkScale.ScaleX = WordmarkScale.ScaleY = 1.0;
    }

    private void StartWordmarkSheenTimer()
    {
        if (!MotionFx.AllowAmbientLoops) return;
        _wordmarkSheenTimer ??= new DispatcherTimer(DispatcherPriority.Background);
        _wordmarkSheenTimer.Tick -= OnWordmarkSheenTick;
        _wordmarkSheenTimer.Tick += OnWordmarkSheenTick;
        _wordmarkSheenTimer.Interval = NextWordmarkSheenGap();
        _wordmarkSheenTimer.Start();
    }

    private TimeSpan NextWordmarkSheenGap() =>
        TimeSpan.FromSeconds(_fxRng.Next(WordmarkSheenMinGapSeconds, WordmarkSheenMaxGapSeconds + 1));

    private void OnWordmarkSheenTick(object? sender, EventArgs e)
    {
        try
        {
            if (_wordmarkSheenTimer != null) _wordmarkSheenTimer.Interval = NextWordmarkSheenGap();
            if (_fxParked || !MotionFx.AllowAmbientLoops || !Wordmark.IsVisible) return;

            var travel = TimeSpan.FromSeconds(WordmarkSheenSeconds);
            var slide = new DoubleAnimation(-WordmarkSheen.Width - 20, Wordmark.ActualWidth + 20, travel)
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            var glow = new DoubleAnimationUsingKeyFrames { Duration = travel };
            glow.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            glow.KeyFrames.Add(new LinearDoubleKeyFrame(0.3, KeyTime.FromPercent(0.45)));
            glow.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            Timeline.SetDesiredFrameRate(slide, AmbientFps);
            Timeline.SetDesiredFrameRate(glow, AmbientFps);
            WordmarkSheenSlide.BeginAnimation(TranslateTransform.XProperty, slide);
            WordmarkSheen.BeginAnimation(UIElement.OpacityProperty, glow);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] wordmark sheen failed"); }
    }

    // ------------------------------------------------------------------ the mascot

    /// <summary>
    /// Emi waves at the Back Room tile: the arm loops about its shoulder (2 s, auto-reverse) and
    /// she rises a little out of the column's edge. Off the tile she settles and goes still.
    /// </summary>
    private void MascotWave(bool on)
    {
        try
        {
            bool loops = on && !_fxParked && MotionFx.AllowAmbientLoops;
            if (loops)
            {
                var wave = new DoubleAnimation(-4, -30, TimeSpan.FromSeconds(1))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(wave, AmbientFps);
                MascotArmTurn.BeginAnimation(RotateTransform.AngleProperty, wave);
            }
            else
            {
                MascotArmTurn.BeginAnimation(RotateTransform.AngleProperty, null);
                MascotArmTurn.Angle = 0;
            }

            double lift = on && !_fxParked ? -26 : 0;
            if (MotionFx.AllowTransitions)
            {
                var rise = new DoubleAnimation(lift, TimeSpan.FromMilliseconds(340))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                Timeline.SetDesiredFrameRate(rise, AmbientFps);
                MascotSlide.BeginAnimation(TranslateTransform.YProperty, rise);
            }
            else
            {
                MascotSlide.BeginAnimation(TranslateTransform.YProperty, null);
                MascotSlide.Y = lift;
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] mascot wave failed"); }
    }
}
