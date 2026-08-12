using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// THE VAT — the daily XP meter, drawn as a glass specimen jar around the
    /// profile picture. Mod-coloured liquid fills as the server banks today's XP:
    /// 20% banks the day, 100% is the level-scaled cap, and the overflow lip sits
    /// on top (everything above cap flows to lifetime XP only and runs down the
    /// outside of the glass).
    ///
    /// PORT, NOT A REINTERPRETATION. Every number here comes from the owner-locked
    /// mockup engine (planning/one-descent/mockups/vat-engine.js) by way of the web
    /// client's port (cclabs-web src/lib/descent/vat-engine.ts): the jar at
    /// 10%..90% x 17.5%..95.5%, the portrait circle at 30% of the jar width, the
    /// two-term wave, the 2.1s pour, the faucet sliding in from the LEFT with its
    /// spout tip at (0.906, 0.989) of the art, the 20/CAP/MAX ticks, the foam over
    /// 98% and the outside spill past the brim. Three clients draw one vat; where
    /// this file and the web engine differ, one of them is wrong.
    ///
    /// WHY NATIVE SKIA AND NOT WEBVIEW2, given the one-implementation law
    /// (DECISIONS.md 2026-08-10, "Spiral Track = web canvas first, hosted in WPF via
    /// WebView2"): the amendment that carved out the spiral RAIL applies here word
    /// for word — "a rail-docked WebView2 HWND would sit over every tab transition —
    /// airspace". This surface lives INSIDE ProfileHeroCard, which is ClipToBounds
    /// with a 14px CornerRadius, inside a ScrollViewer, under AnimateTabIn's
    /// opacity/transform tab transition and StaggerProfileCards' entrance animation,
    /// with an avatar DECORATION layer that must draw ABOVE the portrait. A native
    /// child HWND clips to none of that, animates with none of it, and cannot be
    /// drawn over — the repo says so at every WebView2 call site (BrowserVideoSurface
    /// §HARD RULE; SettingsTabView's browser-card airspace note). SKElement is a
    /// WPF-composited bitmap: it clips, fades, scrolls and stacks like everything
    /// else on the card, and costs no browser process for a 166x218 meter.
    ///
    /// THE PORTRAIT IS NOT DRAWN HERE. A real AdornedAvatar sits underneath (the web
    /// does the same via skipPortrait) so the Discord CDN, the OG ring and the
    /// wardrobe decoration stay in one place and none of them enter this canvas.
    /// </summary>
    public sealed class VatGlassCanvas : System.Windows.Controls.Decorator
    {
        // ------------------------------------------------------------- geometry
        // Fractions of the control box, exactly as the mockup has them, so the host
        // picks the size and nothing here needs to know what it chose.

        private const float JarX0 = 0.10f;
        private const float JarX1 = 0.90f;
        private const float JarYTop = 0.175f;
        private const float JarYBottom = 0.955f;
        private const float JarRadius = 9f;

        /// <summary>Portrait diameter as a fraction of the whole box: 2 x 0.30 x (0.90 - 0.10).</summary>
        public const double PortraitDiameterRatio = 2 * 0.30 * (JarX1 - JarX0);

        /// <summary>Portrait centre as a fraction of the box height (the mockup's .vatwrap value).</summary>
        public const double PortraitCenterYRatio = 0.565;

        /// <summary>The aspect the mockup locks the jar to (152 x 200).</summary>
        public const double JarAspect = 200.0 / 152.0;

        /// <summary>Spout tip inside the faucet art, as a fraction of its box.</summary>
        private const float SpoutX = 0.906f;
        private const float SpoutY = 0.989f;

        /// <summary>Seconds one pour runs for — covers the slide-in and a good pour.</summary>
        public const double PourSeconds = 2.1;

        /// <summary>The base overflow lip, used until the server names this account's.</summary>
        private const double BaseLip = 1.20;

        private const int FaultLimit = 5;

        // --------------------------------------------------------------- surface

        private readonly SKElement _sk;
        private readonly DispatcherTimer _timer;
        private readonly System.Diagnostics.Stopwatch _clock = new();
        private readonly Random _rng = new();
        private double _lastTickMs;
        private int _faults;
        private Window? _window;

        // ----------------------------------------------------------- engine state

        private double _fill;          // the level actually drawn
        private double _target;        // the level the server says it should be
        private double _pourT;         // seconds of pour remaining
        private double _slide;         // faucet slide-in from the left, 0..1
        private double _now;           // ms on the wave/bubble clock

        private struct Bubble { public float X, Y, R, V, Ph; }
        private struct Mote { public float X, Y, VX, VY, Life; }

        private readonly List<Bubble> _bubbles = new();
        private readonly List<Mote> _splash = new();
        private readonly List<Mote> _spill = new();

        // ------------------------------------------------------------- the scale
        // LIP, BRIM and CEIL are the meter's scale, resolved from the server's
        // fill_lip_pct. A LIP OF 1.0 IS THE LEGAL "NO LIP" JAR: no band, no MAX
        // tick, nothing above the cap to spill.

        private double _rawLip = BaseLip;

        private bool HasLip => _rawLip > 1.0;
        private double Lip => HasLip ? Math.Max(1.02, _rawLip) : 1.0;
        private double Brim => HasLip ? Lip - 0.02 : 1.0;
        private double Ceiling => HasLip ? Lip + 0.02 : 1.0;

        // --------------------------------------------------------------- palette

        private SKColor _liquid, _liquidEdge, _accent;
        private static SKImage? _faucet;
        private static bool _faucetTried;
        private static readonly object FaucetLock = new();

        public VatGlassCanvas()
        {
            IsHitTestVisible = false;
            _sk = new SKElement { IsHitTestVisible = false };
            _sk.PaintSurface += OnPaintSurface;
            Child = _sk;

            // Background priority, same as AmbientFxCanvas: a decorative meter yields
            // to input and layout, it never competes with them.
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => Tick();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += (_, _) => Evaluate();

            ReadPalette();
        }

        /// <summary>The level currently drawn, as a fraction of cap.</summary>
        public double Fill => _fill;

        /// <summary>
        /// Fires only when the WHOLE percent of the drawn level changes, so a readout
        /// can hang off it without repainting text 30 times a second. The web
        /// surface's onFill contract, same rounding.
        /// </summary>
        public event EventHandler<int>? FillPercentChanged;

        private int _lastPct = -1;

        private void NotifyPercent()
        {
            int pct = (int)Math.Round(_fill * 100);
            if (pct == _lastPct) return;
            _lastPct = pct;
            try { FillPercentChanged?.Invoke(this, pct); }
            catch (Exception ex) { App.Logger?.Debug("VatGlassCanvas.FillPercentChanged: {E}", ex.Message); }
        }

        /// <summary>True while the faucet is in and pouring.</summary>
        public bool IsPouring => _pourT > 0;

        /// <summary>
        /// Reduced motion is the desktop's prefers-reduced-motion: waves, bubbles,
        /// the faucet, splash and spill all drop out and the level snaps instead of
        /// easing. The vat itself never disappears — a user who turned animation off
        /// did not ask to stop seeing today's XP.
        /// </summary>
        private static bool Animated => MotionFx.AllowAmbientLoops;

        // ============================== public API ==============================

        /// <summary>
        /// The meter's scale, from the server's `vat.fill_lip_pct` / 100 — 1.20 base,
        /// 1.25 at stage 4+, 1.30 at stage 6+. It rescales the whole jar, so it is
        /// applied at once rather than animated.
        /// </summary>
        public void SetLip(double lipFraction)
        {
            if (!double.IsFinite(lipFraction) || lipFraction <= 0) return;
            if (Math.Abs(lipFraction - _rawLip) < 0.0001) return;
            _rawLip = lipFraction;
            _fill = Math.Min(_fill, Ceiling);
            _target = Math.Min(_target, Ceiling);
            _sk.InvalidateVisual();
        }

        /// <summary>Snap to a level with no theater — the first server read of a session.</summary>
        public void Seed(double fill)
        {
            _target = Clamp(fill);
            _fill = _target;
            _pourT = 0;
            _slide = 0;
            NotifyPercent();
            _sk.InvalidateVisual();
            Evaluate();
        }

        /// <summary>
        /// Ease to a new level with no faucet: a grant under VatPourMinXp, a
        /// correction, or the drop at UTC midnight. Snaps under reduced motion.
        /// </summary>
        public void EaseTo(double fill)
        {
            _target = Clamp(fill);
            if (!Animated) { _fill = _target; NotifyPercent(); }
            _sk.InvalidateVisual();
            Evaluate();
        }

        /// <summary>
        /// Run the faucet to a new level.
        ///
        /// A POUR ARRIVING MID-POUR EXTENDS, IT NEVER RESTARTS: the timer is pushed
        /// back out to a full window while <see cref="_slide"/> is left exactly where
        /// it is, so the faucet does not retract and swing in again — it keeps
        /// pouring and the stream simply lasts longer. The level is re-aimed at the
        /// new target and the per-frame rate is taken from the time REMAINING, so the
        /// liquid still lands on the number as the faucet leaves.
        /// </summary>
        public void PourTo(double fill)
        {
            _target = Clamp(fill);
            if (!Animated) { _fill = _target; NotifyPercent(); _sk.InvalidateVisual(); return; }
            _pourT = PourSeconds;
            _sk.InvalidateVisual();
            Evaluate();
        }

        /// <summary>Re-read the mod accent (mod switch).</summary>
        public void RefreshPalette()
        {
            ReadPalette();
            _sk.InvalidateVisual();
        }

        private double Clamp(double f) => double.IsFinite(f) ? Math.Max(0, Math.Min(Ceiling, f)) : 0;

        // ============================== lifecycle ==============================

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                HookWindow(Window.GetWindow(this));
                if (App.Mods != null) App.Mods.ModChanged += OnModChanged;
                ReadPalette();
                Evaluate();
            }
            catch (Exception ex) { App.Logger?.Debug("VatGlassCanvas.OnLoaded: {E}", ex.Message); }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                StopClock();
                UnhookWindow();
                if (App.Mods != null) App.Mods.ModChanged -= OnModChanged;
            }
            catch (Exception ex) { App.Logger?.Debug("VatGlassCanvas.OnUnloaded: {E}", ex.Message); }
        }

        private void OnModChanged(object? sender, Models.ModPackage mod)
        {
            void Apply()
            {
                try { RefreshPalette(); }
                catch (Exception ex) { App.Logger?.Debug("VatGlassCanvas.OnModChanged: {E}", ex.Message); }
            }
            try
            {
                if (Dispatcher.CheckAccess()) Apply();
                else Dispatcher.BeginInvoke((Action)Apply);
            }
            catch { /* a palette that failed to follow a mod switch is not worth a crash */ }
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

        private void Evaluate()
        {
            try
            {
                if (ShouldRun()) StartClock();
                else StopClock();
            }
            catch (Exception ex) { App.Logger?.Debug("VatGlassCanvas.Evaluate: {E}", ex.Message); }
        }

        /// <summary>
        /// The single gate. Note what is NOT in it: "the fill has settled". The idle
        /// vat still breathes — the wave and a slow bubble stream — because a
        /// dead-still jar reads as a broken image. The clock runs whenever the
        /// surface is on screen and animation is allowed, and stops the instant it
        /// is not.
        /// </summary>
        private bool ShouldRun()
        {
            if (_faults >= FaultLimit) return false;
            if (!IsLoaded || !IsVisible) return false;
            if (!Animated) return false;
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

        // ================================= tick =================================

        private void Tick()
        {
            try
            {
                if (!ShouldRun()) { StopClock(); return; }

                double nowMs = _clock.Elapsed.TotalMilliseconds;
                double gapMs = nowMs - _lastTickMs;
                _lastTickMs = nowMs;
                double dt = Math.Clamp(gapMs / 1000.0, 0.001, 0.05);
                _now = nowMs;

                Step(dt);
                _sk.InvalidateVisual();
            }
            catch (Exception ex)
            {
                _faults++;
                App.Logger?.Warning("VatGlassCanvas tick failed ({N}/{Max}): {E}", _faults, FaultLimit, ex.Message);
                if (_faults >= FaultLimit)
                {
                    App.Logger?.Warning("VatGlassCanvas: stopping after repeated faults");
                    StopClock();
                }
            }
        }

        /// <summary>Advance the simulation. State only — Draw() paints, it never steps.</summary>
        private void Step(double dt)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 1 || h <= 1) return;

            float x0 = (float)(w * JarX0), x1 = (float)(w * JarX1);
            float yT = (float)(h * JarYTop), yB = (float)(h * JarYBottom);

            // ---- level
            if (_pourT > 0)
            {
                _pourT = Math.Max(0, _pourT - dt);
                // Rate from the time REMAINING: an extended pour re-aims without a
                // jump and still lands on the number when the faucet leaves.
                double k = Math.Min(1.0, dt / Math.Max(0.08, _pourT));
                _fill += (_target - _fill) * k;
            }
            else if (Math.Abs(_target - _fill) > 0.0002)
            {
                _fill += (_target - _fill) * Math.Min(1.0, dt * 3.2);
            }
            else
            {
                _fill = _target;
            }
            NotifyPercent();

            // ---- faucet slide
            double slideTarget = _pourT > 0 ? 1 : 0;
            _slide += (slideTarget - _slide) * Math.Min(1, dt * 5.5);

            float ySurf = YFor(yT, yB, _fill);

            // ---- bubbles
            double rate = (_pourT > 0 ? 0.5 : 0.12) * (dt * 60.0);
            if (_rng.NextDouble() < rate)
            {
                _bubbles.Add(new Bubble
                {
                    X = x0 + 6 + (float)(_rng.NextDouble() * (x1 - x0 - 12)),
                    Y = yB - 4,
                    R = 0.8f + (float)(_rng.NextDouble() * 2.2),
                    V = 12f + (float)(_rng.NextDouble() * 16),
                    Ph = (float)(_rng.NextDouble() * Math.PI * 2),
                });
            }
            for (int i = _bubbles.Count - 1; i >= 0; i--)
            {
                var b = _bubbles[i];
                b.Y -= b.V * (float)dt;
                float bx = b.X + (float)(Math.Sin(_now / 400 + b.Ph) * 2.2);
                if (b.Y < ySurf + Wave(bx, true) + 2) { _bubbles.RemoveAt(i); continue; }
                _bubbles[i] = b;
            }

            // ---- splash from the stream landing
            if (_pourT > 0 && _slide > 0.88)
            {
                float fx = SpoutXAt((float)w, (float)h, yT);
                if (_rng.NextDouble() < 0.7 * (dt * 60.0))
                {
                    _splash.Add(new Mote
                    {
                        X = fx,
                        Y = ySurf + Wave(fx, true),
                        VX = (float)((_rng.NextDouble() - 0.5) * 40),
                        VY = -(20f + (float)(_rng.NextDouble() * 40)),
                        Life = 0.6f,
                    });
                }
            }

            // ---- OVERFLOW THEATER IS NOT GATED BY THE POUR THRESHOLD. An account
            // parked past the brim spills down the outside of the glass whether or
            // not the last grant was big enough to swing the faucet in.
            if (HasLip && _fill >= Brim && _rng.NextDouble() < 0.5 * (dt * 60.0))
            {
                float side = _rng.NextDouble() < 0.5 ? x0 : x1;
                _spill.Add(new Mote
                {
                    X = side + (float)((_rng.NextDouble() - 0.5) * 4),
                    Y = yT + 4f,
                    VY = 10f + (float)(_rng.NextDouble() * 20),
                    Life = 1f,
                });
            }

            StepMotes(_splash, dt, 160f, yB);
            StepMotes(_spill, dt, 60f, yB);
        }

        private static void StepMotes(List<Mote> motes, double dt, float gravity, float yFloor)
        {
            for (int i = motes.Count - 1; i >= 0; i--)
            {
                var m = motes[i];
                m.Life -= (float)dt;
                if (m.Life <= 0 || m.Y > yFloor) { motes.RemoveAt(i); continue; }
                m.X += m.VX * (float)dt;
                m.Y += m.VY * (float)dt;
                m.VY += gravity * (float)dt;
                motes[i] = m;
            }
        }

        // ================================= paint =================================

        private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            var info = e.Info;
            if (info.Width <= 0 || info.Height <= 0) return;

            double dipW = ActualWidth, dipH = ActualHeight;
            if (dipW <= 1 || dipH <= 1) return;

            try
            {
                // Work in DIPs the way the mockup works in CSS pixels; the DPI scale
                // is a canvas transform, exactly as its ctx.setTransform(dpr,...) is.
                float scale = (float)(info.Width / dipW);
                canvas.Save();
                canvas.Scale(scale);
                Draw(canvas, (float)dipW, (float)dipH);
                canvas.Restore();
            }
            catch (Exception ex)
            {
                _faults++;
                App.Logger?.Debug("VatGlassCanvas.OnPaintSurface: {E}", ex.Message);
            }
        }

        private void Draw(SKCanvas canvas, float w, float h)
        {
            float x0 = w * JarX0, x1 = w * JarX1;
            float yT = h * JarYTop, yB = h * JarYBottom;
            float ySurf = YFor(yT, yB, _fill);
            bool animated = Animated;
            bool pouring = _pourT > 0 && animated;

            using var paint = new SKPaint { IsAntialias = true };

            // ---- faucet layout: slides in from the LEFT, spout parks over the jar
            var faucet = LoadFaucet();
            SKRect? faucetRect = null;
            float fx = SpoutXAt(w, h, yT);
            if (faucet != null)
            {
                float ih = h * 0.21f;
                float iw = ih * faucet.Width / faucet.Height;
                float finalX = w * 0.52f - iw * SpoutX;
                float x = finalX - (float)(1 - _slide) * (finalX + iw + 12);
                faucetRect = SKRect.Create(x, yT - 3 - ih * SpoutY, iw, ih);
            }
            bool streamOn = pouring && (faucetRect is null || _slide > 0.88);

            using var jarPath = new SKPath();
            jarPath.AddRoundRect(new SKRoundRect(new SKRect(x0, yT, x1, yB), JarRadius, JarRadius));

            // ---- liquid, clipped to the jar --------------------------------------
            canvas.Save();
            canvas.ClipPath(jarPath, antialias: true);

            using var bodyPath = new SKPath();
            bodyPath.MoveTo(x0, ySurf + Wave(x0, animated));
            for (float x = x0; x <= x1; x += 4) bodyPath.LineTo(x, ySurf + Wave(x, animated));
            bodyPath.LineTo(x1, yB);
            bodyPath.LineTo(x0, yB);
            bodyPath.Close();
            paint.Style = SKPaintStyle.Fill;
            paint.Color = _liquid;
            canvas.DrawPath(bodyPath, paint);

            using var surfacePath = new SKPath();
            surfacePath.MoveTo(x0, ySurf + Wave(x0, animated));
            for (float x = x0; x <= x1; x += 4) surfacePath.LineTo(x, ySurf + Wave(x, animated));
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 2;
            paint.Color = _liquidEdge;
            using (var glow = SKImageFilter.CreateDropShadow(0, 0, 4, 4, _liquidEdge))
            {
                paint.ImageFilter = glow;
                canvas.DrawPath(surfacePath, paint);
                paint.ImageFilter = null;
            }

            if (animated && _bubbles.Count > 0)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1;
                paint.Color = _liquidEdge.WithAlpha(115);
                foreach (var b in _bubbles)
                {
                    float bx = b.X + (float)(Math.Sin(_now / 400 + b.Ph) * 2.2);
                    canvas.DrawCircle(bx, b.Y, b.R, paint);
                }
            }

            // foam when brimming
            if (_fill > 0.98 && animated)
            {
                paint.Style = SKPaintStyle.Fill;
                for (float x = x0 + 3; x < x1 - 3; x += 7)
                {
                    float fy = ySurf + Wave(x, animated) - 2
                             - (float)(Math.Abs(Math.Sin(x * 3.1 + _now / 600)) * 2.5);
                    double a = (0.35 + 0.25 * Math.Sin(x + _now / 500)) * 0.55;
                    paint.Color = new SKColor(255, 255, 255, (byte)(Math.Clamp(a, 0, 1) * 255));
                    canvas.DrawCircle(x, fy, 2.1f, paint);
                }
            }

            // the pour stream, falling from the spout
            if (streamOn)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 3.2f;
                paint.Color = _liquidEdge.WithAlpha(230);
                using (var glow = SKImageFilter.CreateDropShadow(0, 0, 4, 4, _liquidEdge))
                {
                    paint.ImageFilter = glow;
                    canvas.DrawLine(fx, yT - 4,
                        fx + (float)(Math.Sin(_now / 90) * 1.2), ySurf + Wave(fx, animated), paint);
                    paint.ImageFilter = null;
                }
            }

            paint.Style = SKPaintStyle.Fill;
            foreach (var s in _splash)
            {
                paint.Color = _liquidEdge.WithAlpha((byte)(Math.Clamp(s.Life, 0, 1) * 255));
                canvas.DrawCircle(s.X, s.Y, 1.3f, paint);
            }

            canvas.Restore();   // unclip

            // ---- glass ------------------------------------------------------------
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 2;
            paint.Color = new SKColor(255, 255, 255, 71);
            canvas.DrawPath(jarPath, paint);

            paint.StrokeWidth = 3;
            paint.Color = new SKColor(255, 255, 255, 92);
            canvas.DrawLine(x0 - 4, yT, x1 + 4, yT, paint);           // lip

            paint.Color = new SKColor(255, 255, 255, 26);
            canvas.DrawLine(x0 + 7, yT + 10, x0 + 7, yB - 10, paint);  // inner highlight

            // ---- the overflow lip, drawn as a band --------------------------------
            // The glass between the CAP line and the brim. It grows with the lip: at
            // 1.30 it is nearly half again the height it has at 1.20. Faint enough to
            // read as glass, never as a second liquid.
            float yCap = YFor(yT, yB, 1.0);
            float yBrim = YFor(yT, yB, Brim);
            if (yCap - yBrim > 1)
            {
                canvas.Save();
                canvas.ClipPath(jarPath, antialias: true);
                paint.Style = SKPaintStyle.Fill;
                paint.Color = _accent.WithAlpha(15);
                canvas.DrawRect(SKRect.Create(x0, yBrim, x1 - x0, yCap - yBrim), paint);
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1;
                paint.Color = _accent.WithAlpha(56);
                using (var dash = SKPathEffect.CreateDash(new[] { 3f, 4f }, 0))
                {
                    paint.PathEffect = dash;
                    canvas.DrawLine(x0, yBrim + 0.5f, x1, yBrim + 0.5f, paint);
                    paint.PathEffect = null;
                }
                canvas.Restore();
            }

            DrawTicks(canvas, paint, x1, yT, yB);

            // ---- faucet art, above the glass --------------------------------------
            if (faucet != null && faucetRect is { } rect && _slide > 0.02)
                canvas.DrawImage(faucet, rect);

            // ---- spill running down the OUTSIDE of the glass ----------------------
            paint.Style = SKPaintStyle.Fill;
            paint.PathEffect = null;
            foreach (var s in _spill)
            {
                paint.Color = _liquidEdge.WithAlpha((byte)(Math.Clamp(s.Life * 0.8, 0, 1) * 255));
                canvas.DrawCircle(s.X, s.Y, 1.4f, paint);
            }
        }

        /// <summary>
        /// Meter ticks: 20 banks the day, CAP, and the lip on top. On a no-lip jar
        /// the MAX tick is dropped rather than clamped up — it would land on the CAP
        /// line and label one y twice with two different names.
        /// </summary>
        private void DrawTicks(SKCanvas canvas, SKPaint paint, float x1, float yT, float yB)
        {
            using var text = new SKPaint
            {
                IsAntialias = true,
                TextSize = 7f,
                TextAlign = SKTextAlign.Left,
                FakeBoldText = true,
                Typeface = SKTypeface.FromFamilyName("Consolas") ?? SKTypeface.Default,
            };
            var metrics = text.FontMetrics;
            float middle = -(metrics.Ascent + metrics.Descent) / 2f;

            DrawTick(0.2, "20");
            DrawTick(1.0, "CAP");
            if (HasLip) DrawTick(Brim, "MAX");

            void DrawTick(double f, string label)
            {
                float y = YFor(yT, yB, f);
                bool hit = _fill >= f;

                paint.Style = SKPaintStyle.Stroke;
                paint.PathEffect = null;
                paint.StrokeWidth = hit ? 2 : 1;
                paint.Color = hit ? _accent.WithAlpha(242) : new SKColor(255, 255, 255, 102);
                canvas.DrawLine(x1 - 7, y, x1, y, paint);

                text.Color = hit ? _accent.WithAlpha(242) : new SKColor(255, 255, 255, 115);
                canvas.DrawText(label, x1 + 3, y + middle, text);
            }
        }

        /// <summary>
        /// Fill fraction to liquid-surface y. THE LIP IS THE TOP OF THE SCALE, so a
        /// raised lip pushes the CAP line DOWN the glass and the band above it grows —
        /// that widening band is the whole visual of a deeper subject's taller lip.
        /// </summary>
        private float YFor(float yT, float yB, double f)
            => (float)((yB - 3) - ((yB - 3) - (yT + 5)) * (Math.Min(f, Ceiling) / Lip));

        private float Wave(float x, bool animated)
            => animated
                ? (float)(2.0 * Math.Sin(x / 16 + _now / 480) + 1.3 * Math.Sin(x / 8.5 - _now / 300))
                : 0f;

        /// <summary>Where the stream falls: the spout tip, or 55% of the box with no art.</summary>
        private float SpoutXAt(float w, float h, float yT)
        {
            var faucet = LoadFaucet();
            if (faucet == null) return w * 0.55f;
            float ih = h * 0.21f;
            float iw = ih * faucet.Width / faucet.Height;
            float finalX = w * 0.52f - iw * SpoutX;
            float x = finalX - (float)(1 - _slide) * (finalX + iw + 12);
            return x + iw * SpoutX;
        }

        // ================================ palette ================================

        /// <summary>
        /// The mod accent, exactly as the mockup's VAT_APP derives it: the liquid is
        /// the accent at 48%, the surface line and glow are the light accent, the
        /// ticks are the accent. Falls back to the mockup literals so a mod with no
        /// theme cannot blank the jar.
        /// </summary>
        private void ReadPalette()
        {
            var accent = new SKColor(0xFF, 0x69, 0xB4);
            var light = new SKColor(0xFF, 0x9C, 0xCF);
            try
            {
                accent = ParseHex(App.Mods?.GetAccentColorHex(), accent);
                light = ParseHex(App.Mods?.GetAccentLightColorHex(), light);
            }
            catch { /* palette is decoration; the fallbacks above are the mockup's own */ }

            _accent = accent;
            _liquid = accent.WithAlpha(122);   // 0.48
            _liquidEdge = light;
        }

        private static SKColor ParseHex(string? hex, SKColor fallback)
            => !string.IsNullOrWhiteSpace(hex) && SKColor.TryParse(hex, out var c) ? c : fallback;

        /// <summary>
        /// The faucet art, decoded once for the whole app. A missing or unreadable
        /// resource degrades to a stream with no faucet rather than to no pour — the
        /// level is the information, the plumbing is the theater.
        /// </summary>
        private static SKImage? LoadFaucet()
        {
            if (_faucet != null || _faucetTried) return _faucet;
            lock (FaucetLock)
            {
                if (_faucet != null || _faucetTried) return _faucet;
                _faucetTried = true;
                try
                {
                    var uri = new Uri("pack://application:,,,/Resources/descent/faucet.png", UriKind.Absolute);
                    var info = Application.GetResourceStream(uri);
                    if (info?.Stream == null) return null;
                    using var stream = info.Stream;
                    using var bitmap = SKBitmap.Decode(stream);
                    if (bitmap == null) return null;
                    _faucet = SKImage.FromBitmap(bitmap);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("VatGlassCanvas: faucet art unavailable: {E}", ex.Message);
                }
                return _faucet;
            }
        }
    }
}
