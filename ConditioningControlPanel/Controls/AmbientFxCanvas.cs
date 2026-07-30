using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls
{
    /// <summary>The composable ambient layers an <see cref="AmbientFxCanvas"/> can run.</summary>
    [Flags]
    public enum AmbientFxLayers
    {
        None = 0,
        /// <summary>2-4 big blurred mod-tinted puffs on a 20-40s drift.</summary>
        FogDrift = 1 << 0,
        /// <summary>A slow gradient wash sliding behind everything else.</summary>
        AuroraWash = 1 << 1,
        /// <summary>Sparse additive dust sprites, budgeted by the performance tier.</summary>
        DustField = 1 << 2,
        /// <summary>An angled light band crossing the surface, one-shot or on an 8s+ loop.</summary>
        SheenSweep = 1 << 3,
        /// <summary>A pre-baked glow breathing 0.6 to 1.0 opacity.</summary>
        GlowBreath = 1 << 4,
    }

    /// <summary>Per-surface tuning for <see cref="AmbientFxCanvas.StartLayers(AmbientFxConfig)"/>.</summary>
    public sealed class AmbientFxConfig
    {
        public AmbientFxLayers Layers { get; set; } = AmbientFxLayers.None;

        /// <summary>Global multiplier on every layer's alpha (0-1.5).</summary>
        public double Intensity { get; set; } = 1.0;

        /// <summary>Fog puff count, clamped to 2-4.</summary>
        public int FogPuffs { get; set; } = 3;

        /// <summary>Seconds between sheen passes; floored at 8 so it never lands in the 2s uncanny valley.</summary>
        public double SheenPeriodSeconds { get; set; } = 12.0;

        /// <summary>Run the sheen once at start instead of looping.</summary>
        public bool SheenOneShot { get; set; }

        /// <summary>Glow-breath radius as a fraction of the shorter edge.</summary>
        public double GlowRadius { get; set; } = 0.45;

        /// <summary>Glow-breath centre in element-normalized coordinates.</summary>
        public Point GlowCenter { get; set; } = new(0.5, 0.5);
    }

    /// <summary>
    /// The one reusable in-window FX surface: a hit-test-invisible Skia canvas running a
    /// self-stopping ~30fps <see cref="DispatcherTimer"/>, composed from the layer vocabulary in
    /// <see cref="AmbientFxLayers"/>. Deliberately NOT the fullscreen compositor - that is
    /// per-monitor topmost overlay windows, and keeping its shared tick alive for ambient loops
    /// would undo the idle-parking that fixed #550. This control spawns no window of any kind.
    ///
    /// Rules it enforces for every caller:
    ///   • the clock only runs while the control is loaded, visible, and its window is active and
    ///     not minimized - the timer stops itself the moment any of that stops holding;
    ///   • colours (via <see cref="FxTheme"/>) and the performance budget are read ONCE at
    ///     (re)start and cached, never per tick; a mod switch re-reads them;
    ///   • nothing is allocated per frame - one persistent soft-dot <see cref="SKImage"/>, one glow
    ///     image, one <see cref="SKPaint"/> and colour filters rebuilt only when the palette moves;
    ///   • every tick is wrapped, and repeated faults stop the clock instead of spamming the log.
    ///
    /// All simulation state is in element-normalized (0-1) coordinates, so it is DPI- and
    /// Viewbox-agnostic and <see cref="Burst"/> takes plain element-local coordinates.
    /// </summary>
    public class AmbientFxCanvas : Decorator
    {
        private const int MaxBurstParticles = 150;
        private const int FaultLimit = 5;

        private readonly SKElement _sk;
        private readonly DispatcherTimer _timer;
        private AmbientFxConfig _config = new();

        // ---- cached at (re)start: never read per tick ----
        private PerformanceTier _tier = PerformanceTier.Quality;
        private int _particleBudget;
        private int _targetFps = 30;
        private SKColor _mist, _particle, _glow, _flash;
        private float _mistAlpha = 1f;
        private SKColorFilter? _mistTint, _particleTint, _glowTint, _flashTint;

        // ---- clocks ----
        private readonly System.Diagnostics.Stopwatch _clock = new();
        private double _lastTickMs;
        private float _fogT, _dustT, _sheenT, _breathT, _auroraT;
        private bool _sheenDone;

        // ---- perf governor (shape copied from ChaosModeService.UpdatePerfGovernor) ----
        private double _hitchScore;
        private int _liveBudget;
        private bool _fogOnly;

        // ---- sim ----
        private struct Puff { public float X, Y, R, VX, VY, Phase, PhaseSpd, BaseA; }
        private Puff[] _puffs = Array.Empty<Puff>();

        private struct Dust { public float X, Y, VX, VY, Life, Max, Size; }
        private Dust[] _dust = Array.Empty<Dust>();
        private int _dustN;

        private struct Spark { public float X, Y, VX, VY, Life, Max, Size; public uint Rgb; }
        private Spark[]? _burst;
        private int _burstN;
        private SKColorFilter? _burstTint;

        private bool _running;
        private bool _paused;
        private int _faults;
        private Window? _window;
        private readonly Random _rng = new();

        // Shared, built once for the whole app: a white soft radial sprite every tinted layer
        // draws (scaled + colour-filtered) instead of allocating shaders per puff per frame.
        private static SKImage? _dot;
        private static SKImage? _glowSprite;
        private static readonly object _spriteLock = new();

        private readonly SKPaint _paint = new()
        {
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Medium,
            BlendMode = SKBlendMode.Plus,
        };

        public AmbientFxCanvas()
        {
            IsHitTestVisible = false;
            _sk = new SKElement { IsHitTestVisible = false };
            _sk.PaintSurface += OnPaintSurface;
            Child = _sk;

            // Default (Background) priority on purpose: ambient FX must yield to input and layout,
            // and the frame gaps that causes are exactly what the governor reads to degrade itself.
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => Tick();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += OnIsVisibleChanged;
        }

        /// <summary>The layers this canvas was last asked to run.</summary>
        public AmbientFxLayers Layers => _config.Layers;

        /// <summary>True while the clock is actually ticking.</summary>
        public bool IsRunning => _timer.IsEnabled;

        // ================================ public API ================================

        /// <summary>Compose the surface from the layer flags with default tuning.</summary>
        public void StartLayers(AmbientFxLayers layers) => StartLayers(new AmbientFxConfig { Layers = layers });

        /// <summary>
        /// Compose the surface. Safe to call repeatedly - it re-reads the palette and the
        /// performance budget and reseeds the sim. Never starts a clock the tier or the
        /// reduced-motion setting has ruled out.
        /// </summary>
        public void StartLayers(AmbientFxConfig config)
        {
            _config = config ?? new AmbientFxConfig();
            _paused = false;
            _faults = 0;
            _running = _config.Layers != AmbientFxLayers.None;
            ReadEnvironment();
            Reseed();
            Evaluate();
        }

        /// <summary>Park the clock and keep the composed state (tab switch, window deactivate).</summary>
        public void Pause()
        {
            _paused = true;
            StopClock();
        }

        /// <summary>Un-park a paused canvas. No-op if the environment still says no.</summary>
        public void Resume()
        {
            _paused = false;
            Evaluate();
        }

        /// <summary>Tear the surface down completely and release the sim buffers.</summary>
        public void Stop()
        {
            _running = false;
            _paused = false;
            StopClock();
            _burst = null;
            _burstN = 0;
            _dustN = 0;
            _sk.InvalidateVisual();
        }

        /// <summary>
        /// One-shot particle burst at an element-local point (WPF units): 60-150 sparks over
        /// ~1.2s, then the buffer is released. Skipped entirely when particles are not allowed -
        /// event moments cost nothing at the Performance tier or under reduced motion.
        /// </summary>
        public void Burst(double x, double y, System.Windows.Media.Color? color = null, int count = 90)
        {
            try
            {
                if (!MotionFx.AllowParticles) return;
                double w = ActualWidth, h = ActualHeight;
                if (w <= 1 || h <= 1) return;

                if (_particleBudget <= 0) ReadEnvironment();
                count = Math.Clamp(count, 60, Math.Min(MaxBurstParticles, Math.Max(60, _particleBudget * 2)));

                var c = color is { } wc ? new SKColor(wc.R, wc.G, wc.B) : _particle;
                uint rgb = (uint)((c.Red << 16) | (c.Green << 8) | c.Blue);
                _burstTint?.Dispose();
                _burstTint = SKColorFilter.CreateBlendMode(c, SKBlendMode.Modulate);

                _burst ??= new Spark[MaxBurstParticles];
                _burstN = 0;
                float nx = (float)(x / w), ny = (float)(y / h);
                for (int i = 0; i < count && _burstN < MaxBurstParticles; i++)
                {
                    double a = _rng.NextDouble() * Math.PI * 2;
                    float spd = 0.18f + (float)_rng.NextDouble() * 0.55f;
                    float life = 0.55f + (float)_rng.NextDouble() * 0.65f;
                    _burst[_burstN++] = new Spark
                    {
                        X = nx, Y = ny,
                        VX = (float)Math.Cos(a) * spd,
                        VY = (float)Math.Sin(a) * spd - 0.10f,
                        Life = life, Max = life,
                        Size = 0.006f + (float)_rng.NextDouble() * 0.010f,
                        Rgb = rgb,
                    };
                }
                Evaluate();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AmbientFxCanvas.Burst: {E}", ex.Message);
            }
        }

        // ============================== environment ==============================

        private void ReadEnvironment()
        {
            try
            {
                _tier = PerformanceProfile.CurrentTier;
                _particleBudget = PerformanceProfile.MaxAmbientParticles(_tier);
                _targetFps = PerformanceProfile.FxTargetFps(_tier);
                _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _targetFps));

                _mist = ToSk(FxTheme.MistColor);
                _particle = ToSk(FxTheme.ParticleColor);
                _glow = ToSk(FxTheme.GlowColor);
                _flash = ToSk(FxTheme.FlashTintColor);
                _mistAlpha = (float)Math.Clamp(FxTheme.MistOpacity, 0.0, 1.0);

                _mistTint?.Dispose(); _mistTint = SKColorFilter.CreateBlendMode(_mist, SKBlendMode.Modulate);
                _particleTint?.Dispose(); _particleTint = SKColorFilter.CreateBlendMode(_particle, SKBlendMode.Modulate);
                _glowTint?.Dispose(); _glowTint = SKColorFilter.CreateBlendMode(_glow, SKBlendMode.Modulate);
                _flashTint?.Dispose(); _flashTint = SKColorFilter.CreateBlendMode(_flash, SKBlendMode.Modulate);

                _liveBudget = _particleBudget;
                _fogOnly = false;
                _hitchScore = 0;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AmbientFxCanvas.ReadEnvironment: {E}", ex.Message);
            }
        }

        private void Reseed()
        {
            int puffs = Math.Clamp(_config.FogPuffs, 2, 4);
            _puffs = new Puff[puffs];
            for (int i = 0; i < puffs; i++)
            {
                float t = (i + 0.5f) / puffs;
                _puffs[i] = new Puff
                {
                    X = 0.12f + 0.76f * Frac(t * 1.7f),
                    Y = 0.20f + 0.70f * Frac(t * 2.3f),
                    R = 0.34f + 0.24f * Frac(t * 3.1f),
                    // 20-40s to cross: the ambient clock, never the 2s uncanny valley.
                    VX = (0.030f + 0.020f * Frac(t * 5f)) * (i % 2 == 0 ? 1f : -1f),
                    VY = -(0.022f + 0.016f * Frac(t * 4f)),
                    Phase = t * 6.283f,
                    PhaseSpd = 0.30f + 0.22f * Frac(t * 6f),
                    BaseA = 0.26f + 0.16f * Frac(t * 7f),
                };
            }

            _dust = _particleBudget > 0 ? new Dust[_particleBudget] : Array.Empty<Dust>();
            _dustN = 0;
            _fogT = _dustT = _sheenT = _breathT = _auroraT = 0f;
            _sheenDone = false;
            _burstN = 0;
        }

        // ============================== lifecycle ==============================

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                HookWindow(Window.GetWindow(this));
                if (App.Mods != null) App.Mods.ModChanged += OnModChanged;
                Evaluate();
            }
            catch (Exception ex) { App.Logger?.Debug("AmbientFxCanvas.OnLoaded: {E}", ex.Message); }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                StopClock();
                UnhookWindow();
                if (App.Mods != null) App.Mods.ModChanged -= OnModChanged;
            }
            catch (Exception ex) { App.Logger?.Debug("AmbientFxCanvas.OnUnloaded: {E}", ex.Message); }
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => Evaluate();

        private void OnModChanged(object? sender, ModPackage mod)
        {
            void Apply()
            {
                try
                {
                    ReadEnvironment();
                    _sk.InvalidateVisual();
                }
                catch (Exception ex) { App.Logger?.Debug("AmbientFxCanvas.OnModChanged: {E}", ex.Message); }
            }
            try
            {
                if (Dispatcher.CheckAccess()) Apply();
                else Dispatcher.BeginInvoke((Action)Apply);
            }
            catch { }
        }

        private void HookWindow(Window? window)
        {
            if (ReferenceEquals(_window, window)) return;
            UnhookWindow();
            _window = window;
            if (_window == null) return;
            _window.Activated += OnWindowStateish;
            _window.Deactivated += OnWindowStateish;
            _window.StateChanged += OnWindowStateish;
        }

        private void UnhookWindow()
        {
            if (_window == null) return;
            _window.Activated -= OnWindowStateish;
            _window.Deactivated -= OnWindowStateish;
            _window.StateChanged -= OnWindowStateish;
            _window = null;
        }

        private void OnWindowStateish(object? sender, EventArgs e) => Evaluate();

        /// <summary>The single gate: start the clock only when everything says it may run.</summary>
        private void Evaluate()
        {
            try
            {
                if (ShouldRun()) StartClock();
                else StopClock();
            }
            catch (Exception ex) { App.Logger?.Debug("AmbientFxCanvas.Evaluate: {E}", ex.Message); }
        }

        private bool ShouldRun()
        {
            // A live burst outruns the ambient gates: it is a one-shot event moment, already
            // budget-checked at emit time, and it may fire on a canvas running no ambient layers.
            bool burstLive = _burst != null && _burstN > 0;
            if (_paused || _faults >= FaultLimit) return false;
            if (!_running && !burstLive) return false;
            if (!IsLoaded || !IsVisible) return false;
            if (!burstLive)
            {
                if (_targetFps <= 0) return false;
                if (!MotionFx.AllowAmbientLoops) return false;
            }
            var w = _window;
            if (w != null)
            {
                if (w.WindowState == WindowState.Minimized) return false;
                if (!w.IsActive && !burstLive) return false;
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

        // ================================ tick ================================

        private void Tick()
        {
            try
            {
                if (!ShouldRun()) { StopClock(); return; }

                double nowMs = _clock.Elapsed.TotalMilliseconds;
                double gapMs = nowMs - _lastTickMs;
                _lastTickMs = nowMs;
                float dt = (float)Math.Clamp(gapMs / 1000.0, 0.001, 0.100);

                Governor(gapMs);

                _fogT += dt;
                _auroraT += dt;
                _breathT += dt;
                if (!_sheenDone) _sheenT += dt;
                StepDust(dt);
                StepBurst(dt);

                _sk.InvalidateVisual();
            }
            catch (Exception ex)
            {
                _faults++;
                App.Logger?.Warning("AmbientFxCanvas tick failed ({N}/{Max}): {E}", _faults, FaultLimit, ex.Message);
                if (_faults >= FaultLimit)
                {
                    App.Logger?.Warning("AmbientFxCanvas: stopping after repeated faults");
                    StopClock();
                }
            }
        }

        /// <summary>
        /// Mini perf governor: a frame gap well over budget builds pressure, which first halves the
        /// particle budget and then drops the canvas to fog only. The score decays ~7% a frame, so
        /// a clean run recovers over about 5s; one stray hitch changes nothing.
        /// </summary>
        private void Governor(double gapMs)
        {
            double budgetMs = 1000.0 / Math.Max(1, _targetFps);
            _hitchScore = Math.Max(0, _hitchScore * 0.93);
            if (gapMs > budgetMs * 2.5) _hitchScore += gapMs > budgetMs * 6 ? 3.0 : 1.0;

            bool fogOnly = _hitchScore >= 12;
            int budget = fogOnly ? 0 : _hitchScore >= 5 ? _particleBudget / 2 : _particleBudget;

            if (fogOnly != _fogOnly)
            {
                _fogOnly = fogOnly;
                if (fogOnly) App.Logger?.Information("[FXPERF] ambient canvas dropped to fog-only (hitch score {S:F1})", _hitchScore);
                else App.Logger?.Information("[FXPERF] ambient canvas recovered");
            }
            if (budget < _liveBudget) _liveBudget = budget;
            else if (budget > _liveBudget) _liveBudget = Math.Min(budget, _liveBudget + 1);
            if (_liveBudget < _dustN) _dustN = Math.Max(0, _liveBudget);
        }

        private void StepDust(float dt)
        {
            if (_dust.Length == 0) return;
            for (int i = _dustN - 1; i >= 0; i--)
            {
                var d = _dust[i];
                d.X += d.VX * dt;
                d.Y += d.VY * dt;
                d.Life -= dt;
                if (d.Life <= 0f || d.X < -0.1f || d.X > 1.1f || d.Y < -0.1f || d.Y > 1.1f)
                    _dust[i] = _dust[--_dustN];
                else
                    _dust[i] = d;
            }

            if ((_config.Layers & AmbientFxLayers.DustField) == 0 || _fogOnly) return;

            _dustT += dt;
            // Refill slowly so the field breathes rather than blinking back in all at once.
            while (_dustN < Math.Min(_liveBudget, _dust.Length) && _dustT > 0.12f)
            {
                _dustT -= 0.12f;
                float life = 5f + (float)_rng.NextDouble() * 9f;
                _dust[_dustN++] = new Dust
                {
                    X = (float)_rng.NextDouble(),
                    Y = (float)_rng.NextDouble(),
                    VX = (float)(_rng.NextDouble() - 0.5) * 0.014f,
                    VY = -0.010f - (float)_rng.NextDouble() * 0.016f,
                    Life = life, Max = life,
                    Size = 0.0035f + (float)_rng.NextDouble() * 0.0055f,
                };
            }
        }

        private void StepBurst(float dt)
        {
            if (_burst == null || _burstN == 0) return;
            for (int i = _burstN - 1; i >= 0; i--)
            {
                var s = _burst[i];
                s.X += s.VX * dt;
                s.Y += s.VY * dt;
                s.VY += 0.55f * dt;      // gravity settle
                s.VX *= 0.965f;
                s.Life -= dt;
                if (s.Life <= 0f) _burst[i] = _burst[--_burstN];
                else _burst[i] = s;
            }
            if (_burstN == 0)
            {
                // Full teardown: the buffer and its tint go away until the next event moment.
                _burst = null;
                _burstTint?.Dispose();
                _burstTint = null;
                Evaluate();
            }
        }

        // ================================ paint ================================

        private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            var info = e.Info;
            if (info.Width <= 0 || info.Height <= 0) return;

            try
            {
                float w = info.Width, h = info.Height;
                float min = Math.Min(w, h);
                float intensity = (float)Math.Clamp(_config.Intensity, 0.0, 1.5);
                var layers = _config.Layers;

                if (!_fogOnly && (layers & AmbientFxLayers.AuroraWash) != 0) DrawAurora(canvas, w, h, intensity);
                if ((layers & AmbientFxLayers.FogDrift) != 0) DrawFog(canvas, w, h, min, intensity);
                if (!_fogOnly && (layers & AmbientFxLayers.GlowBreath) != 0) DrawGlowBreath(canvas, w, h, min, intensity);
                if (!_fogOnly && (layers & AmbientFxLayers.DustField) != 0) DrawDust(canvas, w, h, min, intensity);
                if (!_fogOnly && (layers & AmbientFxLayers.SheenSweep) != 0) DrawSheen(canvas, w, h, intensity);
                DrawBurst(canvas, w, h, min);
            }
            catch (Exception ex)
            {
                _faults++;
                App.Logger?.Debug("AmbientFxCanvas.OnPaintSurface: {E}", ex.Message);
            }
        }

        private void DrawAurora(SKCanvas canvas, float w, float h, float intensity)
        {
            // One long, slow diagonal wash: two mod colours sliding across the surface. Drawn as
            // the tinted sprite stretched way past the bounds so nothing needs a per-frame shader.
            float phase = (float)((Math.Sin(_auroraT * 0.06) + 1) * 0.5);
            float bw = w * 2.4f, bh = h * 2.4f;
            float cx = -w * 0.7f + phase * w * 1.4f;
            float cy = -h * 0.7f + (1f - phase) * h * 1.4f;

            _paint.ColorFilter = _mistTint;
            _paint.Color = SKColors.White.WithAlpha(Alpha(0.13f * intensity * _mistAlpha));
            DrawSprite(canvas, Dot, cx, cy, bw, bh);

            _paint.ColorFilter = _glowTint;
            _paint.Color = SKColors.White.WithAlpha(Alpha(0.10f * intensity * _mistAlpha));
            DrawSprite(canvas, Dot, w - cx, h - cy, bw * 0.8f, bh * 0.8f);
            _paint.ColorFilter = null;
        }

        private void DrawFog(SKCanvas canvas, float w, float h, float min, float intensity)
        {
            _paint.ColorFilter = _mistTint;
            for (int i = 0; i < _puffs.Length; i++)
            {
                var p = _puffs[i];
                // Position is a pure function of the clock — no integration state to drift, and a
                // paused/resumed canvas picks up exactly where the elapsed time says it should.
                float px = Frac2(p.X + p.VX * _fogT);
                float py = Frac2(p.Y + p.VY * _fogT);
                float a = p.BaseA * (0.70f + 0.30f * (float)Math.Sin(p.Phase + _fogT * p.PhaseSpd))
                          * intensity * _mistAlpha;
                if (a <= 0.004f) continue;
                float d = p.R * min * 2f;
                _paint.Color = SKColors.White.WithAlpha(Alpha(a));
                DrawSprite(canvas, Dot, px * w, py * h, d, d);
            }
            _paint.ColorFilter = null;
        }

        private void DrawGlowBreath(SKCanvas canvas, float w, float h, float min, float intensity)
        {
            float breath = 0.60f + 0.40f * (float)((Math.Sin(_breathT * 0.62) + 1) * 0.5);
            float d = (float)Math.Clamp(_config.GlowRadius, 0.05, 1.5) * min * 2f;
            _paint.ColorFilter = _glowTint;
            _paint.Color = SKColors.White.WithAlpha(Alpha(0.30f * breath * intensity));
            DrawSprite(canvas, GlowSprite, (float)_config.GlowCenter.X * w, (float)_config.GlowCenter.Y * h, d, d);
            _paint.ColorFilter = null;
        }

        private void DrawDust(SKCanvas canvas, float w, float h, float min, float intensity)
        {
            if (_dustN == 0) return;
            _paint.ColorFilter = _particleTint;
            for (int i = 0; i < _dustN; i++)
            {
                var d = _dust[i];
                float env = (float)Math.Sin(Math.PI * Math.Clamp(1.0 - d.Life / d.Max, 0.0, 1.0));
                float a = 0.55f * env * intensity;
                if (a <= 0.004f) continue;
                float size = d.Size * min * 2f;
                _paint.Color = SKColors.White.WithAlpha(Alpha(a));
                DrawSprite(canvas, Dot, d.X * w, d.Y * h, size, size);
            }
            _paint.ColorFilter = null;
        }

        private void DrawSheen(SKCanvas canvas, float w, float h, float intensity)
        {
            double period = Math.Max(8.0, _config.SheenPeriodSeconds);
            const float sweepDur = 1.5f;
            float phase = _config.SheenOneShot ? _sheenT : (float)(_sheenT % period);
            if (_config.SheenOneShot && phase > sweepDur) { _sheenDone = true; return; }
            if (phase > sweepDur) return;

            float p = phase / sweepDur;
            float env = (float)Math.Sin(Math.PI * p);
            float a = 0.30f * env * intensity;
            if (a <= 0.004f) return;

            float band = w * 0.22f;
            float cx = -band + p * (w + band * 2f);

            canvas.Save();
            canvas.Translate(cx, h * 0.5f);
            canvas.RotateDegrees(18f);
            _paint.ColorFilter = _flashTint;
            _paint.Color = SKColors.White.WithAlpha(Alpha(a));
            DrawSprite(canvas, Dot, 0, 0, band, h * 2.4f);
            _paint.ColorFilter = null;
            canvas.Restore();
        }

        private void DrawBurst(SKCanvas canvas, float w, float h, float min)
        {
            if (_burst == null || _burstN == 0) return;
            _paint.ColorFilter = _burstTint;
            for (int i = 0; i < _burstN; i++)
            {
                var s = _burst[i];
                float env = Math.Clamp(s.Life / s.Max, 0f, 1f);
                float a = 0.95f * env;
                float size = s.Size * min * 2f * (0.6f + 0.4f * env);
                _paint.Color = SKColors.White.WithAlpha(Alpha(a));
                DrawSprite(canvas, Dot, s.X * w, s.Y * h, size, size);
            }
            _paint.ColorFilter = null;
        }

        private void DrawSprite(SKCanvas canvas, SKImage? img, float cx, float cy, float w, float h)
        {
            if (img == null) return;
            canvas.DrawImage(img, new SKRect(cx - w / 2f, cy - h / 2f, cx + w / 2f, cy + h / 2f), _paint);
        }

        // ============================== helpers ==============================

        private static byte Alpha(float a) => (byte)Math.Clamp(a * 255f, 0f, 255f);

        private static float Frac(float v) { v -= (float)Math.Floor(v); return v; }

        /// <summary>Wrap into a -0.3..1.3 band so puffs drift off one edge and back on the other.</summary>
        private static float Frac2(float v) => Frac((v + 0.3f) / 1.6f) * 1.6f - 0.3f;

        private static SKColor ToSk(System.Windows.Media.Color c) => new(c.R, c.G, c.B);

        /// <summary>White soft radial dot, baked once and shared by every canvas in the app.</summary>
        private static SKImage? Dot
        {
            get
            {
                if (_dot != null) return _dot;
                lock (_spriteLock) { _dot ??= BakeRadial(128, 0f); }
                return _dot;
            }
        }

        /// <summary>A tighter-cored radial for the glow-breath layer.</summary>
        private static SKImage? GlowSprite
        {
            get
            {
                if (_glowSprite != null) return _glowSprite;
                lock (_spriteLock) { _glowSprite ??= BakeRadial(160, 0.28f); }
                return _glowSprite;
            }
        }

        private static SKImage? BakeRadial(int size, float coreStop)
        {
            try
            {
                var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(info);
                surface.Canvas.Clear(SKColors.Transparent);
                float r = size / 2f;
                using (var shader = SKShader.CreateRadialGradient(
                    new SKPoint(r, r), r,
                    new[] { SKColors.White, SKColors.White, SKColors.White.WithAlpha(0) },
                    new[] { 0f, Math.Clamp(coreStop, 0f, 0.9f), 1f },
                    SKShaderTileMode.Clamp))
                using (var paint = new SKPaint { IsAntialias = true, Shader = shader })
                    surface.Canvas.DrawCircle(r, r, r, paint);
                return surface.Snapshot();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AmbientFxCanvas.BakeRadial: {E}", ex.Message);
                return null;
            }
        }
    }
}
