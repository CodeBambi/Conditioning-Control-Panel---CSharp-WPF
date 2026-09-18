using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using Serilog;

namespace ConditioningControlPanel.Launcher;

/// <summary>
/// The event choreography: what happens when something is pressed, when the window comes back,
/// and while the engine runs. Play and the CTA get the full beat (pop, shockwave, two bursts, the
/// rest of the screen dims, the content shakes) and the host holds the hide for
/// <see cref="ExitBeatMs"/> so it can be seen. A return from a game is a welcome: bursts from the
/// Launch button and the wordmark. The running engine laps the panel card with a comet and breathes its
/// rim. A sheen wanders the tiles instead of sitting on the first one. The trail takes the hovered
/// tile's hue and throws a twinkling star every few sparks, and that hue bleeds into the backdrop.
///
/// <para>Same rails as the Fx partial: event tweens may run at 60 fps, every loop sits on the
/// MotionFx gates and on <see cref="_fxParked"/>, and no callback is allowed to throw.</para>
/// </summary>
public partial class LauncherWindow
{
    private const int ExitBeatMs = 450;
    private const int ShockwaveMs = 480;
    private const double ShockwaveFrom = 40;
    private const double ShockwaveTo = 700;
    private const int PlayBurstMain = 250;
    private const int PlayBurstEcho = 120;
    private const int EchoDelayMs = 120;
    private const double DimOthersTo = 0.55;
    private const int DimMs = 300;
    private const int ShakeMs = 320;
    private const double ShakeAmp = 5;
    private const double TilePopScale = 1.06;
    private const double CardLiftScale = 1.03;
    private const int PopMs = 380;
    private const int WelcomeEchoMs = 200;
    private const int WelcomeBurstCount = 110;
    private const double PanelCometLapSeconds = 12;
    private const double PanelRimBreathSeconds = 2.6;
    private const int WanderMinSeconds = 6;
    private const int WanderMaxSeconds = 10;
    private const int WanderPassMs = 1700;
    private const double GlowRestOpacity = 0.28;
    private const double GlowHoverOpacity = 0.45;
    private const int GlowBleedMs = 420;
    private const double VeilBreathSeconds = 3;
    private const int StarEvery = 8;
    private const int StarLifeMs = 520;

    private static readonly Geometry StarGeometry = BuildStar();

    private Border? _hoverTile;
    private Color? _trailTint;
    private int _sparkSerial;
    private Brush? _glowRestBrush;
    private PerimeterCometAdorner? _panelComet;
    private Brush? _panelRimRest;
    private SolidColorBrush? _panelRimLive;
    private DispatcherTimer? _wanderTimer;
    private DispatcherTimer? _wanderEndTimer;
    private CardSheenAdorner? _wanderSheen;
    private GradientStop? _veilEdge;
    private bool _veilHooked;
    private double _spShown = double.NaN;

    // ------------------------------------------------------------------ seams from the Fx partial

    private void ChoreoOnShown(bool firstShow)
    {
        try
        {
            LauncherHost.HideDelayMs = MotionFx.AllowTransitions ? ExitBeatMs : 0;
            ClearStaleHover();
            _trailTint = null;
            RestoreGlow(false);
            RestoreDim();
            ShakeSlide.BeginAnimation(TranslateTransform.XProperty, null);
            ShakeSlide.BeginAnimation(TranslateTransform.YProperty, null);
            ShakeSlide.X = ShakeSlide.Y = 0;
            HookVeil();
            RefreshSpReadout(countUp: true);
            if (!firstShow) WelcomeBack();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] ChoreoOnShown failed"); }
    }

    private void ChoreoPark()
    {
        try
        {
            StopWanderingSheen();
            _trailTint = null;
            RestoreGlow(false);
            RefreshVeilBreath();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] ChoreoPark failed"); }
    }

    private void ChoreoUnpark()
    {
        try
        {
            StartWanderingSheen();
            RefreshVeilBreath();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] ChoreoUnpark failed"); }
    }

    private void ChoreoOnClosed()
    {
        try
        {
            StopWanderingSheen();
            PerimeterCometAdorner.Detach(_panelComet);
            _panelComet = null;
            _panelRimLive?.BeginAnimation(Brush.OpacityProperty, null);
            _veilEdge?.BeginAnimation(GradientStop.ColorProperty, null);
            ShockwaveCanvas.Children.Clear();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] ChoreoOnClosed failed"); }
    }

