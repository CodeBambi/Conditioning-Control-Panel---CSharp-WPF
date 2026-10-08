// PORTED (launcher slice 5) from WPF LauncherWindow.Backdrop.cs (spirals), .Fx.cs (ambient, bursts, park),
// .Choreo.cs (exit beat: pop, shockwave, bursts, dim, shake; welcome back; hover glow bleed), .Tiles.cs:43-52/230-244/
// 385-440 (hover lift, tilt, tile glow), .Edge.cs (flare, breath, glint), .Crossfade.cs (fade in/out) and
// Services/Launcher/LauncherSfx.cs (the cues; the melody is Core's LauncherMelody).
// WPF runs one storyboard per tween; here ONE frame clock steps every tween, and only while the launcher is shown
// and not minimised (P01) and something is moving. It stops the moment nothing is. No allocation per frame.
// ponytail: not ported (docs/avalonia-parity.md win-launcher): cursor parallax, Ken-Burns art drift, sparkle trail,
// perimeter comets, card/wordmark sheens, wordmark drift, running-dot breath, title tint, embers layer.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using Serilog;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class LauncherWindow
    {
        // Backdrop.cs:34-40
        private const double SpiralTurns = 7, SpiralInnerRadius = 14, SpiralTurnSeconds = 90, SpiralSmallTurnSeconds = 140;
        private const double SpiralStep = 0.1;                       // SpiralFps = 10
        // Choreo.cs:30-49
        private const int ExitBeatMs = 450, PlayBurstMain = 250, PlayBurstEcho = 120, WelcomeBurstCount = 110;
        private const double EchoDelay = 0.12, WelcomeEcho = 0.2, ShockwaveSeconds = 0.48, ShockwaveFrom = 40, ShockwaveTo = 700;
        private const double DimOthersTo = 0.55, DimSeconds = 0.3, ShakeSeconds = 0.32, ShakeAmp = 5;
        private const double TilePopScale = 1.06, CardLiftScale = 1.03, PopSeconds = 0.38;
        private const double GlowRestOpacity = 0.28, GlowHoverOpacity = 0.45, GlowBleedSeconds = 0.42;
        private const int OpenBurstCount = 70;                       // Fx.cs:36
        // Tiles.cs:43-48, MotionFx.HoverLiftScale
        private const double TileTiltDegrees = 1.2, TileTiltSeconds = 0.09, TileHoverSeconds = 0.16;
        private const double TileGlowRest = 0.35, TileGlowHover = 0.7, HoverLiftScale = 1.02, HoverLiftSeconds = 0.15;
        // Edge.cs:31-34
        private const double EdgeGlowRest = 0.3, EdgeGlowFlare = 0.85, EdgeIdleGlint = 14, GlintSeconds = 1.5;
        private static readonly TimeSpan FocusCueGap = TimeSpan.FromSeconds(4);
        private const double FadeInSeconds = 0.26, FadeGuardSeconds = 0.15;   // Crossfade.cs:24-25

        // Choreo.cs:254-255: seven swings, five pixels down to none.
        private static readonly double[] ShakeX = { 0, 1, -0.8, 0.6, -0.4, 0.2, -0.1, 0 };
        private static readonly double[] ShakeY = { 0, -0.6, 0.5, -0.35, 0.2, -0.1, 0, 0 };

        private enum Ease { QuadOut, QuadIn, Linear }

        /// <summary>One eased value: WPF's DoubleAnimation(To, Duration) with its easing (quadratic out by default).</summary>
        private struct Tween
        {
            public double From, To, T, Dur;
            public bool On;
            public Ease Mode;
            public void Go(double from, double to, double seconds, Ease mode = Ease.QuadOut)
            { From = from; To = to; T = 0; Dur = seconds; On = seconds > 0; Mode = mode; }
            public double Step(double dt)
            {
                if (!On) return To;
                T += dt;
                if (T >= Dur) { On = false; return To; }
                double u = T / Dur;
                return From + (To - From) * (Mode == Ease.Linear ? u : Mode == Ease.QuadIn ? u * u : 1 - (1 - u) * (1 - u));
            }
        }

        /// <summary>A tile (or the panel card): hover lift, tilt, glow, the Play pop and the dim.</summary>
        private sealed class TileFx
        {
            public Border Tile = null!;
            public ScaleTransform Scale = new(1, 1);
            public RotateTransform Tilt = new();
            public DropShadowEffect? Glow;
            public Color Hue;
            public Tween Lift, Lean, Shine, Dim;
            public double PopT = -1, PopPeak;
            public bool Busy => Lift.On || Lean.On || Shine.On || Dim.On || PopT >= 0;
        }

        private readonly DispatcherTimer _fxClock = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
        private readonly DispatcherTimer _fadeGuard = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly List<TileFx> _tileFx = new();
        private bool _fxActive = true;
        private TileFx _cardFx = null!;
        private readonly Ellipse[] _rings = new Ellipse[2];
        internal readonly RotateTransform SpiralTurn = new(), SpiralSmallTurn = new(), EdgeGlintSpin = new();
        internal readonly TranslateTransform ShakeSlide = new();
        private long _fxLast;
        private bool _fxFirstShow = true, _beatArmed, _spiralBuilt;
        private double _spiral, _spiralSmall, _spiralApplyIn;
        private double _edgeFlareT = -1, _edgeBreathT, _glintT = -1, _glintIdleIn = EdgeIdleGlint;
        private DateTime _lastEdgeCue = DateTime.MinValue;
        private Tween _glow, _fade;
        private IBrush? _glowRest;
        private double _shakeT = -1, _shakeA, _ringT = -1;
        private double _echoIn = -1, _exitIn = -1;
        private Control? _echoAnchor;
        private Color _echoColor;
        private Action? _exitThen, _fadeThen;

        internal bool FxTicking => _fxClock.IsEnabled;
        private bool FxLive => IsVisible && WindowState != WindowState.Minimized;
        private static bool PerfLow => Env.CurrentTier == ConditioningControlPanel.Models.PerformanceTier.Performance;
        private static bool TiltAllowed => Env.AllowTransitions && !PerfLow;   // Tiles.cs:52
        private bool SpiralsOn => _fxActive && Env.AllowAmbientLoops && !PerfLow;          // Backdrop.cs:183

        /// <summary>Called once from the constructor, after the first BuildTiles.</summary>
        private void HookFx()
        {
            _fxClock.Tick += (_, _) =>
            {
                long now = Stopwatch.GetTimestamp();
                double dt = Math.Min(0.1, (now - _fxLast) / (double)Stopwatch.Frequency);
                _fxLast = now;
                StepFx(dt);
            };
            SpiralLayer.RenderTransform = SpiralTurn;
            SpiralSmall.RenderTransform = SpiralSmallTurn;
            ContentRoot.RenderTransform = ShakeSlide;
            if (EdgeGlint.BorderBrush is LinearGradientBrush glint) glint.Transform = EdgeGlintSpin;
            _cardFx = new TileFx { Tile = PanelCard };
            PanelCard.RenderTransform = _cardFx.Scale;
            PanelCard.RenderTransformOrigin = RelativePoint.Center;
            for (int i = 0; i < _rings.Length; i++)
            {
                _rings[i] = new Ellipse
                {
                    Width = ShockwaveTo, Height = ShockwaveTo, StrokeThickness = i == 0 ? 3.5 : 1.5, Opacity = 0,
                    IsHitTestVisible = false, RenderTransform = new ScaleTransform(), RenderTransformOrigin = RelativePoint.Center,
                };
                ShockwaveCanvas.Children.Add(_rings[i]);
            }
            // Every button clicks (WPF LauncherSfx.Click in each handler); a tile's Play has its own cue.
            AddHandler(Button.ClickEvent, (_, e) =>
            {
                if (e.Source is Visual v && !GamesGrid.IsVisualAncestorOf(v)) LauncherSfx.Click();
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty) { if (IsVisible) FxOnShown(); else FxPark(); }
                else if (e.Property == WindowStateProperty) { if (FxLive) EnsureFxClock(); else FxPark(); }
            };
            _fadeGuard.Tick += (_, _) => FinishExit();
            // Fx.cs OnFxActivated/OnFxDeactivated: the backdrop spirals park while the launcher is not active.
            Activated += (_, _) => OnFxActivated(true);
            Deactivated += (_, _) => OnFxActivated(false);
            // A real close (app exit, panel closed; not the user's X, which OnClosing turns into the host's
            // decision): a pending exit is dropped before the hide parks the FX, never run into ShowFromTray.
            Closing += (_, e) => { if (!e.Cancel) { _exitThen = _fadeThen = null; _fadeGuard.Stop(); } };
            Closed += (_, _) => FxPark();
        }

        // ------------------------------------------------------------------ show / park

        /// <summary>WPF OnShown's FX half: FadeIn, LauncherSfx.Open, FxOnShown, EdgeOnShown.</summary>
        private void FxOnShown()
        {
            try
            {
                bool first = _fxFirstShow;
                _fxFirstShow = false;
                BuildSpirals();
                if (Env.AllowAmbientLoops)
                    Ambient.StartLayers(new AmbientFxConfig
                    {
                        Layers = AmbientFxLayers.FogDrift | AmbientFxLayers.DustField | AmbientFxLayers.AuroraWash,
                        Intensity = 0.95, FogPuffs = 4, DustDensity = 1.4,
                    });
                else Ambient.Stop();
                _glowRest ??= GlowLayer.Background;
                GlowLayer.Background = _glowRest;
                _glow.On = false; GlowLayer.Opacity = GlowRestOpacity;
                _shakeT = -1; ShakeSlide.X = ShakeSlide.Y = 0;
                _cardFx.Dim.On = false; PanelCard.Opacity = 1;
                _fadeThen = null;
                if (Env.AllowTransitions) { RootGrid.Opacity = 0; _fade.Go(0, 1, FadeInSeconds); }
                else { _fade.On = false; RootGrid.Opacity = 1; }

                LauncherSfx.Open();
                var now = DateTime.UtcNow;
                if (now - _lastEdgeCue > TimeSpan.FromSeconds(1)) LauncherSfx.Edge();
                _lastEdgeCue = now;
                if (Env.AllowTransitions) { _edgeFlareT = 0; _glintT = 0; _glintIdleIn = EdgeIdleGlint; }
                Dispatcher.UIThread.Post(() =>
                {
                    BurstAt(Wordmark, Env.GlowColor, OpenBurstCount);
                    if (!first) WelcomeBack();
                }, DispatcherPriority.Loaded);
                EnsureFxClock();
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] FxOnShown failed"); }
        }

        /// <summary>Hidden, minimised or closed: the clock stops, the composed state is put to rest and a
        /// pending exit runs at once (WPF Crossfade: RootGrid never stays under 1, the hide still happens).</summary>
        private void FxPark()
        {
            _fxClock.Stop();
            try
            {
                _edgeFlareT = _glintT = -1;
                EdgeGlow.Opacity = EdgeGlowRest;
                EdgeGlint.Opacity = 0;
                _shakeT = _ringT = _echoIn = -1;
                ShakeSlide.X = ShakeSlide.Y = 0;
                foreach (var r in _rings) r.Opacity = 0;
                foreach (var t in _tileFx) SettleTile(t);
                _fade.On = false;
                RootGrid.Opacity = 1;
                FinishExit();
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] FxPark failed"); }
        }

        private void EnsureFxClock()
        {
            if (!FxLive || _fxClock.IsEnabled) return;
            _fxLast = Stopwatch.GetTimestamp();
            _fxClock.Start();
        }

        // ------------------------------------------------------------------ the frame

        /// <summary>One frame of every live tween. Stops the clock when nothing is moving.</summary>
        internal void StepFx(double dt)
        {
            if (!FxLive) { FxPark(); return; }
            bool busy = false;
            try
            {
                if (SpiralsOn)
                {
                    busy = true;
                    _spiral = (_spiral + dt * 360 / SpiralTurnSeconds) % 360;
                    _spiralSmall = (_spiralSmall - dt * 360 / SpiralSmallTurnSeconds) % 360;
                    if ((_spiralApplyIn -= dt) <= 0)
                    {
                        _spiralApplyIn = SpiralStep;
                        SpiralTurn.Angle = _spiral;
                        SpiralSmallTurn.Angle = _spiralSmall;
                    }
                }
                if (Env.AllowTransitions) { busy = true; StepEdge(dt); }
                if (_fade.On) { busy = true; RootGrid.Opacity = _fade.Step(dt); if (!_fade.On) FinishFade(); }
                if (_glow.On) { busy = true; GlowLayer.Opacity = _glow.Step(dt); }
                if (_shakeT >= 0) { busy = true; StepShake(dt); }
                if (_ringT >= 0) { busy = true; StepRings(dt); }
                if (_echoIn >= 0 && (_echoIn -= dt) < 0) BurstAt(_echoAnchor, _echoColor, _echoAnchor == Wordmark ? WelcomeBurstCount : PlayBurstEcho);
                if (_echoIn >= 0) busy = true;
                if (_exitIn >= 0) { busy = true; if ((_exitIn -= dt) < 0) BeginFadeOut(); }
                StepTile(_cardFx, dt);
                busy |= _cardFx.Busy;
                foreach (var t in _tileFx) { StepTile(t, dt); busy |= t.Busy; }
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] fx frame failed"); }
            if (!busy) _fxClock.Stop();
        }

        private void StepEdge(double dt)
        {
            double glow;
            if (_edgeFlareT >= 0)
            {
                // Edge.cs FlareEdge: up to 0.85 by 140 ms (cubic out), back to rest by 900 ms (sine in-out).
                _edgeFlareT += dt;
                double ms = _edgeFlareT * 1000;
                glow = ms < 140 ? EdgeGlowRest + (EdgeGlowFlare - EdgeGlowRest) * (1 - Math.Pow(1 - ms / 140, 3))
                     : ms < 900 ? EdgeGlowFlare + (EdgeGlowRest - EdgeGlowFlare) * (1 - Math.Cos(Math.PI * (ms - 140) / 760)) / 2
                     : EdgeGlowRest;
                if (ms >= 900) { _edgeFlareT = -1; _edgeBreathT = 0; }
            }
            else
            {
                // StartEdgeBreath: 0.30..0.44, 3.2 s each way, sine in-out, auto-reverse.
                _edgeBreathT = (_edgeBreathT + dt) % 6.4;
                double u = _edgeBreathT / 3.2;
                if (u > 1) u = 2 - u;
                glow = EdgeGlowRest + 0.14 * (1 - Math.Cos(Math.PI * u)) / 2;
            }
            EdgeGlow.Opacity = glow;

            if ((_glintIdleIn -= dt) <= 0) { _glintIdleIn = EdgeIdleGlint; _glintT = 0; }   // OnEdgeIdle, silent
            if (_glintT >= 0)
            {
                // RunGlint: the band turns -90..270 (sine in-out) while it fades 0 -> 0.9 -> 0.9 -> 0.
                _glintT += dt;
                double u = Math.Min(1, _glintT / GlintSeconds), ms = _glintT * 1000;
                EdgeGlintSpin.Angle = -90 + 360 * (1 - Math.Cos(Math.PI * u)) / 2;
                EdgeGlint.Opacity = ms < 250 ? 0.9 * ms / 250 : ms < 1150 ? 0.9 : Math.Max(0, 0.9 * (1500 - ms) / 350);
                if (u >= 1) { _glintT = -1; EdgeGlint.Opacity = 0; }
            }
        }

        internal void OnFxActivated(bool active)
        {
            _fxActive = active;
            if (active) { EnsureFxClock(); OnEdgeActivated(); }
        }

        /// <summary>WPF OnEdgeActivated: focus coming back runs the shimmer and the glint, at most every 4 s.</summary>
        private void OnEdgeActivated()
        {
            if (!IsVisible || _fxFirstShow) return;
            var now = DateTime.UtcNow;
            if (now - _lastEdgeCue < FocusCueGap) return;
            _lastEdgeCue = now;
            LauncherSfx.Edge();
            if (Env.AllowTransitions) { _glintT = 0; EnsureFxClock(); }
        }

        // ------------------------------------------------------------------ the spirals

        private void BuildSpirals()
        {
            if (_spiralBuilt) return;
            _spiralBuilt = true;
            SpiralPath.Data = BuildSpiral(SpiralLayer.Width, SpiralTurns, SpiralInnerRadius);
            SpiralSmallPath.Data = BuildSpiral(SpiralSmall.Width, SpiralTurns - 1.5, SpiralInnerRadius);
        }

        /// <summary>Backdrop.cs BuildSpiral: an Archimedean spiral as one polyline.</summary>
        private static Geometry BuildSpiral(double size, double turns, double innerRadius)
        {
            var geometry = new StreamGeometry();
            double c = size / 2, maxTheta = turns * Math.PI * 2, growth = (c - innerRadius - 4) / maxTheta;
            const double step = Math.PI / 45;
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(c + innerRadius, c), false);
                for (double theta = step; theta <= maxTheta; theta += step)
                {
                    double r = innerRadius + growth * theta;
                    ctx.LineTo(new Point(c + r * Math.Cos(theta), c + r * Math.Sin(theta)));
                }
                ctx.EndFigure(false);
            }
            return geometry;
        }

        // ------------------------------------------------------------------ tiles: hover

        /// <summary>WPF CreateTile's hover rig (Tiles.cs:72-88, 228-244): lift, tilt toward the pointer, glow, the
        /// hover note, and the hue bleeding into the backdrop (Choreo.cs ChoreoTileHover).</summary>
        private void DecorateTileFx(Border tile, Color hue)
        {
            _tileFx.RemoveAll(t => t.Tile.Parent == null);   // BuildTiles cleared the grid
            var fx = new TileFx { Tile = tile, Hue = hue };
            tile.RenderTransformOrigin = RelativePoint.Center;
            tile.RenderTransform = new TransformGroup { Children = { fx.Scale, fx.Tilt } };
            if (Env.AllowGlow(Env.CurrentTier))
                tile.Effect = fx.Glow = new DropShadowEffect
                {
                    Color = hue, OffsetX = 0, OffsetY = 0, Opacity = TileGlowRest,
                    BlurRadius = Math.Min(20, Env.MaxGlowBlurRadius(Env.CurrentTier)),
                };
            _tileFx.Add(fx);
            tile.PointerEntered += (_, _) => TileHover(fx, true);
            tile.PointerExited += (_, _) => TileHover(fx, false);
            tile.PointerMoved += (_, e) => TiltToward(fx, e.GetPosition(tile));
        }

        internal void TileHover(Border tile, bool on)
        {
            foreach (var t in _tileFx) if (ReferenceEquals(t.Tile, tile)) TileHover(t, on);
        }

        private void TileHover(TileFx t, bool on)
        {
            try
            {
                if (on) LauncherSfx.Hover();
                bool anim = Env.AllowTransitions;
                t.Lift.Go(t.Scale.ScaleX, on ? HoverLiftScale : 1, anim ? HoverLiftSeconds : 0);
                t.Shine.Go(t.Glow?.Opacity ?? 0, on ? TileGlowHover : TileGlowRest, anim ? TileHoverSeconds : 0, Ease.Linear);   // TintTile: no easing
                if (!on) t.Lean.Go(t.Tilt.Angle, 0, anim ? TileHoverSeconds : 0);
                if (!anim) StepTile(t, 0);
                if (on) BleedGlow(t.Hue); else RestoreGlow();
                EnsureFxClock();
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] tile hover fx failed"); }
        }

        /// <summary>Tiles.cs TiltToward: up to 1.2 degrees, right side down when the pointer is right.</summary>
        private void TiltToward(TileFx t, Point at)
        {
            var b = t.Tile.Bounds;
            if (!TiltAllowed || b.Width <= 0 || b.Height <= 0) return;
            double nx = Math.Clamp(at.X / b.Width * 2 - 1, -1, 1), ny = Math.Clamp(at.Y / b.Height * 2 - 1, -1, 1);
            t.Lean.Go(t.Tilt.Angle, nx * -ny * TileTiltDegrees, TileTiltSeconds, Ease.Linear);   // TiltToward: no easing
            EnsureFxClock();
        }

        private static void StepTile(TileFx t, double dt)
        {
            if (t.Lift.On || dt == 0) t.Scale.ScaleX = t.Scale.ScaleY = t.Lift.Step(dt);
            if (t.Lean.On || dt == 0) t.Tilt.Angle = t.Lean.Step(dt);
            if (t.Glow != null && (t.Shine.On || dt == 0)) t.Glow.Opacity = t.Shine.Step(dt);
            if (t.Dim.On) t.Tile.Opacity = t.Dim.Step(dt);
            if (t.PopT >= 0)
            {
                // Choreo.cs Pop: to the peak by 35 % (quad out), back to 1 by 100 % (quad in-out).
                t.PopT += dt;
                double u = Math.Min(1, t.PopT / PopSeconds), s;
                if (u < 0.35) { double v = u / 0.35; s = 1 + (t.PopPeak - 1) * (1 - (1 - v) * (1 - v)); }
                else { double v = (u - 0.35) / 0.65; v = v < 0.5 ? 2 * v * v : 1 - 2 * (1 - v) * (1 - v); s = t.PopPeak + (1 - t.PopPeak) * v; }
                t.Scale.ScaleX = t.Scale.ScaleY = s * (t.Lift.On ? 1 : t.Lift.To);
                if (u >= 1) t.PopT = -1;
            }
        }

        private static void SettleTile(TileFx t)
        {
            t.Lift.Go(1, 1, 0); t.Lean.Go(0, 0, 0); t.Shine.Go(TileGlowRest, TileGlowRest, 0);
            t.PopT = -1;
            StepTile(t, 0);
        }

        /// <summary>Choreo.cs BleedGlow: the hovered tile's hue takes over the backdrop glow, a little brighter.</summary>
        private void BleedGlow(Color hue)
        {
            _glowRest ??= GlowLayer.Background;
            GlowLayer.Background = new RadialGradientBrush
            {
                GradientOrigin = new RelativePoint(0.72, 0.18, RelativeUnit.Relative),
                Center = new RelativePoint(0.72, 0.18, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.55, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
                GradientStops = { new GradientStop(hue, 0), new GradientStop(Color.FromArgb(0, hue.R, hue.G, hue.B), 1) },
            };
            AnimateGlow(GlowHoverOpacity);
        }

        private void RestoreGlow()
        {
            if (_glowRest != null) GlowLayer.Background = _glowRest;
            AnimateGlow(GlowRestOpacity);
        }

        private void AnimateGlow(double to)
        {
            _glow.Go(GlowLayer.Opacity, to, Env.AllowTransitions ? GlowBleedSeconds : 0);
            if (!_glow.On) GlowLayer.Opacity = to;
            EnsureFxClock();
        }

        // ------------------------------------------------------------------ the exit beats

        /// <summary>Choreo.cs ChoreoPlay. A locked or signed-out tile only flinches (Denied); otherwise the
        /// full beat, and the hide waits <see cref="ExitBeatMs"/> for it.</summary>
        private void FxPlayBeat(string cardId, bool refused)
        {
            try
            {
                if (!FxLive) return;   // boot straight into a game / a handoff: silent, like WPF LaunchGame
                if (refused) { LauncherSfx.Denied(); Shake(0.45); return; }
                LauncherSfx.Click();
                TileFx? t = null;
                foreach (var x in _tileFx) if (Equals(x.Tile.Tag, cardId)) t = x;
                if (t == null) return;
                ExitBeat(t, t.Hue, TilePopScale);
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] play beat failed"); }
        }

        /// <summary>Choreo.cs ChoreoPanelLaunch: the same beat on the panel card in the particle colour.</summary>
        private void FxPanelLaunchBeat()
        {
            try { ExitBeat(_cardFx, Env.ParticleColor, CardLiftScale, PanelCta); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] CTA beat failed"); }
        }

        private void ExitBeat(TileFx pop, Color tint, double peak, Control? anchor = null)
        {
            anchor ??= pop.Tile;
            if (!FxLive) return;
            LauncherSfx.Launch();
            _beatArmed = Env.AllowTransitions;
            if (Env.AllowTransitions) { pop.PopT = 0; pop.PopPeak = peak; }
            Shockwave(anchor, tint);
            BurstAt(anchor, tint, PlayBurstMain);
            _echoAnchor = anchor; _echoColor = tint; _echoIn = EchoDelay;
            foreach (var t in _tileFx) if (!ReferenceEquals(t, pop)) Dim(t);
            if (!ReferenceEquals(_cardFx, pop)) Dim(_cardFx);
            Shake(1.0);
            EnsureFxClock();
        }

        private static void Dim(TileFx t)
        {
            if (!Env.AllowTransitions) { t.Tile.Opacity = DimOthersTo; return; }
            t.Dim.Go(t.Tile.Opacity, DimOthersTo, DimSeconds);
        }

        private void Shake(double strength)
        {
            if (!Env.AllowTransitions) return;
            _shakeA = ShakeAmp * strength;
            _shakeT = 0;
            EnsureFxClock();
        }

        private void StepShake(double dt)
        {
            _shakeT += dt;
            double u = Math.Min(1, _shakeT / ShakeSeconds) * (ShakeX.Length - 1);
            int i = Math.Min((int)u, ShakeX.Length - 2);
            double f = u - i;
            ShakeSlide.X = _shakeA * (ShakeX[i] + (ShakeX[i + 1] - ShakeX[i]) * f);
            ShakeSlide.Y = _shakeA * (ShakeY[i] + (ShakeY[i + 1] - ShakeY[i]) * f);
            if (_shakeT >= ShakeSeconds) { _shakeT = -1; ShakeSlide.X = ShakeSlide.Y = 0; }
        }

        /// <summary>Choreo.cs Shockwave: a bright ring and a thin echo 90 ms behind, 40 -> 700 px over 480 ms.</summary>
        private void Shockwave(Control anchor, Color color)
        {
            if (!Env.AllowTransitions || !anchor.IsEffectivelyVisible || anchor.Bounds.Width <= 0) return;
            if (anchor.TranslatePoint(new Point(anchor.Bounds.Width / 2, anchor.Bounds.Height / 2), ShockwaveCanvas) is not { } c) return;
            foreach (var r in _rings)
            {
                r.Stroke = new SolidColorBrush(color);
                Canvas.SetLeft(r, c.X - ShockwaveTo / 2);
                Canvas.SetTop(r, c.Y - ShockwaveTo / 2);
            }
            _ringT = 0;
            StepRings(0);
        }

        private void StepRings(double dt)
        {
            _ringT += dt;
            double start = ShockwaveFrom / ShockwaveTo;
            for (int i = 0; i < _rings.Length; i++)
            {
                double u = (_ringT - i * 0.09) / ShockwaveSeconds, alpha = i == 0 ? 0.95 : 0.5;
                var r = _rings[i];
                if (u < 0 || u >= 1) { r.Opacity = 0; continue; }
                double s = start + (1 - start) * (1 - (1 - u) * (1 - u));
                ((ScaleTransform)r.RenderTransform!).ScaleX = s;
                ((ScaleTransform)r.RenderTransform!).ScaleY = s;
                r.Opacity = u < 0.35 ? alpha * (1 - 0.2 * u / 0.35) : alpha * 0.8 * (1 - (u - 0.35) / 0.65);
            }
            if (_ringT >= 0.09 + ShockwaveSeconds) { _ringT = -1; foreach (var r in _rings) r.Opacity = 0; }
        }

        /// <summary>Fx.cs BurstAt: a burst at the centre of <paramref name="anchor"/>, inside the burst layer.</summary>
        private void BurstAt(Control? anchor, Color color, int count)
        {
            try
            {
                if (anchor == null || !Env.AllowParticles || !anchor.IsEffectivelyVisible || anchor.Bounds.Width <= 0) return;
                if (anchor.TranslatePoint(new Point(anchor.Bounds.Width / 2, anchor.Bounds.Height / 2), BurstLayer) is not { } p) return;
                if (p.X < 0 || p.Y < 0 || p.X > BurstLayer.Bounds.Width || p.Y > BurstLayer.Bounds.Height) return;
                BurstLayer.Burst(p.X, p.Y, color, count);
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] burst failed"); }
        }

        /// <summary>Choreo.cs WelcomeBack: the return cue, bursts from the Launch button and the wordmark.</summary>
        private void WelcomeBack()
        {
            LauncherSfx.Return();
            BurstAt(PanelCta, Env.ParticleColor, WelcomeBurstCount);
            _echoAnchor = Wordmark; _echoColor = Env.GlowColor; _echoIn = WelcomeEcho;
            EnsureFxClock();
        }

        // ------------------------------------------------------------------ crossfade + the held hide

        /// <summary>LauncherHost.OpenPanel/FadeThenHide: an armed beat holds the step FadeLeadMs, then the
        /// launcher fades out over FadeOutMs and <paramref name="then"/> hides it. Off screen or motion off: now.</summary>
        private void AfterExitBeat(Action then)
        {
            if (_exitThen != null || _fadeThen != null) { _exitThen += then; return; }
            bool armed = _beatArmed;
            _beatArmed = false;
            if (!FxLive || !Env.AllowTransitions) { then(); return; }
            _exitThen = then;
            _exitIn = armed ? LauncherRules.FadeLeadMs(ExitBeatMs) / 1000.0 : 0;
            // Crossfade.cs:71 guard: the hide happens even if the frame clock never gets there.
            _fadeGuard.Interval = TimeSpan.FromSeconds(_exitIn + LauncherRules.FadeOutMs / 1000.0 + FadeGuardSeconds);
            _fadeGuard.Start();
            if (_exitIn <= 0) BeginFadeOut();
            EnsureFxClock();
        }

        private void BeginFadeOut()
        {
            _exitIn = -1;
            _fadeThen = _exitThen;
            _exitThen = null;
            _fade.Go(RootGrid.Opacity, 0, LauncherRules.FadeOutMs / 1000.0, Ease.QuadIn);   // Crossfade.cs RunFade: ease-in out
        }

        private void FinishFade()
        {
            _fadeGuard.Stop();
            var then = _fadeThen;
            _fadeThen = null;
            if (then == null) return;
            try { then(); }
            catch (Exception ex) { Log.Error(ex, "[Launcher] step after fade failed"); }
            finally { RootGrid.Opacity = 1; }
        }

        /// <summary>Parked mid-exit: the hide still happens, at once.</summary>
        internal void FinishExit()
        {
            _fadeGuard.Stop();
            _fade.On = false;
            RootGrid.Opacity = 1;
            var then = _exitThen + _fadeThen;
            _exitThen = _fadeThen = null;
            _exitIn = -1;
            if (then == null) return;
            try { then(); }
            catch (Exception ex) { Log.Error(ex, "[Launcher] step after fade failed"); }
        }

        // ------------------------------------------------------------------ the cues

        /// <summary>WPF LauncherSfx: one-shots through the head's CoreAudio, silent when the launcher's speaker is
        /// off, the master volume is 0 or the file is missing. ponytail: CoreAudio returns no handle, so WPF's
        /// three-voice hover cap and the IsOutputSuppressed probe have nothing to call here.</summary>
        internal static class LauncherSfx
        {
            /// <summary>The hover throttle's clock (P08: tests step it).</summary>
            internal static TimeProvider Clock = TimeProvider.System;
            private static readonly LauncherMelody Melody = new();
            private static DateTime _lastHover = DateTime.MinValue;

            public static void Open() { Melody.Reset(); Play("chaos/reveal_chime.mp3", 0.18f, "launcher-open"); }
            public static void Click() => Play("chaos/ui_click.mp3", 0.16f, "launcher-click");
            public static void Denied() => Play("chaos/ui_denied.mp3", 0.16f, "launcher-denied");
            public static void Launch() { Melody.Reset(); Play("chaos/reveal_chime.mp3", 0.24f, "launcher-launch"); }
            public static void Edge() => Play("chaos/ripple_cast.mp3", 0.12f, "launcher-edge");
            public static void Return() { Melody.Reset(); Play("chaos/dling.mp3", 0.14f, "launcher-return"); }

            /// <summary>The next note of the phrase LauncherMelody composes as the pointer moves; throttled.</summary>
            public static void Hover()
            {
                var now = Clock.GetUtcNow().UtcDateTime;
                var gap = (now - _lastHover).TotalMilliseconds;
                if (gap >= 0 && gap < LauncherMelody.MinGapMs) return;   // a clock stepped back never mutes
                _lastHover = now;
                var cue = Melody.Next(now);
                Play($"launcher/wood_{cue.Rung:00}.wav", (float)Math.Clamp(cue.Level, 0d, 1d) * 0.05f, "launcher-hover", "launcher/hover.wav");
            }

            private static void Play(string rel, float scale, string tag, string? fallback = null)
            {
                try
                {
                    var s = CoreSettings.Current;
                    if (!s.LauncherSoundEnabled || s.MasterVolume <= 0) return;
                    var path = Resolve(rel) ?? (fallback == null ? null : Resolve(fallback));
                    if (path == null) return;
                    CoreAudio.PlayOneShot(path, Math.Clamp(s.MasterVolume / 100f * scale, 0f, 1f), tag);
                }
                catch (Exception ex) { Log.Debug(ex, "[Launcher] sfx {Tag} failed", tag); }
            }

            private static string? Resolve(string rel)
            {
                var path = CoreModArt.AudioOverridePath(rel)
                    ?? ContentLocator.Resolve(System.IO.Path.Combine("Resources", "sounds", rel.Replace('/', System.IO.Path.DirectorySeparatorChar)));
                return string.IsNullOrEmpty(path) || !System.IO.File.Exists(path) ? null : path;
            }
        }
    }
}
