// PORTED from WPF 7.1.5, the launcher's art motion (hunt IA9):
//   - Ken-Burns drift on every tile's art (LauncherWindow.Backdrop.cs:40-42, 283-356): 1.00 to 1.07 and back,
//     14 s each way, sine in-out, written at 12 fps, held while the tile is hovered;
//   - the wordmark drift (Fx.cs:38-40, 407-428): 1.00 to 1.04 and back, 40 s each way, 10 fps;
//   - the wordmark's light pass (Fx.cs:41-43, 430-472): every 25-40 s, 1.1 s across, 0 -> 0.3 -> 0;
//   - the sparkle trail (Fx.cs:32-34, 302-372; Choreo.cs:55-56, 501-556): 3-5 sparks per move, one move per
//     30 ms, 48 live, 460 ms each, every eighth a star (520 ms), in the hovered tile's hue;
//   - the comets (Fx.cs:133-145, Choreo.cs:393-431): one laps the hovered tile in 7 s, one laps the panel card
//     in 12 s while the engine runs, and the card's rim breathes 0.45-1.0 over 2.6 s;
//   - the sheens (Fx.cs:376-403, Choreo.cs:435-484): one on the Launch button, one that visits a tile in view
//     every 6-10 s for 1.7 s;
//   - the running dot (Fx.cs:103-117): 0.55-1.0 over 2.6 s while the engine runs.
// WPF runs a storyboard per effect. Here the launcher's ONE frame clock steps all of it (StepArtMotion), so it
// stops with the launcher: hidden, minimised or not the active window (WPF ParkFx). No infinite Animation, no
// Effect: the comets and sheens are the port's adorners on their own capped frame clocks.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using Serilog;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class LauncherWindow
    {
        // Backdrop.cs:40-42
        internal const double KenBurnsTo = 1.07, KenBurnsSeconds = 14;
        private const double KenBurnsStep = 1.0 / 12;
        // Fx.cs:32-43
        private const int TrailMinGapMs = 30, TrailMaxLive = 48;
        private const double TrailLifeSeconds = 0.46, TransitionGuardSeconds = 0.65;
        internal const double WordmarkDriftTo = 1.04, WordmarkDriftSeconds = 40;
        private const double WordmarkDriftStep = 0.1;
        internal const double WordmarkSheenSeconds = 1.1;
        private const int WordmarkSheenMinGap = 25, WordmarkSheenMaxGap = 40;
        // Choreo.cs:46-56
        private const double PanelCometLapSeconds = 12, PanelRimBreathSeconds = 2.6, TileCometLapSeconds = 7;
        private const int WanderMinSeconds = 6, WanderMaxSeconds = 10;
        private const double WanderPassSeconds = 1.7;
        private const int StarEvery = 8;
        private const double StarLifeSeconds = 0.52;
        private const double WordmarkOutSeconds = 0.3;   // the drift's OUT: WPF snaps, here it eases home

        // Built on first use: a static initialiser would touch the render platform before the app has one.
        private static Geometry? _starGeometry;
        private static Geometry StarGeometry => _starGeometry ??= Geometry.Parse("M 0,-6 L 1.6,-1.6 L 6,0 L 1.6,1.6 L 0,6 L -1.6,1.6 L -6,0 L -1.6,-1.6 Z");

        private sealed class Spark
        {
            public Control Shape = null!;
            public TranslateTransform? Drift;
            public ScaleTransform? Bloom;
            public RotateTransform? Turn;
            public double T, Dx, Dy;
        }

        private readonly Random _artRng = new();
        private readonly List<Spark> _sparks = new();
        private readonly ScaleTransform _wordScale = new(1, 1);
        private readonly TranslateTransform _wordSheenSlide = new(-90, 0);
        private DateTime _lastTrail = DateTime.MinValue;
        private Color? _trailTint;
        private int _sparkSerial;
        private bool _artLive;
        private double _kenApplyIn, _wordT, _wordApplyIn, _trailHold;
        private double _wordSheenIn, _wordSheenT = -1, _wanderIn, _wanderEndIn, _ctaTryIn, _breathT;
        private Tween _wordOut;
        private PerimeterCometAdorner? _comet, _panelComet;
        private CardSheenAdorner? _ctaSheen, _wanderSheen;
        private IBrush? _panelRimRest;
        private SolidColorBrush? _panelRimLive;

        /// <summary>Tests: the engine state the panel card and the dot follow.</summary>
        internal Func<bool> EngineRunning = () => CoreEngine.IsRunning;

        internal double WordmarkZoom => _wordScale.ScaleX;
        internal double WordmarkSheenX => _wordSheenSlide.X;
        internal int TrailLive => _sparks.Count;
        internal bool TileCometOn => _comet != null;
        internal bool PanelCometOn => _panelComet != null;
        internal bool CtaSheenOn => _ctaSheen != null;
        internal bool WanderSheenOn => _wanderSheen != null;

        /// <summary>WPF ParkFx / MotionFx.AllowAmbientLoops: shown, not minimised, the active window, loops allowed.</summary>
        private bool LoopsOn => _fxActive && FxLive && Env.AllowAmbientLoops;
        private bool TrailAllowed => _fxActive && FxLive && !PerfLow && Env.AllowParticles && _trailHold <= 0
                                     && !LockdownVeil.IsVisible;

        /// <summary>Called once from HookFx.</summary>
        private void HookArtMotion()
        {
            WordmarkImage.RenderTransformOrigin = RelativePoint.Center;
            WordmarkImage.RenderTransform = _wordScale;
            var glow = Env.GlowColor;
            var clear = Color.FromArgb(0, glow.R, glow.G, glow.B);
            WordmarkSheen.Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(clear, 0), new GradientStop(glow, 0.5), new GradientStop(clear, 1) },
            };
            WordmarkSheen.RenderTransformOrigin = RelativePoint.TopLeft;   // WPF's default origin
            WordmarkSheen.RenderTransform = new TransformGroup { Children = { new SkewTransform(-18, 0), _wordSheenSlide } };
            PointerMoved += OnTrailPointerMoved;
            _wordSheenIn = NextGap(WordmarkSheenMinGap, WordmarkSheenMaxGap);
            _wanderIn = NextGap(WanderMinSeconds, WanderMaxSeconds);
        }

        private double NextGap(int min, int max) => _artRng.Next(min, max + 1);

        /// <summary>Triangle time into a sine in-out swing: WPF's auto-reversing DoubleAnimation with a SineEase.</summary>
        internal static double Swing(double t, double legSeconds, double from, double to)
        {
            double u = t % (2 * legSeconds) / legSeconds;
            if (u > 1) u = 2 - u;
            return from + (to - from) * (1 - Math.Cos(Math.PI * u)) / 2;
        }

        // ------------------------------------------------------------------ show / park

        private void ArtMotionOnShown()
        {
            _trailHold = TransitionGuardSeconds;   // Fx.cs MarkTransition: no trail while the entrance moves things
            _wordSheenIn = NextGap(WordmarkSheenMinGap, WordmarkSheenMaxGap);
            _wanderIn = NextGap(WanderMinSeconds, WanderMaxSeconds);
            _ctaTryIn = 0;
            SyncEngineFx();
        }

        /// <summary>WPF OnFxActivated / OnFxDeactivated: everything with a clock parks while another window is in front.</summary>
        private void ArtMotionActive(bool active)
        {
            if (!active) ParkArtMotion(hidden: false);
            else SyncEngineFx();
        }

        /// <summary>WPF ParkFx. Adorners leave, the trail clears, the light pass ends, the wordmark goes home
        /// (eased while the launcher still shows, snapped when it does not). The tile art holds its zoom.</summary>
        private void ParkArtMotion(bool hidden)
        {
            try
            {
                _artLive = false;
                PerimeterCometAdorner.Detach(_comet);
                _comet = null;
                CardSheenAdorner.Detach(_ctaSheen);
                _ctaSheen = null;
                CardSheenAdorner.Detach(_wanderSheen);
                _wanderSheen = null;
                _wanderEndIn = 0;
                ClearTrail();
                _wordSheenT = -1;
                WordmarkSheen.Opacity = 0;
                _wordSheenSlide.X = -90;
                _wordT = 0;
                if (hidden || !Env.AllowTransitions || !FxLive)
                {
                    _wordOut.On = false;
                    _wordScale.ScaleX = _wordScale.ScaleY = 1;
                    foreach (var t in _tileFx) t.Hovered = false;
                    _trailTint = null;
                }
                else if (Math.Abs(_wordScale.ScaleX - 1) > 0.0005) _wordOut.Go(_wordScale.ScaleX, 1, WordmarkOutSeconds);
                SyncEngineFx();
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] art motion park failed"); }
        }

        // ------------------------------------------------------------------ the frame

        /// <summary>One frame of the art motion; true while any of it is moving.</summary>
        private bool StepArtMotion(double dt)
        {
            bool busy = false;
            try
            {
                bool loops = LoopsOn, art = loops && !PerfLow;
                if (!loops && _artLive) ParkArtMotion(hidden: false);
                _artLive = loops;
                if (_trailHold > 0) { _trailHold -= dt; busy |= Env.AllowParticles; }   // only a launcher that can trail waits for it

                if (art)
                {
                    bool apply = (_kenApplyIn -= dt) <= 0;
                    if (apply) _kenApplyIn = KenBurnsStep;
                    foreach (var t in _tileFx)
                    {
                        if (t.Zoom == null || t.Hovered) continue;
                        busy = true;
                        t.ZoomT = (t.ZoomT + dt) % (2 * KenBurnsSeconds);
                        if (apply) t.Zoom.ScaleX = t.Zoom.ScaleY = Swing(t.ZoomT, KenBurnsSeconds, 1, KenBurnsTo);
                    }
                }

                if (loops)
                {
                    busy = true;
                    _wordOut.On = false;
                    _wordT = (_wordT + dt) % (2 * WordmarkDriftSeconds);
                    if ((_wordApplyIn -= dt) <= 0)
                    {
                        _wordApplyIn = WordmarkDriftStep;
                        _wordScale.ScaleX = _wordScale.ScaleY = Swing(_wordT, WordmarkDriftSeconds, 1, WordmarkDriftTo);
                    }
                    if (_wordSheenT >= 0) StepWordmarkSheen(dt);
                    else if ((_wordSheenIn -= dt) <= 0)
                    {
                        _wordSheenIn = NextGap(WordmarkSheenMinGap, WordmarkSheenMaxGap);
                        if (Wordmark.IsEffectivelyVisible) { _wordSheenT = 0; StepWordmarkSheen(0); }
                    }
                    if (_ctaSheen == null && (_ctaTryIn -= dt) <= 0)
                    {
                        _ctaTryIn = 1;   // no adorner layer yet: ask again in a second, never every frame
                        _ctaSheen = CardSheenAdorner.Attach(PanelCta, 12);
                    }
                    if (art) StepWander(dt);
                    else if (_wanderSheen != null) { CardSheenAdorner.Detach(_wanderSheen); _wanderSheen = null; }
                }
                else if (_wordOut.On)
                {
                    busy = true;
                    _wordScale.ScaleX = _wordScale.ScaleY = _wordOut.Step(dt);
                }

                SyncEngineFx();
                if (_panelRimLive != null || (loops && EngineRunning()))
                {
                    _breathT = (_breathT + dt) % (2 * PanelRimBreathSeconds);
                    if (_panelRimLive != null) _panelRimLive.Opacity = Swing(_breathT, PanelRimBreathSeconds, 0.45, 1.0);
                    if (loops) RunningDot.Opacity = Swing(_breathT, PanelRimBreathSeconds, 0.55, 1.0);
                }
                busy |= StepTrail(dt);
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] art motion frame failed"); }
            return busy;
        }

        // ------------------------------------------------------------------ Ken-Burns

        /// <summary>WPF BackdropDecorateArt: the art image carries its own zoom, inside the host that slides.</summary>
        private static void DecorateArtMotion(TileFx fx, Image? art)
        {
            if (art == null) return;
            fx.ArtImage = art;
            fx.Zoom = new ScaleTransform(1, 1);
            art.RenderTransformOrigin = RelativePoint.Center;
            art.RenderTransform = fx.Zoom;
        }

        /// <summary>Tests: the zoom on a tile's art (1 when the tile has none).</summary>
        internal double ArtZoom(Border tile)
        {
            foreach (var t in _tileFx) if (ReferenceEquals(t.Tile, tile)) return t.Zoom?.ScaleX ?? 1;
            return 1;
        }

        // ------------------------------------------------------------------ hover: comet, trail hue, held zoom

        private void ArtMotionTileHover(TileFx t, bool on)
        {
            t.Hovered = on;   // Backdrop.cs:120: the drift holds under the pointer and picks up where it was
            if (on) _trailTint = t.Hue;
            else
            {
                _trailTint = null;
                foreach (var other in _tileFx) if (other.Hovered) _trailTint = other.Hue;
            }
            PerimeterCometAdorner.Detach(_comet);
            _comet = null;
            if (on && LoopsOn && !PerfLow) _comet = PerimeterCometAdorner.Attach(t.Tile, TileRadius, TileCometLapSeconds);
        }

        // ------------------------------------------------------------------ the running engine

        /// <summary>Choreo.cs ChoreoEngineState + Fx.cs FxOnEngineState: the panel card's comet and breathing rim,
        /// and the dot's resting level. Cheap: nothing happens unless the state changed.</summary>
        internal void SyncEngineFx()
        {
            try
            {
                bool running = EngineRunning(), loops = LoopsOn, live = running && loops && !PerfLow;
                if (!(running && loops)) RunningDot.Opacity = running ? 1.0 : 0.35;
                if (live == (_panelComet != null)) return;
                if (live)
                {
                    _panelComet = PerimeterCometAdorner.Attach(PanelCard, PanelCard.CornerRadius.TopLeft, PanelCometLapSeconds);
                    if (_panelComet == null) return;
                    if (PanelCard.BorderBrush is ISolidColorBrush rim)
                    {
                        _panelRimRest = PanelCard.BorderBrush;
                        _panelRimLive = new SolidColorBrush(rim.Color);
                        PanelCard.BorderBrush = _panelRimLive;
                    }
                    EnsureFxClock();
                }
                else
                {
                    PerimeterCometAdorner.Detach(_panelComet);
                    _panelComet = null;
                    _panelRimLive = null;
                    if (_panelRimRest != null) PanelCard.BorderBrush = _panelRimRest;
                    _panelRimRest = null;
                }
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] engine fx failed"); }
        }

        // ------------------------------------------------------------------ the wandering sheen

        private void StepWander(double dt)
        {
            if (_wanderSheen != null && (_wanderEndIn -= dt) <= 0)
            {
                CardSheenAdorner.Detach(_wanderSheen);
                _wanderSheen = null;
            }
            if ((_wanderIn -= dt) > 0) return;
            _wanderIn = NextGap(WanderMinSeconds, WanderMaxSeconds);
            CardSheenAdorner.Detach(_wanderSheen);
            _wanderSheen = null;
            var candidates = new List<Border>();
            foreach (var t in _tileFx) if (TileInView(t.Tile)) candidates.Add(t.Tile);
            if (candidates.Count == 0) return;
            _wanderSheen = CardSheenAdorner.Attach(candidates[_artRng.Next(candidates.Count)], TileRadius);
            _wanderEndIn = WanderPassSeconds;
        }

        /// <summary>Choreo.cs TileInView: wholly inside the games column, a quarter tile of slack.</summary>
        private bool TileInView(Border tile)
        {
            try
            {
                if (tile.Parent == null || !tile.IsEffectivelyVisible || tile.Bounds.Height <= 0) return false;
                if (tile.TranslatePoint(default, GamesColumn) is not { } top) return false;
                double slack = tile.Bounds.Height * 0.25;
                return top.Y >= -slack && top.Y + tile.Bounds.Height <= GamesColumn.Bounds.Height + slack;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ the wordmark's light pass

        private void StepWordmarkSheen(double dt)
        {
            _wordSheenT += dt;
            double u = Math.Min(1, _wordSheenT / WordmarkSheenSeconds);
            double from = -WordmarkSheen.Width - 20, to = Wordmark.Bounds.Width + 20;
            _wordSheenSlide.X = from + (to - from) * (1 - Math.Cos(Math.PI * u)) / 2;
            WordmarkSheen.Opacity = Math.Clamp(u < 0.45 ? 0.3 * u / 0.45 : 0.3 * (1 - (u - 0.45) / 0.55), 0, 1);
            if (u < 1) return;
            _wordSheenT = -1;
            WordmarkSheen.Opacity = 0;
        }

        /// <summary>Tests: start the light pass now instead of waiting 25-40 s.</summary>
        internal void RunWordmarkSheenNow() { if (LoopsOn) { _wordSheenT = 0; EnsureFxClock(); } }

        /// <summary>Tests: send the wandering sheen now.</summary>
        internal void WanderNow() { _wanderIn = 0; }

        // ------------------------------------------------------------------ the sparkle trail

        private void OnTrailPointerMoved(object? sender, PointerEventArgs e)
        {
            try
            {
                if (!TrailAllowed) return;
                var now = DateTime.UtcNow;
                if ((now - _lastTrail).TotalMilliseconds < TrailMinGapMs) return;
                _lastTrail = now;
                TrailAt(e.GetPosition(TrailCanvas));
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] trail failed"); }
        }

        /// <summary>WPF OnFxMouseMove's body, without the 30 ms gate (tests call it): three to five sparks,
        /// 48 live at most, every eighth a star, in the hovered tile's hue when there is one.</summary>
        internal void TrailAt(Point at)
        {
            if (!TrailAllowed) return;
            var tint = _trailTint ?? Env.ParticleColor;
            int n = 3 + _artRng.Next(3);
            for (int i = 0; i < n && _sparks.Count < TrailMaxLive; i++)
            {
                if (++_sparkSerial % StarEvery == 0) SpawnStar(at, tint);
                else SpawnSpark(at, tint);
            }
            EnsureFxClock();
        }

        private void SpawnSpark(Point at, Color tint)
        {
            double size = 2.5 + _artRng.NextDouble() * 3.5;
            var drift = new TranslateTransform();
            var spark = new Ellipse
            {
                Width = size, Height = size, Fill = new SolidColorBrush(tint), Opacity = 0.9,
                IsHitTestVisible = false, RenderTransform = drift,
            };
            Canvas.SetLeft(spark, at.X - size / 2 + (_artRng.NextDouble() - 0.5) * 12);
            Canvas.SetTop(spark, at.Y - size / 2 + (_artRng.NextDouble() - 0.5) * 12);
            TrailCanvas.Children.Add(spark);
            _sparks.Add(new Spark
            {
                Shape = spark, Drift = drift,
                Dx = (_artRng.NextDouble() - 0.5) * 28, Dy = -8 - _artRng.NextDouble() * 22,
            });
        }

        /// <summary>Choreo.cs SpawnStar: a four-point star that blooms, turns a quarter and is gone.</summary>
        private void SpawnStar(Point at, Color tint)
        {
            var pale = Color.FromArgb(0xFF, (byte)((tint.R + 255) / 2), (byte)((tint.G + 255) / 2), (byte)((tint.B + 255) / 2));
            var bloom = new ScaleTransform(0.3, 0.3);
            var turn = new RotateTransform(0);
            var star = new Path
            {
                Data = StarGeometry, Fill = new SolidColorBrush(pale), Width = 13, Height = 13, Stretch = Stretch.Uniform,
                Opacity = 0, IsHitTestVisible = false, RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = new TransformGroup { Children = { bloom, turn } },
            };
            Canvas.SetLeft(star, at.X - 6.5 + (_artRng.NextDouble() - 0.5) * 16);
            Canvas.SetTop(star, at.Y - 6.5 + (_artRng.NextDouble() - 0.5) * 16);
            TrailCanvas.Children.Add(star);
            _sparks.Add(new Spark { Shape = star, Bloom = bloom, Turn = turn });
        }

        private bool StepTrail(double dt)
        {
            for (int i = _sparks.Count - 1; i >= 0; i--)
            {
                var s = _sparks[i];
                s.T += dt;
                bool star = s.Bloom != null;
                double u = Math.Min(1, s.T / (star ? StarLifeSeconds : TrailLifeSeconds));
                if (u >= 1)
                {
                    TrailCanvas.Children.Remove(s.Shape);
                    _sparks.RemoveAt(i);
                    continue;
                }
                if (star)
                {
                    // glow 0 -> 1 by 30 % -> 0; bloom 0.3 -> 1.15 by 40 % -> 0.5; a quarter turn, quad out.
                    s.Shape.Opacity = Math.Clamp(u < 0.3 ? u / 0.3 : 1 - (u - 0.3) / 0.7, 0, 1);
                    double b = u < 0.4 ? 0.3 + 0.85 * u / 0.4 : 1.15 - 0.65 * (u - 0.4) / 0.6;
                    s.Bloom!.ScaleX = s.Bloom.ScaleY = b;
                    s.Turn!.Angle = 90 * (1 - (1 - u) * (1 - u));
                }
                else
                {
                    s.Shape.Opacity = Math.Clamp(0.9 * (1 - u * u), 0, 1);   // quad in
                    s.Drift!.X = s.Dx * u;
                    s.Drift.Y = s.Dy * u;
                }
            }
            return _sparks.Count > 0;
        }

        private void ClearTrail()
        {
            _sparks.Clear();
            TrailCanvas.Children.Clear();
        }
    }
}
