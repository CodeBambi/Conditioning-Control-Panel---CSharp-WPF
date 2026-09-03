using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;
using ModPackage = ConditioningControlPanel.Models.ModPackage;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Controls/TakeoverOrb.cs.
    ///
    /// The Takeover tab's identity: a real Skia orb in place of the two stacked ellipses that
    /// stood in for one. Layered glow, a slowly rotating inner wisp texture, and a ring of orbiting
    /// particle wisps - the most present ambient FX in the app, and still one that parks the instant
    /// nobody is looking at it.
    ///
    /// It deliberately copies the ambient canvas's lifecycle wholesale rather than inventing a
    /// second one:
    ///   • a self-stopping <see cref="DispatcherTimer"/> that only runs while the control is loaded,
    ///     visible, and its window is active and not minimized;
    ///   • colours and the performance budget read ONCE at (re)start and on a mod switch, never
    ///     per tick;
    ///   • nothing allocated per frame - three persistent <see cref="SKImage"/> sprites baked once
    ///     for the whole app, two <see cref="SKPaint"/>s, and colour filters rebuilt only when the
    ///     palette moves;
    ///   • every tick and every paint wrapped, with repeated faults stopping the clock rather than
    ///     spamming the log;
    ///   • a mini perf governor that sheds the wisps, then the swirl, under sustained hitching.
    ///
    /// Unlike the ambient canvas it always PAINTS, even when the clock is off: an orb that vanishes
    /// under reduced motion or the Performance tier would leave a hole in the tab's hero row. Off
    /// means "a still orb", not "no orb". No window, popup or layered surface is created anywhere.
    ///
    /// PORT NOTES
    /// • WPF hosted an <c>SKElement</c> as the <see cref="Decorator"/>'s child and painted its
    ///   surface. Avalonia has no SKElement; the equivalent is an <see cref="ICustomDrawOperation"/>
    ///   that leases the render pass's own <see cref="SKCanvas"/>, so every draw and bake routine
    ///   below is the WPF one unchanged. The lease canvas is the WINDOW's, so it is never cleared.
    /// • The base is still <see cref="Decorator"/>, so a host may put content over the orb - but a
    ///   Decorator with no child MEASURES TO ZERO. Give it an explicit Width/Height, or put it in a
    ///   Grid cell where Stretch gives it the slot. In a StackPanel it would draw nothing.
    /// </summary>
    public class TakeoverOrb : Decorator
    {
        private const int FaultLimit = 5;

        /// <summary>Hard ceiling on orbiting wisps. The ring reads as a ring well before this.</summary>
        private const int MaxWisps = 16;

        // ---- geometry, all as a fraction of the shorter edge ----
        private const float CoreRadius = 0.235f;
        private const float RingRadiusMin = 0.30f;
        private const float RingRadiusMax = 0.455f;

        private readonly DispatcherTimer _timer;

        // ---- cached at (re)start: never read per tick ----
        private int _wispBudget;
        private int _targetFps = 30;
        private SKColor _glow, _particle, _mist;
        private SKColorFilter? _glowTint, _particleTint, _mistTint, _coreTint;

        // ---- clocks ----
        private readonly System.Diagnostics.Stopwatch _clock = new();
        private double _lastTickMs;
        private float _breathT, _swirlT, _wispT;

        /// <summary>Live speech energy (0-1), decayed every tick. Drives the "she is hearing you"
        /// brightening while a voice prompt is running; zero the rest of the time.</summary>
        private float _energy;

        // ---- perf governor (shape copied from AmbientFxCanvas.Governor) ----
        private double _hitchScore;
        private int _liveWisps;
        private bool _coreOnly;

        // ---- sim ----
        private struct Wisp { public float Ang, AngSpd, Rad, Bob, BobSpd, Size, Alpha; }
        private Wisp[] _wisps = Array.Empty<Wisp>();
        private int _wispN;

        private bool _active;
        private bool _paused;
        private bool _modHooked;
        private int _faults;
        private Window? _window;
        private readonly Random _rng = new();

        // Shared, baked once for the whole app.
        private static SKImage? _dot;
        private static SKImage? _ball;
        private static SKImage? _swirl;
        private static readonly object _spriteLock = new();

        private static readonly SKSamplingOptions Sampling =
            new(SKFilterMode.Linear, SKMipmapMode.Linear);   // SkiaSharp 3 dropped SKPaint.FilterQuality

        private readonly SKPaint _add = new()
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
        };

        private readonly SKPaint _over = new()
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.SrcOver,
        };

        public TakeoverOrb()
        {
            IsHitTestVisible = false;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => Tick();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            ReadEnvironment();
            Reseed();
        }

        /// <summary>True while she has the reins - the orb burns brighter and turns faster.</summary>
        public bool IsActive => _active;

        /// <summary>True while the clock is actually ticking.</summary>
        public bool IsRunning => _timer.IsEnabled;

        // ================================ public API ================================

        /// <summary>Dormant or active. Safe to call repeatedly; repaints even when parked.</summary>
        public void SetActive(bool active)
        {
            if (_active == active) { Evaluate(); return; }
            _active = active;
            if (!active) _energy = 0f;
            Evaluate();
            Repaint();
        }

        /// <summary>
        /// Feeds the orb the live speech level (raw RMS, ~0-0.2) while a voice prompt is running.
        /// Only ever RAISES the stored energy - the tick decays it - so a burst of speech blooms and
        /// then settles instead of strobing with every level callback.
        /// </summary>
        public void SetEnergy(double level)
        {
            try
            {
                float e = (float)Math.Clamp(level / 0.2, 0.0, 1.0);
                if (e > _energy) _energy = e;
            }
            catch { }
        }

        /// <summary>Park the clock and keep the composed state (tab switch, window deactivate).</summary>
        public void Pause()
        {
            _paused = true;
            StopClock();
        }

        /// <summary>Un-park. No-op if the environment still says no.</summary>
        public void Resume()
        {
            _paused = false;
            Evaluate();
        }

        /// <summary>Drops the clock and the sim buffer. The orb still paints, statically.</summary>
        public void Stop()
        {
            _paused = false;
            _energy = 0f;
            StopClock();
            _wispN = 0;
            Repaint();
        }

        /// <summary>Re-reads the palette and the performance budget and reseeds the ring.</summary>
        public void Restart()
        {
            _faults = 0;
            ReadEnvironment();
            Reseed();
            Evaluate();
            Repaint();
        }

        // ============================== environment ==============================

        private void ReadEnvironment()
        {
            try
            {
                // CurrentTier -> FxTargetFps / MaxAmbientParticles, exactly as the WPF original.
                // At the Performance tier FxTargetFps is 0, which ShouldRun reads as "never start
                // the clock" - the orb still PAINTS, it just stops moving.
                var tier = AmbientFxCanvas.Env.CurrentTier;
                _targetFps = AmbientFxCanvas.Env.FxTargetFps(tier);
                if (_targetFps > 0)
                    _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / _targetFps);

                // A third of the shared ambient budget: this is one small element, not a field.
                _wispBudget = Math.Clamp(AmbientFxCanvas.Env.MaxAmbientParticles(tier) / 4, 0, MaxWisps);

                // The palette half of FxTheme is not a service - it reads the app resource
                // dictionary, and Theme/Colors.xaml is already ported - so this is the real thing.
                _glow = ThemeColor("FxGlowColor", new SKColor(0xFF, 0x69, 0xB4));
                _particle = ThemeColor("FxParticleColor", new SKColor(0xFF, 0x69, 0xB4));
                _mist = ThemeColor("FxMistColor", new SKColor(0xE8, 0x43, 0x93));

                _glowTint?.Dispose(); _glowTint = SKColorFilter.CreateBlendMode(_glow, SKBlendMode.Modulate);
                _particleTint?.Dispose(); _particleTint = SKColorFilter.CreateBlendMode(_particle, SKBlendMode.Modulate);
                _mistTint?.Dispose(); _mistTint = SKColorFilter.CreateBlendMode(_mist, SKBlendMode.Modulate);

                // The body is the mod's own glow colour dragged most of the way to black, so the
                // orb reads as a dark sphere lit from within rather than as a flat pink disc - and
                // it re-tints with the mod like everything else. No literal colour anywhere.
                _coreTint?.Dispose();
                _coreTint = SKColorFilter.CreateBlendMode(Darken(_glow, 0.30f), SKBlendMode.Modulate);

                _liveWisps = _wispBudget;
                _coreOnly = false;
                _hitchScore = 0;
            }
            catch (Exception ex)
            {
                Log("TakeoverOrb.ReadEnvironment: " + ex.Message);
            }
        }

        private void Reseed()
        {
            int n = Math.Clamp(_wispBudget, 0, MaxWisps);
            _wispN = 0;
            _wisps = n > 0 ? new Wisp[n] : Array.Empty<Wisp>();
            for (int i = 0; i < n; i++)
            {
                float t = (i + 0.5f) / n;
                _wisps[_wispN++] = new Wisp
                {
                    Ang = t * 6.283f + (float)_rng.NextDouble() * 0.9f,
                    // 24-60s for a full orbit: the ambient clock, never the 2s uncanny valley.
                    AngSpd = (0.105f + 0.155f * (float)_rng.NextDouble()) * (i % 3 == 0 ? -1f : 1f),
                    Rad = RingRadiusMin + (RingRadiusMax - RingRadiusMin) * (float)_rng.NextDouble(),
                    Bob = (float)_rng.NextDouble() * 6.283f,
                    BobSpd = 0.32f + 0.30f * (float)_rng.NextDouble(),
                    Size = 0.016f + 0.020f * (float)_rng.NextDouble(),
                    Alpha = 0.45f + 0.45f * (float)_rng.NextDouble(),
                };
            }
            _breathT = _swirlT = _wispT = 0f;
        }

        // ============================== lifecycle ==============================

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                HookWindow(TopLevel.GetTopLevel(this) as Window);
                if (!_modHooked) { CoreMods.ModChanged += OnModChanged; _modHooked = true; }
                Evaluate();
            }
            catch (Exception ex) { Log("TakeoverOrb.OnLoaded: " + ex.Message); }
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                StopClock();
                UnhookWindow();
                if (_modHooked) { CoreMods.ModChanged -= OnModChanged; _modHooked = false; }
            }
            catch (Exception ex) { Log("TakeoverOrb.OnUnloaded: " + ex.Message); }
        }

        /// <summary>
        /// A mod switch re-reads the palette and the budget and repaints - it never reseeds, so the
        /// ring keeps its orbits and only its colour moves, which is what the WPF twin does. The
        /// head may raise this off the UI thread, so it is marshalled before the colour filters are
        /// rebuilt.
        /// </summary>
        private void OnModChanged(object? sender, ModPackage mod)
        {
            void Apply()
            {
                try { ReadEnvironment(); Repaint(); }
                catch (Exception ex) { Log("TakeoverOrb.OnModChanged: " + ex.Message); }
            }
            if (Dispatcher.UIThread.CheckAccess()) Apply();
            else Dispatcher.UIThread.Post(Apply);
        }

        /// <summary>
        /// WPF's IsVisibleChanged. Avalonia routes property changes through here instead.
        ///
        /// The gate itself (ShouldRun) reads IsEffectivelyVisible, which is the real twin of WPF's
        /// UIElement.IsVisible: the plain IsVisible is only this control's own flag and stays true
        /// under a collapsed ancestor. Avalonia's IsEffectivelyVisibleChanged event is internal, so
        /// an ancestor collapsing raises nothing here - but Tick() re-checks ShouldRun first, so the
        /// clock still stops itself within one frame, and Loaded fires when a TabControl reattaches
        /// the content. ponytail: if a host ever hides an ancestor WITHOUT detaching, call Pause().
        /// </summary>
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty) Evaluate();
        }

        private void HookWindow(Window? window)
        {
            if (ReferenceEquals(_window, window)) return;
            UnhookWindow();
            _window = window;
            if (_window == null) return;
            _window.Activated += OnWindowStateish;
            _window.Deactivated += OnWindowStateish;
            // Avalonia has no Window.StateChanged; WindowState is a styled property, so watch it.
            _window.PropertyChanged += OnWindowPropertyChanged;
        }

        private void UnhookWindow()
        {
            if (_window == null) return;
            _window.Activated -= OnWindowStateish;
            _window.Deactivated -= OnWindowStateish;
            _window.PropertyChanged -= OnWindowPropertyChanged;
            _window = null;
        }

        private void OnWindowStateish(object? sender, EventArgs e) => Evaluate();

        private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.WindowStateProperty) Evaluate();
        }

        /// <summary>The single gate: start the clock only when everything says it may run.</summary>
        private void Evaluate()
        {
            try
            {
                if (ShouldRun()) StartClock();
                else StopClock();
            }
            catch (Exception ex) { Log("TakeoverOrb.Evaluate: " + ex.Message); }
        }

        private bool ShouldRun()
        {
            if (_paused || _faults >= FaultLimit) return false;
            if (!IsLoaded || !IsEffectivelyVisible) return false;
            if (_targetFps <= 0) return false;
            // The reduced-motion / Performance-tier kill switch. It stops the CLOCK only: Render
            // still paints, so "off" is a still orb rather than a hole in the hero row.
            if (!AmbientFxCanvas.Env.AllowAmbientLoops) return false;
            var w = _window;
            if (w != null)
            {
                if (w.WindowState == WindowState.Minimized) return false;
                if (!w.IsActive) return false;
            }
            return true;
        }

        private void StartClock()
        {
            if (_timer.IsEnabled) return;
            if (!_clock.IsRunning) _clock.Start();
            _lastTickMs = _clock.Elapsed.TotalMilliseconds;
            _timer.Start();
        }

        private void StopClock()
        {
            if (_timer.IsEnabled) _timer.Stop();
        }

        private void Repaint()
        {
            try { InvalidateVisual(); } catch { }
        }

        // ================================ tick ================================

        private void Tick()
        {
            try
            {
                if (!ShouldRun()) { StopClock(); Repaint(); return; }

                double nowMs = _clock.Elapsed.TotalMilliseconds;
                double gapMs = nowMs - _lastTickMs;
                _lastTickMs = nowMs;
                float dt = (float)Math.Clamp(gapMs / 1000.0, 0.001, 0.100);

                Governor(gapMs);

                float speed = _active ? 1.0f : 0.45f;
                _breathT += dt;
                _swirlT += dt * speed;
                _wispT += dt * speed;

                // Energy decays over ~0.6s, so the bloom trails the voice rather than tracking it.
                if (_energy > 0f) _energy = Math.Max(0f, _energy - dt * 1.6f);

                InvalidateVisual();
            }
            catch (Exception ex)
            {
                _faults++;
                Log($"TakeoverOrb tick failed ({_faults}/{FaultLimit}): {ex.Message}");
                if (_faults >= FaultLimit)
                {
                    Log("TakeoverOrb: stopping after repeated faults");
                    StopClock();
                }
            }
        }

        /// <summary>
        /// Sustained frame gaps first shed the orbiting wisps and then the swirl, leaving the glow
        /// and the body - which is still recognisably the orb. Score decays ~7% a frame, so a clean
        /// run recovers over about 5s and one stray hitch changes nothing.
        /// </summary>
        private void Governor(double gapMs)
        {
            double budgetMs = 1000.0 / Math.Max(1, _targetFps);
            _hitchScore = Math.Max(0, _hitchScore * 0.93);
            if (gapMs > budgetMs * 2.5) _hitchScore += gapMs > budgetMs * 6 ? 3.0 : 1.0;

            bool coreOnly = _hitchScore >= 12;
            int budget = coreOnly ? 0 : _hitchScore >= 5 ? _wispBudget / 2 : _wispBudget;

            if (coreOnly != _coreOnly)
            {
                _coreOnly = coreOnly;
                if (coreOnly) Log($"[FXPERF] takeover orb dropped to core-only (hitch score {_hitchScore:F1})");
                else Log("[FXPERF] takeover orb recovered");
            }
            if (budget < _liveWisps) _liveWisps = budget;
            else if (budget > _liveWisps) _liveWisps = Math.Min(budget, _liveWisps + 1);
        }

        // ================================ paint ================================

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var r = new Rect(Bounds.Size);
            if (r.Width <= 0 || r.Height <= 0) return;
            context.Custom(new OrbDrawOp(this, r));
        }

        /// <summary>
        /// The Avalonia stand-in for WPF's SKElement.PaintSurface. It holds the control, not a copy
        /// of its state.
        /// ponytail: the render thread reads the sim floats the UI thread writes. Torn reads are not
        /// a thing for 32-bit floats and one frame of a stale angle is invisible; the only pointer
        /// that moves is _wisps, and DrawWisps clamps to the array it actually loaded. Snapshot the
        /// whole sim into the op if a future field is wider than a float.
        /// </summary>
        private sealed class OrbDrawOp : ICustomDrawOperation
        {
            private readonly TakeoverOrb _orb;
            private readonly Rect _bounds;

            public OrbDrawOp(TakeoverOrb orb, Rect bounds) { _orb = orb; _bounds = bounds; }

            public Rect Bounds => _bounds;
            public bool HitTest(Point p) => false;
            // false, always: returning true lets the scene graph reuse the previous frame's op and
            // the orb would freeze the moment it stopped moving in layout terms.
            public bool Equals(ICustomDrawOperation? other) => false;
            public void Dispose() { }

            public void Render(ImmediateDrawingContext context)
            {
                var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (lease is null) return;
                using var leased = lease.Lease();
                _orb.Paint(leased.SkCanvas, (float)_bounds.Width, (float)_bounds.Height);
            }
        }

        /// <summary>
        /// The WPF OnPaintSurface body. The one change: no Clear - the leased canvas is the whole
        /// window's, and clearing it would wipe everything drawn underneath.
        /// </summary>
        private void Paint(SKCanvas canvas, float w, float h)
        {
            if (w <= 0 || h <= 0) return;
            canvas.Save();
            try
            {
                float min = Math.Min(w, h);
                float cx = w * 0.5f, cy = h * 0.5f;

                // Dormant reads as banked embers; active as lit. Energy blooms on top of either.
                float lit = _active ? 1.0f : 0.45f;
                float breath = 0.62f + 0.38f * (float)((Math.Sin(_breathT * (_active ? 0.86 : 0.52)) + 1) * 0.5);
                float bloom = 1f + _energy * 0.85f;

                DrawHalo(canvas, cx, cy, min, lit * breath * bloom);
                DrawBody(canvas, cx, cy, min, lit);
                if (!_coreOnly) DrawSwirl(canvas, cx, cy, min, lit * bloom);
                DrawRim(canvas, cx, cy, min, lit * breath * bloom);
                if (!_coreOnly) DrawWisps(canvas, cx, cy, min, lit * bloom);
            }
            catch (Exception ex)
            {
                _faults++;
                Log("TakeoverOrb.Paint: " + ex.Message);
            }
            finally { canvas.Restore(); }
        }

        /// <summary>Two stacked radial glows: a wide mist bloom and a tighter core halo.</summary>
        private void DrawHalo(SKCanvas canvas, float cx, float cy, float min, float k)
        {
            _add.ColorFilter = _mistTint;
            _add.Color = SKColors.White.WithAlpha(Alpha(0.16f * k));
            DrawSprite(canvas, _add, Dot, cx, cy, min * 1.02f);

            _add.ColorFilter = _glowTint;
            _add.Color = SKColors.White.WithAlpha(Alpha(0.30f * k));
            DrawSprite(canvas, _add, Dot, cx, cy, min * 0.70f);
            _add.ColorFilter = null;
        }

        /// <summary>The sphere itself - the mod's glow colour dragged towards black.</summary>
        private void DrawBody(SKCanvas canvas, float cx, float cy, float min, float k)
        {
            _over.ColorFilter = _coreTint;
            _over.Color = SKColors.White.WithAlpha(Alpha(0.78f + 0.20f * k));
            DrawSprite(canvas, _over, Ball, cx, cy, min * CoreRadius * 2f);
            _over.ColorFilter = null;
        }

        /// <summary>
        /// The inner texture: one baked spiral, rotated. A full turn takes ~34s active / ~75s
        /// dormant, and it is clipped to the body so the arms read as something moving INSIDE the
        /// orb rather than a pinwheel stuck on the front of it.
        /// </summary>
        private void DrawSwirl(SKCanvas canvas, float cx, float cy, float min, float k)
        {
            float d = min * CoreRadius * 2f;
            canvas.Save();
            try
            {
                canvas.ClipRoundRect(new SKRoundRect(
                    new SKRect(cx - d / 2f, cy - d / 2f, cx + d / 2f, cy + d / 2f), d / 2f, d / 2f), antialias: true);
                canvas.Translate(cx, cy);
                canvas.RotateDegrees(_swirlT * 10.6f);
                _add.ColorFilter = _particleTint;
                _add.Color = SKColors.White.WithAlpha(Alpha(0.34f * k));
                DrawSprite(canvas, _add, Swirl, 0, 0, d * 1.06f);
                _add.ColorFilter = null;
            }
            finally { canvas.Restore(); }
        }

        /// <summary>A breathing rim so the sphere has an edge instead of just fading out.</summary>
        private void DrawRim(SKCanvas canvas, float cx, float cy, float min, float k)
        {
            float r = min * CoreRadius;
            _over.ColorFilter = null;
            _over.Style = SKPaintStyle.Stroke;
            _over.StrokeWidth = Math.Max(1f, min * 0.016f);
            _over.Color = _glow.WithAlpha(Alpha(0.30f + 0.42f * k));
            canvas.DrawCircle(cx, cy, r, _over);
            _over.Style = SKPaintStyle.Fill;
        }

        /// <summary>
        /// The orbiting wisps. Polar positions are a pure function of the clock (no integrated
        /// state to drift), squashed vertically so the ring reads as a tilted orbit rather than a
        /// flat halo, and drawn back-to-front-agnostic additively - they are light, not objects.
        /// </summary>
        private void DrawWisps(SKCanvas canvas, float cx, float cy, float min, float k)
        {
            var wisps = _wisps;                       // load once: Reseed swaps the array wholesale
            int n = Math.Min(Math.Min(_wispN, wisps.Length), Math.Max(0, _liveWisps));
            if (n == 0) return;

            _add.ColorFilter = _particleTint;
            for (int i = 0; i < n; i++)
            {
                var p = wisps[i];
                float ang = p.Ang + p.AngSpd * _wispT;
                float rad = p.Rad * min + (float)Math.Sin(p.Bob + _wispT * p.BobSpd) * min * 0.018f;

                float x = cx + (float)Math.Cos(ang) * rad;
                // 0.62 vertical squash: the orbit is seen slightly from above.
                float y = cy + (float)Math.Sin(ang) * rad * 0.62f;

                // Wisps on the far side of the orbit dim, which is what sells the tilt.
                float depth = 0.55f + 0.45f * (float)Math.Sin(ang);
                float a = p.Alpha * depth * k * 0.75f;
                if (a <= 0.004f) continue;

                float size = p.Size * min * (0.8f + 0.4f * depth);
                _add.Color = SKColors.White.WithAlpha(Alpha(a));
                DrawSprite(canvas, _add, Dot, x, y, size);
            }
            _add.ColorFilter = null;
        }

        private static void DrawSprite(SKCanvas canvas, SKPaint paint, SKImage? img, float cx, float cy, float d)
        {
            if (img == null) return;
            canvas.DrawImage(img, new SKRect(cx - d / 2f, cy - d / 2f, cx + d / 2f, cy + d / 2f), Sampling, paint);
        }

        // ============================== helpers ==============================

        private static byte Alpha(float a) => (byte)Math.Clamp(a * 255f, 0f, 255f);

        private static SKColor Darken(SKColor c, float f) =>
            new((byte)(c.Red * f), (byte)(c.Green * f), (byte)(c.Blue * f));

        /// <summary>FxTheme.Read's Avalonia twin: the same key out of the app resource dictionary.</summary>
        private static SKColor ThemeColor(string key, SKColor fallback)
        {
            try
            {
                var app = Application.Current;
                if (app != null && app.TryGetResource(key, app.ActualThemeVariant, out var v) && v is Color c)
                    return new SKColor(c.R, c.G, c.B);
            }
            catch { }
            return fallback;
        }

        /// <summary>App.Logger's twin: Serilog's static Log, fully qualified past this method.</summary>
        private static void Log(string message) => Serilog.Log.Debug("{Message}", message);

        /// <summary>Soft white radial dot - the glow and wisp sprite.</summary>
        private static SKImage? Dot
        {
            get
            {
                if (_dot != null) return _dot;
                lock (_spriteLock) { _dot ??= BakeRadial(128, 0f, 1f); }
                return _dot;
            }
        }

        /// <summary>The sphere body: solid to ~72% of the radius, then a fast falloff.</summary>
        private static SKImage? Ball
        {
            get
            {
                if (_ball != null) return _ball;
                lock (_spriteLock) { _ball ??= BakeRadial(192, 0.72f, 0.55f); }
                return _ball;
            }
        }

        /// <summary>The rotating inner texture.</summary>
        private static SKImage? Swirl
        {
            get
            {
                if (_swirl != null) return _swirl;
                lock (_spriteLock) { _swirl ??= BakeSwirl(192); }
                return _swirl;
            }
        }

        /// <summary>
        /// A white radial with a flat core out to <paramref name="coreStop"/> and an edge alpha of
        /// <paramref name="edge"/> at the midpoint, so one bake serves both the soft glow dot and
        /// the much harder-edged sphere body.
        /// </summary>
        private static SKImage? BakeRadial(int size, float coreStop, float edge)
        {
            try
            {
                var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(info);
                surface.Canvas.Clear(SKColors.Transparent);
                float r = size / 2f;
                float mid = Math.Clamp(coreStop, 0f, 0.9f);
                using (var shader = SKShader.CreateRadialGradient(
                    new SKPoint(r, r), r,
                    new[]
                    {
                        SKColors.White,
                        SKColors.White,
                        SKColors.White.WithAlpha((byte)Math.Clamp(edge * 255f, 0f, 255f)),
                        SKColors.White.WithAlpha(0),
                    },
                    new[] { 0f, mid, Math.Min(0.97f, mid + (1f - mid) * 0.55f), 1f },
                    SKShaderTileMode.Clamp))
                using (var paint = new SKPaint { IsAntialias = true, Shader = shader })
                    surface.Canvas.DrawCircle(r, r, r, paint);
                return surface.Snapshot();
            }
            catch (Exception ex)
            {
                Log("TakeoverOrb.BakeRadial: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Four blurred logarithmic-spiral arms fading out towards the rim, baked once. Drawn white
        /// so every caller can colour-filter it to the mod palette.
        /// </summary>
        private static SKImage? BakeSwirl(int size)
        {
            try
            {
                var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(info);
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                float r = size / 2f;
                using var shader = SKShader.CreateRadialGradient(
                    new SKPoint(r, r), r,
                    new[] { SKColors.White, SKColors.White.WithAlpha(0xC0), SKColors.White.WithAlpha(0) },
                    new[] { 0f, 0.55f, 1f },
                    SKShaderTileMode.Clamp);
                using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, size * 0.014f);
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeWidth = size * 0.038f,
                    Shader = shader,
                    MaskFilter = blur,
                };

                const int arms = 4;
                const int steps = 26;
                for (int a = 0; a < arms; a++)
                {
                    using var path = new SKPath();
                    double start = a * (Math.PI * 2 / arms);
                    for (int s = 0; s <= steps; s++)
                    {
                        float t = s / (float)steps;
                        double ang = start + t * 4.4;                 // ~250 degrees of sweep
                        float rad = r * (0.14f + 0.78f * t);
                        float x = r + (float)Math.Cos(ang) * rad;
                        float y = r + (float)Math.Sin(ang) * rad;
                        if (s == 0) path.MoveTo(x, y);
                        else path.LineTo(x, y);
                    }
                    canvas.DrawPath(path, paint);
                }
                return surface.Snapshot();
            }
            catch (Exception ex)
            {
                Log("TakeoverOrb.BakeSwirl: " + ex.Message);
                return null;
            }
        }
    }
}
