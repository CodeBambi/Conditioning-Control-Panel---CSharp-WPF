using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls
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
    /// PORTED from ConditioningControlPanel/Controls/VatGlassCanvas.cs. The WPF
    /// original is a <c>Decorator</c> hosting an <c>SKElement</c> child and paints in
    /// its PaintSurface handler; this one is an Avalonia <c>Decorator</c> that paints
    /// in <see cref="Render"/> through Avalonia's own DrawingContext, so there is no
    /// Skia child surface and no SkiaSharp API involved — Avalonia composites on Skia
    /// already and clips, fades and scrolls this drawing like any other control,
    /// which is the whole reason the WPF file chose SKElement over a WebView2 HWND.
    ///
    /// THE PORTRAIT IS NOT DRAWN HERE. A real AdornedAvatar sits underneath (the web
    /// does the same via skipPortrait) so the Discord CDN, the OG ring and the
    /// wardrobe decoration stay in one place and none of them enter this canvas.
    /// </summary>
    public sealed class VatGlassCanvas : Decorator
    {
        // ------------------------------------------------------------- geometry
        // Fractions of the control box, exactly as the mockup has them, so the host
        // picks the size and nothing here needs to know what it chose.

        private const double JarX0 = 0.10;
        private const double JarX1 = 0.90;
        private const double JarYTop = 0.175;
        private const double JarYBottom = 0.955;
        private const double JarRadius = 9;

        /// <summary>Portrait diameter as a fraction of the whole box: 2 x 0.30 x (0.90 - 0.10).</summary>
        public const double PortraitDiameterRatio = 2 * 0.30 * (JarX1 - JarX0);

        /// <summary>Portrait centre as a fraction of the box height (the mockup's .vatwrap value).</summary>
        public const double PortraitCenterYRatio = 0.565;

        /// <summary>The aspect the mockup locks the jar to (152 x 200).</summary>
        public const double JarAspect = 200.0 / 152.0;

        /// <summary>Spout tip inside the faucet art, as a fraction of its box.</summary>
        private const double SpoutX = 0.906;
        private const double SpoutY = 0.989;

        /// <summary>Seconds one pour runs for — covers the slide-in and a good pour.</summary>
        public const double PourSeconds = 2.1;

        /// <summary>The base overflow lip, used until the server names this account's.</summary>
        private const double BaseLip = 1.20;

        private const int FaultLimit = 5;

        // --------------------------------------------------------------- surface

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

        private struct Bubble { public double X, Y, R, V, Ph; }
        private struct Mote { public double X, Y, VX, VY, Life; }

        private readonly List<Bubble> _bubbles = new();
        private readonly List<Mote> _splash = new();
        private readonly List<Mote> _spill = new();

        // ------------------------------------------------------------- the scale
        // LIP, BRIM and CEIL are the meter's scale, resolved from the server's
        // fill_lip_pct. A LIP OF 1.0 IS THE LEGAL "NO LIP" JAR: no band, no MAX
        // tick, nothing above the cap to spill.

        private double _rawLip = BaseLip;

        /// <summary>
        /// True while the pour in flight was asked for by a USER GESTURE (the
        /// CHARGE-HOLD on the Trainer Card's tap) rather than by an ambient reading.
        ///
        /// A user gesture OUTRANKS THE AMBIENT CAP (owner call 2026-08-30, House Book
        /// Law VIII "answer in 100ms" and Deck IV "a verb press with no sensory echo
        /// is a broken slot handle"). The vat's ambient clock is gated on
        /// MotionFx.AllowAmbientLoops, which flips to false the moment sixteen flash
        /// windows push the perf tier to Performance — i.e. exactly when XP is
        /// flowing. Before this flag, the 2.1s pour therefore collapsed to a silent
        /// snap precisely in the moment it was supposed to celebrate.
        ///
        /// It keeps the clock alive for the pour's own duration, and nothing else:
        /// the wave, the bubbles, the foam and the wobble all still read
        /// <see cref="Animated"/> and still drop out on a loaded machine.
        /// </summary>
        private bool _gesturePour;

        /// <summary>
        /// Reduced motion's pour: Law VI collapses every move to a &lt;=120ms change
        /// and lets the SOUND carry the beat. It is still a pour and still lands on
        /// the number — it just does not perform.
        /// </summary>
        private const double ReducedPourSeconds = 0.12;

        /// <summary>Seconds of BRIGHTER spill remaining — see <see cref="PulseOverflow"/>.</summary>
        private double _spillBoost;

        private bool HasLip => _rawLip > 1.0;
        private double Lip => HasLip ? Math.Max(1.02, _rawLip) : 1.0;
        private double Brim => HasLip ? Lip - 0.02 : 1.0;
        private double Ceiling => HasLip ? Lip + 0.02 : 1.0;

        // --------------------------------------------------------------- palette

        private Color _liquid, _liquidEdge, _accent;

        public VatGlassCanvas()
        {
            IsHitTestVisible = false;

            // Background priority, same as AmbientFxCanvas: a decorative meter yields
            // to input and layout, it never competes with them.
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => Tick();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

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
            catch (Exception ex) { Log.Debug("VatGlassCanvas.FillPercentChanged: {E}", ex.Message); }
        }

        /// <summary>True while the faucet is in and pouring.</summary>
        public bool IsPouring => _pourT > 0;

        // ponytail: needs MotionFx (perf tier + prefers-reduced-motion), wired when it moves to
        // Core. Until then the vat draws its Full-motion self — waves, bubbles, foam and the 2.1s
        // pour all run — which is the WPF behaviour on an unloaded machine.
        /// <summary>
        /// Reduced motion is the desktop's prefers-reduced-motion: waves, bubbles,
        /// the faucet, splash and spill all drop out and the level snaps instead of
        /// easing. The vat itself never disappears — a user who turned animation off
        /// did not ask to stop seeing today's XP.
        /// </summary>
        private static bool Animated => true;

        /// <summary>MotionFx.Level == MotionLevel.Full. See <see cref="Animated"/>.</summary>
        private static bool FullMotion => true;

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
            InvalidateVisual();
        }

        /// <summary>Snap to a level with no theater — the first server read of a session.</summary>
        public void Seed(double fill) => SnapTo(fill);

        /// <summary>
        /// Set the level at once, with no ease and no faucet, and drop any pour in
        /// flight. Two callers, both of which are re-scalings rather than readings:
        /// the session's first read (<see cref="Seed"/>), and a cap or lip change —
        /// a different meter, not a different amount of liquid.
        /// </summary>
        public void SnapTo(double fill)
        {
            _target = Clamp(fill);
            _fill = _target;
            _pourT = 0;
            _slide = 0;
            NotifyPercent();
            InvalidateVisual();
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
            InvalidateVisual();
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
        ///
        /// <para><paramref name="userGesture"/> is the CHARGE-HOLD's pour and it
        /// OUTRANKS THE AMBIENT CAP (see <see cref="_gesturePour"/>): it animates on
        /// every performance tier, and only reduced motion shortens it — to
        /// <see cref="ReducedPourSeconds"/>, never to a silent snap. Ambient pours
        /// (the mid-pour extension, a reading that arrived on its own) keep the old
        /// behaviour exactly.</para>
        /// </summary>
        public void PourTo(double fill, bool userGesture = false)
        {
            _target = Clamp(fill);

            if (userGesture)
            {
                // Law VI: reduced motion collapses the move, the sound still carries
                // the beat (the host plays faucet_pour.wav either way).
                _pourT = FullMotion ? PourSeconds : ReducedPourSeconds;
                _gesturePour = true;
                InvalidateVisual();
                Evaluate();
                return;
            }

            if (!Animated) { _fill = _target; NotifyPercent(); InvalidateVisual(); return; }
            _pourT = PourSeconds;
            InvalidateVisual();
            Evaluate();
        }

        // ------------------------------------------------------------ external spout
        // The Trainer Card's interactive faucet (MainWindow.ProfileFaucet) is a XAML
        // element perched on the jar's top-left lip; when it owns the plumbing the
        // canvas must not draw its own slide-in faucet art, and the stream has to
        // fall from the XAML spout instead of the art's tip. The slide value is kept
        // as a ~150ms stream-on delay so the stream starts as the faucet finishes
        // its tilt rather than a frame before it.

        private double? _externalSpoutX;

        /// <summary>
        /// When set (0..1, a fraction of the control width), pours draw no internal
        /// faucet art and the stream falls from this x instead — the host's own
        /// faucet element is the visible plumbing. Null restores the built-in art.
        /// </summary>
        public double? ExternalSpoutXFraction
        {
            get => _externalSpoutX;
            set
            {
                _externalSpoutX = value is double d && double.IsFinite(d) ? Math.Clamp(d, 0, 1) : null;
                InvalidateVisual();
            }
        }

        /// <summary>
        /// THE JACKPOT LADDER, minor rung: a pour that carries the level PAST THE
        /// BRIM gets a thicker, brighter spill down the outside of the glass for a
        /// couple of seconds. The host calls this on the same frame it layers the
        /// extra chime (MainWindow.ProfileFaucet), so sight and sound land together
        /// (Law X).
        ///
        /// Deliberately a PULSE and not a state: the overflow theater itself is
        /// permanent while the level sits past the brim (an account parked at MAX
        /// spills all day), and what this marks is the MOMENT of crossing.
        /// </summary>
        public void PulseOverflow(double seconds = 1.8)
        {
            if (!double.IsFinite(seconds) || seconds <= 0) return;
            _spillBoost = Math.Max(_spillBoost, seconds);
            InvalidateVisual();
            Evaluate();
        }

        /// <summary>Is this level past the overflow brim? The host asks before pouring, so
        /// it can tell a pour that CROSSES the lip from one that was already over it.</summary>
        public bool IsPastBrim(double fill) => HasLip && fill >= Brim;

        /// <summary>
        /// The three marks the meter draws down its right wall, named so a host can
        /// hang words on them.
        /// </summary>
        public enum VatTickMark
        {
            /// <summary>The 20% line - the "you have banked a day" mark.</summary>
            Drain,

            /// <summary>The daily cap.</summary>
            Cap,

            /// <summary>The overflow brim. Absent on a jar with no lip.</summary>
            Max,
        }

        /// <summary>
        /// Where a tick line sits, in this control's own coordinates, or null when
        /// the mark is not drawn (MAX on a lipless jar) or the control has no size
        /// yet.
        ///
        /// EXISTS SO THE LEGEND CANNOT DRIFT. The tick glyphs on the Trainer Card are
        /// real controls (they carry tooltips; a canvas-drawn label cannot), so they
        /// are positioned by the host - and a host doing that arithmetic itself would
        /// be a second copy of <see cref="YFor"/> to keep in step with the lip, the
        /// cap and the 5px/3px insets. This answers from the one copy.
        /// </summary>
        public double? TickCenterY(VatTickMark mark)
        {
            double h = Bounds.Height;
            if (!(h > 0)) return null;

            double f = mark switch
            {
                VatTickMark.Drain => 0.2,
                VatTickMark.Cap => 1.0,
                VatTickMark.Max => HasLip ? Brim : double.NaN,
                _ => double.NaN,
            };
            if (double.IsNaN(f)) return null;

            double yT = h * JarYTop, yB = h * JarYBottom;
            return YFor(yT, yB, f);
        }

        /// <summary>
        /// The x of the tick lines' INNER end (they are drawn from x1-7 to x1, with
        /// their labels outside the wall). A legend glyph is hung just inside this.
        /// </summary>
        public double TickInnerX => Bounds.Width * JarX1 - 7;

        /// <summary>How far the slide must be before the stream/splash turn on.</summary>
        private double StreamOnSlide => _externalSpoutX is double ? 0.55 : 0.88;

        /// <summary>Re-read the mod accent (mod switch).</summary>
        public void RefreshPalette()
        {
            ReadPalette();
            InvalidateVisual();
        }

        private double Clamp(double f) => double.IsFinite(f) ? Math.Max(0, Math.Min(Ceiling, f)) : 0;

        // ============================== lifecycle ==============================

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                HookWindow(TopLevel.GetTopLevel(this) as Window);
                // ponytail: needs ModService.ModChanged to re-read the accent on a mod switch,
                // wired when it moves to Core. RefreshPalette() is public so a host can push it.
                ReadPalette();
                Evaluate();
            }
            catch (Exception ex) { Log.Debug("VatGlassCanvas.OnLoaded: {E}", ex.Message); }
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                StopClock();
                UnhookWindow();
            }
            catch (Exception ex) { Log.Debug("VatGlassCanvas.OnUnloaded: {E}", ex.Message); }
        }

        /// <summary>WPF's IsVisibleChanged. Avalonia routes it through the property system.</summary>
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
            // Avalonia has no StateChanged event; WindowState is a styled property, so the
            // minimise half of WindowIsPresenting comes off the property system instead.
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
            if (e.Property == Window.WindowStateProperty || e.Property == IsVisibleProperty) Evaluate();
        }

        private void Evaluate()
        {
            try
            {
                if (ShouldRun()) StartClock();
                else StopClock();
            }
            catch (Exception ex) { Log.Debug("VatGlassCanvas.Evaluate: {E}", ex.Message); }
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
            // A USER-GESTURE POUR KEEPS ITS OWN CLOCK. Everything else in here is
            // ambient and still yields to the tier; this one beat does not, because
            // the user asked for it (owner call 2026-08-30).
            if (!Animated && !(_gesturePour && _pourT > 0)) return false;
            return IsPresenting;
        }

        /// <summary>
        /// "This surface is in front of a human right now" — everything ShouldRun
        /// tests EXCEPT the motion setting and the fault trip, so a caller that is
        /// not a renderer can share the definition. The vat's 60s network poll gates
        /// on this: reduced motion still wants a current reading, a minimised window
        /// does not.
        ///
        /// <para>IsEffectivelyVisible, not IsVisible: WPF's IsVisible is ancestor-aware, Avalonia's
        /// is the LOCAL property, so a collapsed parent - a tab that is not the selected one -
        /// would leave IsVisible true and the clock stepping motes at 30fps behind a hidden
        /// tab.</para>
        /// </summary>
        // ponytail: Avalonia 12 keeps IsEffectivelyVisibleChanged internal, so there is no
        // ancestor-visibility event to gate on and OnPropertyChanged sees only the LOCAL
        // IsVisible. The clock therefore stops one tick (33ms) after an ancestor hides, on
        // Tick's own ShouldRun check, and restarts on Loaded or the next reading rather than
        // the instant the ancestor comes back. Subscribe to the event if Avalonia makes it public.
        public bool IsPresenting => IsLoaded && IsEffectivelyVisible && WindowIsPresenting(_window);

        /// <summary>
        /// The window half of the gate, in ONE place so the renderer's clock and the
        /// host's poll cannot drift apart. A null window means "not parented yet" and
        /// is left to the caller's own IsLoaded/IsVisible test rather than answering
        /// false here.
        /// </summary>
        public static bool WindowIsPresenting(Window? window)
        {
            if (window == null) return true;
            if (window.WindowState == WindowState.Minimized) return false;
            if (!window.IsVisible) return false;
            if (!window.IsActive) return false;
            return true;
        }

        private void StartClock()
        {
            if (_timer.IsEnabled) return;
            if (!_clock.IsRunning) _clock.Start();
            _lastTickMs = _clock.Elapsed.TotalMilliseconds;
            _timer.Start();
        }

        /// <summary>
        /// NO STALE FAUCET. Stopping the clock SETTLES any pour in flight instead of
        /// freezing it mid-swing: the level lands on its target and the spout is put
        /// away. Without this the faucet's remaining seconds sit in <see cref="_pourT"/>
        /// for as long as the window is minimised or the tab is elsewhere, and then
        /// replay — a pour announcing XP earned minutes ago, the moment the user comes
        /// back. The reading is never lost, only its theater.
        /// </summary>
        private void StopClock()
        {
            if (_timer.IsEnabled) _timer.Stop();
            _gesturePour = false;
            if (_pourT <= 0 && _slide <= 0) return;
            _pourT = 0;
            _slide = 0;
            _fill = _target;
            _splash.Clear();   // otherwise a frozen droplet hangs in mid-air
            NotifyPercent();
            InvalidateVisual();
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
                InvalidateVisual();

                // The gesture's exemption lasts exactly as long as its pour. Dropped
                // here rather than in Step so a paused/parked clock cannot leave the
                // ambient cap permanently lifted.
                if (_gesturePour && _pourT <= 0) { _gesturePour = false; Evaluate(); }
            }
            catch (Exception ex)
            {
                _faults++;
                Log.Warning("VatGlassCanvas tick failed ({N}/{Max}): {E}", _faults, FaultLimit, ex.Message);
                if (_faults >= FaultLimit)
                {
                    Log.Warning("VatGlassCanvas: stopping after repeated faults");
                    StopClock();
                }
            }
        }

        /// <summary>Advance the simulation. State only — Render() paints, it never steps.</summary>
        private void Step(double dt)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 1 || h <= 1) return;

            double x0 = w * JarX0, x1 = w * JarX1;
            double yT = h * JarYTop, yB = h * JarYBottom;

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

            double ySurf = YFor(yT, yB, _fill);

            // The overflow's extra brightness burns down on the same clock as
            // everything else, so a spill that started on a gesture pour cannot stay
            // lit after the pour is over.
            if (_spillBoost > 0) _spillBoost = Math.Max(0, _spillBoost - dt);

            // ---- bubbles. Ambient garnish: skipped entirely when the clock is only
            // running because a user gesture lifted the cap (see _gesturePour).
            bool ambient = Animated;
            double rate = (_pourT > 0 ? 0.5 : 0.12) * (dt * 60.0);
            if (ambient && _rng.NextDouble() < rate)
            {
                _bubbles.Add(new Bubble
                {
                    X = x0 + 6 + _rng.NextDouble() * (x1 - x0 - 12),
                    Y = yB - 4,
                    R = 0.8 + _rng.NextDouble() * 2.2,
                    V = 12 + _rng.NextDouble() * 16,
                    Ph = _rng.NextDouble() * Math.PI * 2,
                });
            }
            for (int i = _bubbles.Count - 1; i >= 0; i--)
            {
                var b = _bubbles[i];
                b.Y -= b.V * dt;
                double bx = b.X + Math.Sin(_now / 400 + b.Ph) * 2.2;
                if (b.Y < ySurf + Wave(bx, true) + 2) { _bubbles.RemoveAt(i); continue; }
                _bubbles[i] = b;
            }

            // ---- splash from the stream landing
            if (_pourT > 0 && _slide > StreamOnSlide)
            {
                double fx = SpoutXAt(w, h);
                if (_rng.NextDouble() < 0.7 * (dt * 60.0))
                {
                    _splash.Add(new Mote
                    {
                        X = fx,
                        Y = ySurf + Wave(fx, true),
                        VX = (_rng.NextDouble() - 0.5) * 40,
                        VY = -(20 + _rng.NextDouble() * 40),
                        Life = 0.6,
                    });
                }
            }

            // ---- OVERFLOW THEATER IS NOT GATED BY THE POUR THRESHOLD. An account
            // parked past the brim spills down the outside of the glass whether or
            // not the last grant was big enough to swing the faucet in.
            //
            // THE JACKPOT LADDER, minor rung (owner-approved 2026-08-30): while
            // _spillBoost is lit — a pour that crossed the brim — the spill runs
            // roughly twice as thick and brighter (see Draw), and the host layers one
            // extra chime over the pour clip. Rarity tracks the ceremony: crossing
            // the brim is a minor, and it costs one boolean.
            double spillRate = (_spillBoost > 0 ? 1.1 : 0.5) * (dt * 60.0);
            if (HasLip && _fill >= Brim && _rng.NextDouble() < spillRate)
            {
                double side = _rng.NextDouble() < 0.5 ? x0 : x1;
                _spill.Add(new Mote
                {
                    X = side + (_rng.NextDouble() - 0.5) * 4,
                    Y = yT + 4,
                    VY = 10 + _rng.NextDouble() * 20,
                    Life = 1,
                });
            }

            StepMotes(_splash, dt, 160, yB);
            StepMotes(_spill, dt, 60, yB);
        }

        private static void StepMotes(List<Mote> motes, double dt, double gravity, double yFloor)
        {
            for (int i = motes.Count - 1; i >= 0; i--)
            {
                var m = motes[i];
                m.Life -= dt;
                if (m.Life <= 0 || m.Y > yFloor) { motes.RemoveAt(i); continue; }
                m.X += m.VX * dt;
                m.Y += m.VY * dt;
                m.VY += gravity * dt;
                motes[i] = m;
            }
        }

        // ================================= paint =================================

        /// <summary>
        /// The WPF file's OnPaintSurface and Draw, merged. Avalonia's DrawingContext
        /// is already in DIPs with the DPI scale applied by the compositor, so the
        /// SKElement's <c>canvas.Scale(info.Width / ActualWidth)</c> transform has no
        /// counterpart here and is dropped rather than reproduced.
        /// </summary>
        public override void Render(DrawingContext context)
        {
            base.Render(context);

            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 1 || h <= 1) return;

            try { Draw(context, w, h); }
            catch (Exception ex)
            {
                _faults++;
                Log.Debug("VatGlassCanvas.Render: {E}", ex.Message);
            }
        }

        private void Draw(DrawingContext context, double w, double h)
        {
            double x0 = w * JarX0, x1 = w * JarX1;
            double yT = h * JarYTop, yB = h * JarYBottom;
            double ySurf = YFor(yT, yB, _fill);
            bool animated = Animated;
            // A user-gesture pour draws its stream on any tier: the ambient cap does
            // not get to silence the one thing the user actually pressed for.
            bool pouring = _pourT > 0 && (animated || _gesturePour);

            // ---- faucet layout: slides in from the LEFT, spout parks over the jar.
            // With an external spout the host's own faucet IS the plumbing: no art
            // here, and the stream-on delay rides the same slide ramp.
            var faucet = _externalSpoutX is null ? LoadFaucet() : null;
            Rect? faucetRect = null;
            double fx = SpoutXAt(w, h);
            if (faucet != null)
            {
                double ih = h * 0.21;
                double iw = ih * faucet.Size.Width / faucet.Size.Height;
                double finalX = w * 0.52 - iw * SpoutX;
                double x = finalX - (1 - _slide) * (finalX + iw + 12);
                faucetRect = new Rect(x, yT - 3 - ih * SpoutY, iw, ih);
            }
            bool streamOn = pouring
                && (_externalSpoutX is double
                        ? _slide > StreamOnSlide
                        : faucetRect is null || _slide > 0.88);

            var jarRect = new RoundedRect(new Rect(x0, yT, x1 - x0, yB - yT), JarRadius);

            // ---- liquid, clipped to the jar --------------------------------------
            using (context.PushClip(jarRect))
            {
                var body = new StreamGeometry();
                var surface = new StreamGeometry();
                using (var bc = body.Open())
                using (var sc = surface.Open())
                {
                    var first = new Point(x0, ySurf + Wave(x0, animated));
                    bc.BeginFigure(first, isFilled: true);
                    sc.BeginFigure(first, isFilled: false);
                    for (double x = x0; x <= x1; x += 4)
                    {
                        var p = new Point(x, ySurf + Wave(x, animated));
                        bc.LineTo(p);
                        sc.LineTo(p);
                    }
                    bc.LineTo(new Point(x1, yB));
                    bc.LineTo(new Point(x0, yB));
                    bc.EndFigure(true);
                    sc.EndFigure(false);
                }

                context.DrawGeometry(new ImmutableSolidColorBrush(_liquid), null, body);

                // ponytail: the WPF file glows the surface line with
                // SKImageFilter.CreateDropShadow(0,0,4,4,edge). Avalonia's DrawingContext has no
                // per-draw blur, so the glow is a wide translucent under-stroke; swap it for a
                // real blur if a shared blur effect ever lands in the head.
                context.DrawGeometry(null, new ImmutablePen(new ImmutableSolidColorBrush(_liquidEdge, 0.30), 7), surface);
                context.DrawGeometry(null, new ImmutablePen(new ImmutableSolidColorBrush(_liquidEdge), 2), surface);

                if (animated && _bubbles.Count > 0)
                {
                    var bubblePen = new ImmutablePen(new ImmutableSolidColorBrush(WithA(_liquidEdge, 115)), 1);
                    foreach (var b in _bubbles)
                    {
                        double bx = b.X + Math.Sin(_now / 400 + b.Ph) * 2.2;
                        context.DrawEllipse(null, bubblePen, new Point(bx, b.Y), b.R, b.R);
                    }
                }

                // foam when brimming
                if (_fill > 0.98 && animated)
                {
                    for (double x = x0 + 3; x < x1 - 3; x += 7)
                    {
                        double fy = ySurf + Wave(x, animated) - 2
                                  - Math.Abs(Math.Sin(x * 3.1 + _now / 600)) * 2.5;
                        double a = (0.35 + 0.25 * Math.Sin(x + _now / 500)) * 0.55;
                        var foam = new ImmutableSolidColorBrush(
                            Color.FromArgb((byte)(Math.Clamp(a, 0, 1) * 255), 255, 255, 255));
                        context.DrawEllipse(foam, null, new Point(x, fy), 2.1, 2.1);
                    }
                }

                // the pour stream, falling from the spout
                if (streamOn)
                {
                    var from = new Point(fx, yT - 4);
                    var to = new Point(fx + Math.Sin(_now / 90) * 1.2, ySurf + Wave(fx, animated));
                    context.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(_liquidEdge, 0.30), 8.5), from, to);
                    context.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(WithA(_liquidEdge, 230)), 3.2), from, to);
                }

                foreach (var s in _splash)
                {
                    var drop = new ImmutableSolidColorBrush(WithA(_liquidEdge, (byte)(Math.Clamp(s.Life, 0, 1) * 255)));
                    context.DrawEllipse(drop, null, new Point(s.X, s.Y), 1.3, 1.3);
                }
            }   // unclip

            // ---- glass ------------------------------------------------------------
            context.DrawRectangle(null,
                new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(71, 255, 255, 255)), 2), jarRect);

            context.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(92, 255, 255, 255)), 3),
                new Point(x0 - 4, yT), new Point(x1 + 4, yT));            // lip

            context.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(26, 255, 255, 255)), 3),
                new Point(x0 + 7, yT + 10), new Point(x0 + 7, yB - 10));  // inner highlight

            // ---- the overflow lip, drawn as a band --------------------------------
            // The glass between the CAP line and the brim. It grows with the lip: at
            // 1.30 it is nearly half again the height it has at 1.20. Faint enough to
            // read as glass, never as a second liquid.
            double yCap = YFor(yT, yB, 1.0);
            double yBrim = YFor(yT, yB, Brim);
            if (yCap - yBrim > 1)
            {
                using (context.PushClip(jarRect))
                {
                    context.FillRectangle(new ImmutableSolidColorBrush(WithA(_accent, 15)),
                        new Rect(x0, yBrim, x1 - x0, yCap - yBrim));
                    var dashed = new ImmutablePen(new ImmutableSolidColorBrush(WithA(_accent, 56)), 1,
                        new ImmutableDashStyle(new double[] { 3, 4 }, 0));
                    context.DrawLine(dashed, new Point(x0, yBrim + 0.5), new Point(x1, yBrim + 0.5));
                }
            }

            DrawTicks(context, x1, yT, yB);

            // ---- faucet art, above the glass --------------------------------------
            if (faucet != null && faucetRect is { } rect && _slide > 0.02)
                context.DrawImage(faucet, rect);

            // ---- spill running down the OUTSIDE of the glass ----------------------
            double spillAlpha = _spillBoost > 0 ? 1.0 : 0.8;   // the minor jackpot's brighter spill
            double spillR = _spillBoost > 0 ? 1.8 : 1.4;
            foreach (var s in _spill)
            {
                var drop = new ImmutableSolidColorBrush(WithA(_liquidEdge, (byte)(Math.Clamp(s.Life * spillAlpha, 0, 1) * 255)));
                context.DrawEllipse(drop, null, new Point(s.X, s.Y), spillR, spillR);
            }
        }

        /// <summary>
        /// Meter ticks: 20 banks the day, CAP, and the lip on top. On a no-lip jar
        /// the MAX tick is dropped rather than clamped up — it would land on the CAP
        /// line and label one y twice with two different names.
        /// </summary>
        private void DrawTicks(DrawingContext context, double x1, double yT, double yB)
        {
            DrawTick(0.2, "20");
            DrawTick(1.0, "CAP");
            if (HasLip) DrawTick(Brim, "MAX");

            void DrawTick(double f, string label)
            {
                double y = YFor(yT, yB, f);
                bool hit = _fill >= f;

                var lineColor = hit ? WithA(_accent, 242) : Color.FromArgb(102, 255, 255, 255);
                context.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(lineColor), hit ? 2 : 1),
                    new Point(x1 - 7, y), new Point(x1, y));

                // Avalonia's DrawText takes the TOP-LEFT of the run where Skia's takes the
                // baseline, so the WPF file's -(ascent + descent) / 2 becomes half the
                // formatted height.
                var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(TickFont, FontStyle.Normal, FontWeight.Bold), 7,
                    new ImmutableSolidColorBrush(hit ? WithA(_accent, 242) : Color.FromArgb(115, 255, 255, 255)));
                context.DrawText(text, new Point(x1 + 3, y - text.Height / 2));
            }
        }

        /// <summary>The mockup's tick face. A machine without it falls back to the default.</summary>
        private static readonly FontFamily TickFont = new("Consolas");

        /// <summary>
        /// Fill fraction to liquid-surface y. THE LIP IS THE TOP OF THE SCALE, so a
        /// raised lip pushes the CAP line DOWN the glass and the band above it grows —
        /// that widening band is the whole visual of a deeper subject's taller lip.
        /// </summary>
        private double YFor(double yT, double yB, double f)
            => (yB - 3) - ((yB - 3) - (yT + 5)) * (Math.Min(f, Ceiling) / Lip);

        private double Wave(double x, bool animated)
            => animated
                ? 2.0 * Math.Sin(x / 16 + _now / 480) + 1.3 * Math.Sin(x / 8.5 - _now / 300)
                : 0;

        /// <summary>Where the stream falls: the external spout when the host owns the
        /// plumbing, else the art's spout tip, else 55% of the box with no art.</summary>
        private double SpoutXAt(double w, double h)
        {
            if (_externalSpoutX is double ext) return w * ext;
            var faucet = LoadFaucet();
            if (faucet == null) return w * 0.55;
            double ih = h * 0.21;
            double iw = ih * faucet.Size.Width / faucet.Size.Height;
            double finalX = w * 0.52 - iw * SpoutX;
            double x = finalX - (1 - _slide) * (finalX + iw + 12);
            return x + iw * SpoutX;
        }

        // ================================ palette ================================

        private static Color WithA(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        /// <summary>
        /// The mod accent, exactly as the mockup's VAT_APP derives it: the liquid is
        /// the accent at 48%, the surface line and glow are the light accent, the
        /// ticks are the accent. Falls back to the mockup literals so a mod with no
        /// theme cannot blank the jar.
        /// </summary>
        private void ReadPalette()
        {
            var accent = Color.FromRgb(0xFF, 0x69, 0xB4);
            var light = Color.FromRgb(0xFF, 0x9C, 0xCF);
            try
            {
                accent = ParseHex(AccentHex, accent);
                light = ParseHex(AccentLightHex, light);
            }
            catch { /* palette is decoration; the fallbacks above are the mockup's own */ }

            _accent = accent;
            _liquid = WithA(accent, 122);   // 0.48
            _liquidEdge = light;
        }

        // ponytail: needs ModService (App.Mods.GetAccentColorHex / GetAccentLightColorHex), wired
        // when it moves to Core. Null keeps the mockup's own hot-pink literals, which is exactly
        // what the WPF file falls back to for a mod that ships no theme.
        private static string? AccentHex => null;
        private static string? AccentLightHex => null;

        private static Color ParseHex(string? hex, Color fallback)
            => !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out var c) ? c : fallback;

        // ponytail: needs the faucet art (Resources/descent/faucet.png, a WPF pack:// resource not
        // yet in the Avalonia head's assets), wired when the descent art moves across. Null is the
        // WPF file's own degradation path — the stream falls from 55% of the box with no plumbing
        // drawn, because the level is the information and the faucet is only the theater.
        private static IImage? LoadFaucet() => null;
    }
}