    partial void FxOnStatusTick()
    {
        try { RefreshSpReadout(countUp: false); }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] SP readout failed"); }
    }

    // ------------------------------------------------------------------ the exit beats

    /// <summary>Play pressed. A locked tile only flinches; the host paints its toast.</summary>
    private void ChoreoPlay(Border tile, LauncherEntry entry)
    {
        if (entry.Locked) { Shake(0.45); return; }
        LauncherHost.ArmExitBeat();
        LauncherSfx.Launch();
        Pop(tile, TilePopScale);
        Shockwave(tile, entry.Hue);
        BurstAt(tile, entry.Hue, PlayBurstMain);
        Later(EchoDelayMs, () => BurstAt(tile, entry.Hue, PlayBurstEcho));
        DimOthers(tile);
        Shake(1.0);
    }

    /// <summary>The panel CTA pressed: the same beat in the theme's particle colour.</summary>
    private void ChoreoPanelLaunch()
    {
        LauncherHost.ArmExitBeat();
        LauncherSfx.Launch();
        var tint = FxColor("FxParticleColor");
        Pop(PanelCard, CardLiftScale);
        Shockwave(PanelCta, tint);
        BurstAt(PanelCta, tint, PlayBurstMain);
        Later(EchoDelayMs, () => BurstAt(PanelCta, tint, PlayBurstEcho));
        DimOthers(PanelCard);
        Shake(1.0);
    }

    /// <summary>A quick scale to <paramref name="peak"/> and an eased settle back to 1.</summary>
    private static void Pop(FrameworkElement element, double peak)
    {
        if (!MotionFx.AllowTransitions) return;
        var scale = EnsureScale(element);
        if (scale == null) return;
        var pop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(PopMs) };
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromPercent(0.35),
            new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1),
            new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    /// <summary>
    /// The scale on an element, made if there is none. A group gets one inserted first so the
    /// stagger's translate keeps working; an element carrying some other transform is left alone.
    /// </summary>
    private static ScaleTransform? EnsureScale(FrameworkElement element)
    {
        switch (element.RenderTransform)
        {
            case ScaleTransform s:
                return s;
            case TransformGroup g:
                var found = g.Children.OfType<ScaleTransform>().FirstOrDefault();
                if (found != null) return found;
                if (g.IsFrozen) return null;
                var added = new ScaleTransform(1, 1);
                g.Children.Insert(0, added);
                return added;
            case null:
            case MatrixTransform { Matrix.IsIdentity: true }:
                element.RenderTransformOrigin = new Point(0.5, 0.5);
                var fresh = new ScaleTransform(1, 1);
                element.RenderTransform = fresh;
                return fresh;
            default:
                return null;
        }
    }

    /// <summary>Two rings out of the anchor's centre: a bright one and a thin echo 90 ms behind.</summary>
    private void Shockwave(FrameworkElement anchor, Color color)
    {
        if (!MotionFx.AllowTransitions || !anchor.IsVisible || anchor.ActualWidth <= 0) return;
        var bounds = anchor.TransformToVisual(ShockwaveCanvas)
                           .TransformBounds(new Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));
        var centre = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        Ring(centre, color, 0, 3.5, 0.95);
        Ring(centre, color, 90, 1.5, 0.5);
    }

    private void Ring(Point centre, Color color, int delayMs, double thickness, double alpha)
    {
        double start = ShockwaveFrom / ShockwaveTo;
        var scale = new ScaleTransform(start, start);
        var ring = new Ellipse
        {
            Width = ShockwaveTo, Height = ShockwaveTo,
            Stroke = new SolidColorBrush(color), StrokeThickness = thickness,
            Opacity = 0, IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale,
        };
        Canvas.SetLeft(ring, centre.X - ShockwaveTo / 2);
        Canvas.SetTop(ring, centre.Y - ShockwaveTo / 2);
        ShockwaveCanvas.Children.Add(ring);

        var duration = TimeSpan.FromMilliseconds(ShockwaveMs);
        var begin = TimeSpan.FromMilliseconds(delayMs);
        var grow = new DoubleAnimation(start, 1.0, duration)
        {
            BeginTime = begin,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin, Duration = duration };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(alpha, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(alpha * 0.8, KeyTime.FromPercent(0.35)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        fade.Completed += (_, _) =>
        {
            try { ShockwaveCanvas.Children.Remove(ring); } catch { }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        ring.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>A decaying shake on the whole content: seven swings, five pixels down to none.</summary>
    private void Shake(double strength)
    {
        if (!MotionFx.AllowTransitions) return;
        double a = ShakeAmp * strength;
        double[] kx = { 0, a, -a * 0.8, a * 0.6, -a * 0.4, a * 0.2, -a * 0.1, 0 };
        double[] ky = { 0, -a * 0.6, a * 0.5, -a * 0.35, a * 0.2, -a * 0.1, 0, 0 };
        var x = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ShakeMs) };
        var y = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ShakeMs) };
        for (int i = 0; i < kx.Length; i++)
        {
            var at = KeyTime.FromPercent(i / (double)(kx.Length - 1));
            x.KeyFrames.Add(new LinearDoubleKeyFrame(kx[i], at));
            y.KeyFrames.Add(new LinearDoubleKeyFrame(ky[i], at));
        }
        x.Completed += (_, _) =>
        {
            try
            {
                ShakeSlide.BeginAnimation(TranslateTransform.XProperty, null);
                ShakeSlide.BeginAnimation(TranslateTransform.YProperty, null);
                ShakeSlide.X = ShakeSlide.Y = 0;
            }
            catch { }
        };
        ShakeSlide.BeginAnimation(TranslateTransform.XProperty, x);
        ShakeSlide.BeginAnimation(TranslateTransform.YProperty, y);
    }

    /// <summary>Everything that was not pressed steps back. The next show brings it all forward.</summary>
    private void DimOthers(FrameworkElement keep)
    {
        foreach (var tile in _tiles)
            if (!ReferenceEquals(tile, keep)) Fade(tile, DimOthersTo);
        if (!ReferenceEquals(PanelCard, keep)) Fade(PanelCard, DimOthersTo);
    }

    private static void Fade(UIElement element, double to)
    {
        if (!MotionFx.AllowTransitions) { element.Opacity = to; return; }
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(DimMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>The tiles are rebuilt on every show; the panel card is not, so it is reset here.</summary>
    private void RestoreDim()
    {
        PanelCard.BeginAnimation(UIElement.OpacityProperty, null);
        PanelCard.Opacity = 1;
    }

    // ------------------------------------------------------------------ welcome back

    private void WelcomeBack()
    {
        LauncherSfx.Return();
        BurstAt(PanelCta, FxColor("FxParticleColor"), WelcomeBurstCount);
        Later(WelcomeEchoMs, () => BurstAt(WordmarkImage, FxColor("FxGlowColor"), WelcomeBurstCount));
    }

    // ------------------------------------------------------------------ hover

    private void ChoreoTileHover(Border tile, LauncherEntry entry, bool on)
    {
        if (on)
        {
            _hoverTile = tile;
            _trailTint = entry.Hue;
            BleedGlow(entry.Hue);
        }
        else
        {
            if (ReferenceEquals(_hoverTile, tile)) _hoverTile = null;
            _trailTint = null;
            RestoreGlow(true);
        }
    }

    /// <summary>
    /// A tile still lifted with the cursor elsewhere (the window hid under the cursor and came
    /// back, so its leave never arrived). Cheap enough to check on every move.
    /// </summary>
    private void ClearStaleHover()
    {
        var tile = _hoverTile;
        if (tile == null || tile.IsMouseOver) return;
        _hoverTile = null;
        MotionFx.HoverLift(tile, false);
        if (tile.Tag is LauncherEntry entry) FxOnTileHover(tile, entry, false);
    }

    /// <summary>The hovered tile's hue takes over the backdrop glow, a little brighter.</summary>
    private void BleedGlow(Color hue)
    {
        if (_fxParked) return;
        _glowRestBrush ??= GlowLayer.Background;
        var stops = new GradientStopCollection
        {
            new GradientStop(hue, 0),
            new GradientStop(Color.FromArgb(0, hue.R, hue.G, hue.B), 1),
        };
        GlowLayer.Background = new RadialGradientBrush(stops)
        {
            GradientOrigin = new Point(0.72, 0.18), Center = new Point(0.72, 0.18),
            RadiusX = 0.55, RadiusY = 0.5,
        };
        AnimateOpacity(GlowLayer, GlowHoverOpacity, GlowBleedMs);
    }

    private void RestoreGlow(bool animate)
    {
        if (_glowRestBrush != null) GlowLayer.Background = _glowRestBrush;
        if (animate && MotionFx.AllowTransitions) AnimateOpacity(GlowLayer, GlowRestOpacity, GlowBleedMs);
        else
        {
            GlowLayer.BeginAnimation(UIElement.OpacityProperty, null);
            GlowLayer.Opacity = GlowRestOpacity;
        }
    }

    private static void AnimateOpacity(UIElement element, double to, int ms)
    {
        if (!MotionFx.AllowTransitions)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = to;
            return;
        }
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    // ------------------------------------------------------------------ the running engine

    private void ChoreoEngineState(bool running)
    {
        bool live = running && !_fxParked && !_perfLow && MotionFx.AllowAmbientLoops;
        if (live == (_panelComet != null)) return;
        if (live)
        {
            _panelComet = PerimeterCometAdorner.Attach(PanelCard, PanelCard.CornerRadius.TopLeft, PanelCometLapSeconds);
            if (_panelComet == null) return;
            if (PanelCard.BorderBrush is SolidColorBrush rim)
            {
                _panelRimRest = rim;
                _panelRimLive = new SolidColorBrush(rim.Color);
                PanelCard.BorderBrush = _panelRimLive;
                var breath = new DoubleAnimation(0.45, 1.0, TimeSpan.FromSeconds(PanelRimBreathSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(breath, AmbientFps);
                _panelRimLive.BeginAnimation(Brush.OpacityProperty, breath);
            }
        }
        else
        {
            PerimeterCometAdorner.Detach(_panelComet);
            _panelComet = null;
            if (_panelRimLive != null)
            {
                _panelRimLive.BeginAnimation(Brush.OpacityProperty, null);
                _panelRimLive = null;
            }
            if (_panelRimRest != null)
            {
                PanelCard.BorderBrush = _panelRimRest;
                _panelRimRest = null;
            }
        }
    }

    // ------------------------------------------------------------------ the wandering sheen

    private void StartWanderingSheen()
    {
        if (_perfLow || _fxParked || !MotionFx.AllowAmbientLoops) return;
        _wanderTimer ??= new DispatcherTimer(DispatcherPriority.Background);
        _wanderTimer.Tick -= OnWanderTick;
        _wanderTimer.Tick += OnWanderTick;
        _wanderTimer.Interval = NextWanderGap();
        _wanderTimer.Start();
    }

    private void StopWanderingSheen()
    {
        _wanderTimer?.Stop();
        _wanderEndTimer?.Stop();
        DetachSheen(ref _wanderSheen);
    }

    private TimeSpan NextWanderGap() =>
        TimeSpan.FromSeconds(_fxRng.Next(WanderMinSeconds, WanderMaxSeconds + 1));

    private void OnWanderTick(object? sender, EventArgs e)
    {
        try
        {
            if (_wanderTimer != null) _wanderTimer.Interval = NextWanderGap();
            if (_fxParked || !MotionFx.AllowAmbientLoops) return;
            DetachSheen(ref _wanderSheen);
            var candidates = _tiles.Where(TileInView).ToList();
            if (candidates.Count == 0) return;
            _wanderSheen = AttachSheen(candidates[_fxRng.Next(candidates.Count)], TileRadius);
            if (_wanderSheen == null) return;
            _wanderEndTimer ??= new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(WanderPassMs),
            };
            _wanderEndTimer.Tick -= OnWanderEnd;
            _wanderEndTimer.Tick += OnWanderEnd;
            _wanderEndTimer.Stop();
            _wanderEndTimer.Start();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] wandering sheen failed"); }
    }

    private void OnWanderEnd(object? sender, EventArgs e)
    {
        _wanderEndTimer?.Stop();
        DetachSheen(ref _wanderSheen);
    }

    private bool TileInView(Border tile)
    {
        try
        {
            if (!tile.IsVisible || tile.ActualHeight <= 0) return false;
            var bounds = tile.TransformToVisual(GamesColumn)
                             .TransformBounds(new Rect(0, 0, tile.ActualWidth, tile.ActualHeight));
            double slack = tile.ActualHeight * 0.25;
            return bounds.Top >= -slack && bounds.Bottom <= GamesColumn.ActualHeight + slack;
        }
        catch { return false; }
    }

    // ------------------------------------------------------------------ the star in the trail

    private static Geometry BuildStar()
    {
        var g = Geometry.Parse("M 0,-6 L 1.6,-1.6 L 6,0 L 1.6,1.6 L 0,6 L -1.6,1.6 L -6,0 L -1.6,-1.6 Z");
        g.Freeze();
        return g;
    }

    /// <summary>A four-point star that blooms, turns a quarter and is gone. Counts as a spark.</summary>
    private void SpawnStar(Point at, Color tint)
    {
        var pale = Color.FromArgb(0xFF, (byte)((tint.R + 255) / 2), (byte)((tint.G + 255) / 2), (byte)((tint.B + 255) / 2));
        var scale = new ScaleTransform(0.3, 0.3);
        var turn = new RotateTransform(0);
        var star = new Path
        {
            Data = StarGeometry, Fill = new SolidColorBrush(pale),
            Width = 13, Height = 13, Stretch = Stretch.Uniform,
            Opacity = 0, IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { scale, turn } },
        };
        Canvas.SetLeft(star, at.X - 6.5 + (_fxRng.NextDouble() - 0.5) * 16);
        Canvas.SetTop(star, at.Y - 6.5 + (_fxRng.NextDouble() - 0.5) * 16);
        TrailCanvas.Children.Add(star);
        _trailLive++;

        var life = TimeSpan.FromMilliseconds(StarLifeMs);
        var glow = new DoubleAnimationUsingKeyFrames { Duration = life };
        glow.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        glow.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.3)));
        glow.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        var bloom = new DoubleAnimationUsingKeyFrames { Duration = life };
        bloom.KeyFrames.Add(new LinearDoubleKeyFrame(0.3, KeyTime.FromPercent(0)));
        bloom.KeyFrames.Add(new LinearDoubleKeyFrame(1.15, KeyTime.FromPercent(0.4)));
        bloom.KeyFrames.Add(new LinearDoubleKeyFrame(0.5, KeyTime.FromPercent(1)));
        var spin = new DoubleAnimation(0, 90, life) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Timeline.SetDesiredFrameRate(glow, 30);
        Timeline.SetDesiredFrameRate(bloom, 30);
        Timeline.SetDesiredFrameRate(spin, 30);
        glow.Completed += (_, _) =>
        {
            try
            {
                TrailCanvas.Children.Remove(star);
                _trailLive = Math.Max(0, _trailLive - 1);
            }
            catch { }
        };
        star.BeginAnimation(UIElement.OpacityProperty, glow);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, bloom);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, bloom);
        turn.BeginAnimation(RotateTransform.AngleProperty, spin);
    }

    // ------------------------------------------------------------------ the lockdown veil

    private void HookVeil()
    {
        if (_veilHooked) return;
        _veilHooked = true;
        LockdownVeil.IsVisibleChanged += (_, _) => RefreshVeilBreath();
        RefreshVeilBreath();
    }

    /// <summary>A red vignette that breathes at the veil's edge, and the line of text with it.</summary>
    private void RefreshVeilBreath()
    {
        try
        {
            bool on = LockdownVeil.IsVisible && !_fxParked && MotionFx.AllowAmbientLoops;
            if (on)
            {
                if (_veilEdge == null)
                {
                    _veilEdge = new GradientStop(Color.FromArgb(0x66, 0xAA, 0x00, 0x22), 1);
                    var stops = new GradientStopCollection { new GradientStop(Colors.Transparent, 0.45), _veilEdge };
                    LockdownVignette.Background = new RadialGradientBrush(stops)
                    {
                        GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5),
                        RadiusX = 0.8, RadiusY = 0.8,
                    };
                }
                var breath = new ColorAnimation(Color.FromArgb(0x55, 0xAA, 0x00, 0x22), Color.FromArgb(0xB3, 0xAA, 0x00, 0x22),
                                                TimeSpan.FromSeconds(VeilBreathSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(breath, AmbientFps);
                _veilEdge.BeginAnimation(GradientStop.ColorProperty, breath);
                MotionFx.GlowBreath(LockdownText, 0.7, 1.0, VeilBreathSeconds);
            }
            else
            {
                _veilEdge?.BeginAnimation(GradientStop.ColorProperty, null);
                MotionFx.Stop(LockdownText);
                LockdownText.Opacity = 1;
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] veil breath failed"); }
    }

    // ------------------------------------------------------------------ sparkle points

    /// <summary>
    /// The chip's balance. Counts up from zero on show, tweens between values on the status tick
    /// after that. The tick before the first show pass only sets the chip's visibility.
    /// </summary>
    private void RefreshSpReadout(bool countUp)
    {
        int sp = App.Settings?.Current?.SkillPoints ?? -1;
        bool show = sp >= 0 && App.IsLoggedIn;
        SpChip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) { _spShown = double.NaN; return; }
        if (!countUp && double.IsNaN(_spShown)) return;
        double from = countUp ? 0 : _spShown;
        if (!countUp && Math.Abs(from - sp) < 0.5) return;
        MotionFx.Odometer(SpReadout, from, sp, "{0:N0}", countUp ? 0.9 : 0.6);
        _spShown = sp;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>One-shot timer for a beat that lands a little later. Its own timer, so beats
    /// never cancel each other.</summary>
    private static void Later(int ms, Action beat)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms)),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try { beat(); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] deferred beat failed"); }
        };
        timer.Start();
    }
}
