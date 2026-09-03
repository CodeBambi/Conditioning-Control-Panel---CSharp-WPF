using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls
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

        /// <summary>
        /// Share of the tier's particle budget the dust field is allowed to fill, 0-1. Default 1 is
        /// exactly the behaviour every caller had before this existed: the budget, in full.
        ///
        /// <para>This exists for surfaces whose particle density is itself expressive - the Programs
        /// tab's ignition curve spends it as "how far into the run am I" - and it only ever narrows
        /// the tier's budget, never widens it, so no caller can use it to buy frames back.</para>
        /// </summary>
        public double DustDensity { get; set; } = 1.0;

        /// <summary>
        /// Overrides the mod palette for this surface's particle and glow layers. Null (the default)
        /// keeps <c>FxTheme</c>'s colours, which is what every ambient surface in the app wants. Set
        /// it only where the surface has its own authored accent that would otherwise clash with the
        /// mod's - a program's AccentColor is author-supplied and is the identity the rest of that
        /// panel is already painted in.
        /// </summary>
        public Color? Tint { get; set; }
    }

    /// <summary>
    /// PORTED from ConditioningControlPanel/Controls/AmbientFxCanvas.cs.
    ///
    /// <para>The one reusable in-window FX surface: a hit-test-invisible canvas running a
    /// self-stopping ~30fps <see cref="DispatcherTimer"/>, composed from the layer vocabulary in
    /// <see cref="AmbientFxLayers"/>. Deliberately NOT the fullscreen compositor - that is
    /// per-monitor topmost overlay windows, and keeping its shared tick alive for ambient loops
    /// would undo the idle-parking that fixed #550. This control spawns no window of any kind.</para>
    ///
    /// Rules it enforces for every caller:
    ///   • the clock only runs while the control is loaded, visible, and its window is active and
    ///     not minimized - the timer stops itself the moment any of that stops holding;
    ///   • colours and the performance budget are read ONCE at (re)start and cached, never per tick;
    ///     a mod switch re-reads them via <see cref="RefreshPalette"/>;
    ///   • nothing is allocated per frame - the radial brushes are rebuilt only when the palette
    ///     moves, and per-particle alpha rides <c>PushOpacity</c> rather than a new brush;
    ///   • every tick is wrapped, and repeated faults stop the clock instead of spamming the log.
    ///
    /// <para>All simulation state is in element-normalized (0-1) coordinates, so it is DPI- and
    /// Viewbox-agnostic, while the two one-shot entry points (<see cref="Burst"/> and
    /// <see cref="BankTokens"/>) take plain element-local coordinates.</para>
    ///
    /// <para><b>What changed in the port.</b> The WPF original hosted a SkiaSharp <c>SKElement</c>
    /// as its <c>Decorator.Child</c> and painted every layer as one white radial <c>SKImage</c>
    /// re-tinted by an <c>SKColorFilter</c> under <c>SKBlendMode.Plus</c>. Avalonia's
    /// <see cref="DrawingContext"/> draws the same shapes natively - a <see cref="RadialGradientBrush"/>
    /// stretched over an ellipse IS the soft sprite - so the port drops SkiaSharp entirely and
    /// overrides <see cref="Render"/> on the control itself, keeping the head free of a Skia
    /// dependency it otherwise does not need.</para>
    ///
    /// <para>ponytail: Avalonia's DrawingContext has no additive blend mode, so the layers composite
    /// source-over where WPF composited Plus. Overlapping puffs therefore read a shade flatter
    /// instead of blooming. The upgrade path is an <c>ICustomDrawOperation</c> holding an
    /// <c>ISkiaSharpApiLease</c>, which buys back <c>SKBlendMode.Plus</c> at the cost of pulling
    /// SkiaSharp into this project and of only working on the Skia backend; not worth it until the
    /// owner says the bloom is missed.</para>
    /// </summary>
    public class AmbientFxCanvas : Decorator
    {
        private const int MaxBurstParticles = 150;
        private const int FaultLimit = 5;

        // ---- THE BANK: guided token flight (House Book) ----

        /// <summary>Hard ceiling on tokens in flight, and the size the sim array is pre-baked to.</summary>
        private const int MaxBankTokens = BankFlightPlan.MaxTokens;

        /// <summary>
        /// Core diameter band in ELEMENT pixels. The book calls for a coin, not a spark - and since
        /// THE BANK stopped firing for ambient XP (BankAccumulator.IsBankable) the coin is allowed
        /// to be a fatter one. MainWindow.BankFx.cs's BankBoxPadPx is sized off the largest halo
        /// this band can produce; the two move together.
        /// </summary>
        private const double BankTokenCoreMinPx = 6;
        private const double BankTokenCoreMaxPx = 9;

        /// <summary>Halo diameter as a multiple of the core. Enough to read as "lit", not as a puff.</summary>
        private const float BankTokenGlowScale = 4.0f;

        /// <summary>
        /// Peak alpha of the halo. Raised with the rest of the flight: a rare payout is allowed to
        /// be the brightest thing in the header for the third of a second it is crossing it.
        /// </summary>
        private const float BankTokenGlowAlpha = 0.42f;

        /// <summary>Fade-in after a token's stagger delay expires, so it arrives instead of popping.</summary>
        private const float BankTokenFadeInMs = 90f;

        private readonly DispatcherTimer _timer;
        private AmbientFxConfig _config = new();

        // ---- cached at (re)start: never read per tick ----
        private PerformanceTier _tier = PerformanceTier.Quality;
        private int _particleBudget;
        private int _targetFps = 30;
        private float _mistAlpha = 1f;

        /// <summary>
        /// The four palette slots, as ready-to-draw soft-dot brushes. These are the port's twin of
        /// the WPF original's one white sprite plus four <c>SKColorFilter</c>s: a gradient brush
        /// already carries its colour, so the tint IS the brush and no filter is needed.
        /// </summary>
        private IBrush? _mistDot, _particleDot, _glowDot, _flashDot, _glowSoft;
        private IBrush? _burstDot, _tokDot;

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

        private struct Spark { public float X, Y, VX, VY, Life, Max, Size; }
        private Spark[]? _burst;
        private int _burstN;

        /// <summary>
        /// One banked token. It carries its whole bezier rather than a velocity because the flight
        /// is AUTHORED, not simulated: position is a pure function of elapsed time, so a dropped
        /// frame moves the token further instead of bending its path, and the landing instant is
        /// exact no matter how the clock stutters.
        /// </summary>
        private struct Tok
        {
            public float X0, Y0;      // P0, normalized
            public float CX, CY;      // P1 (the bowed control point), normalized
            public float X2, Y2;      // P2, normalized
            public float X, Y;        // last evaluated position, normalized
            public float Elapsed;     // ms since the FLIGHT started, not since this token launched
            public float Delay, Dur;  // ms, from the flight plan
            public float Size;        // normalized core half-size, same units as Spark.Size
        }

        private Tok[]? _tok;
        private int _tokN;

        /// <summary>The live flight's landing callback, plus the counters that make (index, isLast) honest.</summary>
        private Action<int, bool>? _tokOnLand;
        private int _tokLanded;
        private int _tokTotal;

        /// <summary>
        /// Landing indices collected during a tick and dispatched after the sim loop has finished.
        /// Pre-sized like everything else here - but the real reason it exists is re-entrancy: a
        /// landing callback steps a counter and may do anything at all, including starting another
        /// flight, and it must not be able to do that while the loop is still walking the array.
        /// </summary>
        private readonly int[] _tokLandBuf = new int[MaxBankTokens];

        private bool _running;
        private bool _paused;
        private int _faults;
        private readonly Random _rng = new();

        /// <summary>
        /// Subscriptions to the host window's IsActive / WindowState. WPF hooked three events
        /// (Activated, Deactivated, StateChanged); Avalonia exposes IsActive as a DirectProperty and
        /// WindowState as a StyledProperty, and observing the properties covers both directions of
        /// the activation flip in one subscription instead of two half-events.
        /// </summary>
        private readonly List<IDisposable> _windowHooks = new();
        private Window? _window;

        public AmbientFxCanvas()
        {
            IsHitTestVisible = false;

            // The WPF original was clipped by its SKElement surface; nothing clips a Decorator's own
            // Render output, and fog puffs are deliberately drawn well past the bounds, so without
            // this a canvas would paint over its siblings.
            ClipToBounds = true;

            // Background priority on purpose: ambient FX must yield to input and layout, and the
            // frame gaps that causes are exactly what the governor reads to degrade itself.
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(33),
            };
            _timer.Tick += (_, _) => Tick();

            // IsLoaded is a plain getter in Avalonia, not a StyledProperty, so the gate cannot be
            // re-run from OnPropertyChanged the way IsVisible is - the event is the only hook.
            Loaded += (_, _) => Evaluate();

            ReadEnvironment();
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
            // The WPF twin leaned on the first timer tick to paint the freshly composed surface.
            // That is a hole whenever the clock is gated off - reduced motion, the Performance
            // tier, an inactive window - which would leave the surface blank instead of showing
            // its static first frame. Paint it here and the tick only ever moves it.
            InvalidateVisual();
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
            // A live token flight is force-landed rather than dropped: its callbacks are somebody
            // else's choreography and silently abandoning them leaves a held counter behind.
            ForceLandTokens();
            _burst = null;
            _burstN = 0;
            _dustN = 0;
            InvalidateVisual();
        }

        /// <summary>
        /// Re-read the mod palette and repaint. The WPF original subscribed to
        /// <c>App.Mods.ModChanged</c> for this; the Avalonia head has no mod service yet, so the
        /// shell calls it instead.
        /// ponytail: needs ModService.ModChanged, wired when it moves to Core.
        /// </summary>
        public void RefreshPalette()
        {
            try
            {
                ReadEnvironment();
                InvalidateVisual();
            }
            catch (Exception ex) { Log.Debug("AmbientFxCanvas.RefreshPalette: {E}", ex.Message); }
        }

        /// <summary>
        /// One-shot particle burst at an element-local point: 60-150 sparks over ~1.2s, then the
        /// buffer is released. Skipped entirely when particles are not allowed - event moments cost
        /// nothing at the Performance tier or under reduced motion.
        /// </summary>
        public void Burst(double x, double y, Color? color = null, int count = 90)
        {
            try
            {
                if (!Env.AllowParticles) return;
                double w = Bounds.Width, h = Bounds.Height;
                if (w <= 1 || h <= 1) return;

                if (_particleBudget <= 0) ReadEnvironment();
                count = Math.Clamp(count, 60, Math.Min(MaxBurstParticles, Math.Max(60, _particleBudget * 2)));

                _burstDot = MakeDot(color ?? Env.ParticleColor, 0f);

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
                    };
                }
                Evaluate();
            }
            catch (Exception ex)
            {
                Log.Debug("AmbientFxCanvas.Burst: {E}", ex.Message);
            }
        }

        /// <summary>
        /// THE BANK (House Book): <paramref name="count"/> tokens spawn at <paramref name="origin"/>
        /// and fly a slight arc to <paramref name="target"/> - both in element-local px - landing one
        /// after another so the counter can tick per landing.
        /// <paramref name="onLand"/> is invoked on the UI thread as each token arrives, with the
        /// landing's ordinal and whether it was the last of the flight. Timings and bow come from
        /// <see cref="BankFlightPlan"/>.
        ///
        /// <para><b>Arcs, not physics.</b> Each token rides a quadratic bezier whose control point
        /// is the midpoint pushed perpendicular by the plan's signed bow, and its parameter is
        /// eased IN - the book is explicit that tokens accelerate into the counter, which is what
        /// makes the arrival read as being caught rather than as coasting to a stop.</para>
        ///
        /// <para><b>Landing ordinal, not plan index.</b> Durations vary by 150ms while the stagger
        /// is 60-80ms, so tokens can and do land out of the order they left in. The index handed to
        /// <paramref name="onLand"/> counts LANDINGS, which is the only thing a counter stepping
        /// once per landing can safely divide by, and <c>isLast</c> is true exactly once.</para>
        ///
        /// <para><b>Safe to call while a flight is alive.</b> The old flight is force-landed first:
        /// its outstanding callbacks fire immediately, in order, with the last carrying
        /// <c>isLast</c>. Nothing is ever left holding a counter it was promised would be
        /// released - which is also why a refusal (reduced motion, an unmeasured canvas, a
        /// non-finite anchor) settles every callback on the spot instead of returning silently.
        /// The value still arrives; only the show is skipped.</para>
        /// </summary>
        public void BankTokens(Point origin, Point target, int count, Color? color, Action<int, bool>? onLand)
        {
            try
            {
                // A second flight always ends the first - THE BANK is one moment at a time.
                ForceLandTokens();

                if (count <= 0) return;

                double w = Bounds.Width, h = Bounds.Height;
                if (!Env.AllowParticles || w <= 1 || h <= 1 ||
                    !IsFinite(origin) || !IsFinite(target))
                {
                    SettleNow(count, onLand);
                    return;
                }

                if (_particleBudget <= 0) ReadEnvironment();
                // Budget-clamped exactly like Burst, even though ten tokens can never trouble a
                // tier that allows particles at all: the rule is that no emitter gets to opt out.
                count = Math.Clamp(count, 1, Math.Min(MaxBankTokens, Math.Max(1, _particleBudget)));

                var plan = BankFlightPlan.Plan(count, _rng.Next());
                if (plan.Length == 0) { SettleNow(count, onLand); return; }
                count = plan.Length;

                _tokDot = MakeDot(color ?? Env.ParticleColor, 0f);

                // Geometry is done in element px and normalized once at the end: normalized space is
                // anisotropic, so a perpendicular computed in it would bow the wrong way on any
                // canvas that is not square.
                double dx = target.X - origin.X, dy = target.Y - origin.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                double perpX = dist > 0.001 ? -dy / dist : 0;
                double perpY = dist > 0.001 ? dx / dist : 0;
                double midX = (origin.X + target.X) * 0.5;
                double midY = (origin.Y + target.Y) * 0.5;
                float minElem = (float)Math.Min(w, h);

                _tok ??= new Tok[MaxBankTokens];
                _tokN = 0;
                _tokOnLand = onLand;
                _tokLanded = 0;
                _tokTotal = count;

                for (int i = 0; i < count && _tokN < MaxBankTokens; i++)
                {
                    var slot = plan[i];
                    double bow = slot.ArcBow * dist;
                    double corePx = BankTokenCoreMinPx + _rng.NextDouble() * (BankTokenCoreMaxPx - BankTokenCoreMinPx);

                    _tok[_tokN++] = new Tok
                    {
                        X0 = (float)(origin.X / w), Y0 = (float)(origin.Y / h),
                        CX = (float)((midX + perpX * bow) / w), CY = (float)((midY + perpY * bow) / h),
                        X2 = (float)(target.X / w), Y2 = (float)(target.Y / h),
                        X = (float)(origin.X / w), Y = (float)(origin.Y / h),
                        Elapsed = 0f,
                        Delay = (float)slot.DelayMs,
                        Dur = (float)Math.Max(1.0, slot.DurationMs),
                        Size = (float)(corePx / (2.0 * Math.Max(1f, minElem))),
                    };
                }

                Evaluate();
            }
            catch (Exception ex)
            {
                Log.Debug("AmbientFxCanvas.BankTokens: {E}", ex.Message);
                // Whatever failed, the caller is mid-choreography and is waiting on callbacks it
                // will otherwise never get. Force-landing settles whatever was armed; if the flight
                // never armed at all this is a no-op and the caller's own watchdog takes it.
                try { ForceLandTokens(); } catch { }
            }
        }

        /// <summary>
        /// End the live flight now: clear the sim FIRST (so a callback that starts another flight
        /// cannot see a corpse), then fire every callback the flight still owed, in order.
        /// </summary>
        private void ForceLandTokens()
        {
            var cb = _tokOnLand;
            int landed = _tokLanded, total = _tokTotal;

            _tokOnLand = null;
            _tokLanded = 0;
            _tokTotal = 0;
            _tokN = 0;
            _tokDot = null;

            if (cb == null || total <= 0) return;
            for (int i = landed; i < total; i++) InvokeLand(cb, i, i == total - 1);
        }

        /// <summary>A flight that never flew, answered instantly so nobody is left holding a counter.</summary>
        private static void SettleNow(int count, Action<int, bool>? onLand)
        {
            if (onLand == null || count <= 0) return;
            for (int i = 0; i < count; i++) InvokeLand(onLand, i, i == count - 1);
        }

        /// <summary>
        /// Every landing callback is individually railed. A subscriber that throws on token three
        /// must not cost tokens four through seven their callbacks - the last one is the only thing
        /// that puts the counter back on the ledger's number.
        /// </summary>
        private static void InvokeLand(Action<int, bool>? cb, int index, bool isLast)
        {
            if (cb == null) return;
            try { cb(index, isLast); }
            catch (Exception ex) { Log.Debug("AmbientFxCanvas token landing: {E}", ex.Message); }
        }

        private static bool IsFinite(Point p)
            => !double.IsNaN(p.X) && !double.IsNaN(p.Y) &&
               !double.IsInfinity(p.X) && !double.IsInfinity(p.Y);

        // ============================== environment ==============================

        private void ReadEnvironment()
        {
            try
            {
                _tier = Env.CurrentTier;
                _particleBudget = Env.MaxAmbientParticles(_tier);
                _targetFps = Env.FxTargetFps(_tier);
                _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _targetFps));

                var mist = Env.MistColor;
                var particle = Env.ParticleColor;
                var glow = Env.GlowColor;
                var flash = Env.FlashTintColor;
                _mistAlpha = (float)Math.Clamp(Env.MistOpacity, 0.0, 1.0);

                // A surface with its own authored accent overrides the mod palette for the two
                // layers that read as "this thing's colour" - the particles and the glow. Mist and
                // flash stay the mod's, so the surface still sits inside the app's theme rather
                // than becoming a coloured hole in it.
                if (_config.Tint is { } tint)
                {
                    particle = tint;
                    glow = tint;
                }

                _mistDot = MakeDot(mist, 0f);
                _particleDot = MakeDot(particle, 0f);
                _glowDot = MakeDot(glow, 0f);
                _flashDot = MakeDot(flash, 0f);
                // The tighter-cored radial the glow-breath layer wants; the WPF twin baked a second
                // 160px sprite with a 0.28 core stop for exactly this.
                _glowSoft = MakeDot(glow, 0.28f);

                _liveBudget = _particleBudget;
                _fogOnly = false;
                _hitchScore = 0;
            }
            catch (Exception ex)
            {
                Log.Debug("AmbientFxCanvas.ReadEnvironment: {E}", ex.Message);
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

        protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            try
            {
                HookWindow(TopLevel.GetTopLevel(this) as Window);
                // ponytail: needs ModService.ModChanged to re-read the palette on a mod switch,
                // wired when it moves to Core; RefreshPalette() is the seam it will call.
                Evaluate();
            }
            catch (Exception ex) { Log.Debug("AmbientFxCanvas.OnAttachedToVisualTree: {E}", ex.Message); }
        }

        protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            try
            {
                StopClock();
                UnhookWindow();
            }
            catch (Exception ex) { Log.Debug("AmbientFxCanvas.OnDetachedFromVisualTree: {E}", ex.Message); }
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>The WPF twin's IsVisibleChanged handler; Avalonia routes it through the property system.</summary>
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
            _windowHooks.Add(_window.GetObservable(WindowBase.IsActiveProperty).Subscribe(new Ping(this)));
            _windowHooks.Add(_window.GetObservable(Window.WindowStateProperty).Subscribe(new Ping(this)));
        }

        private void UnhookWindow()
        {
            foreach (var h in _windowHooks)
            {
                try { h.Dispose(); } catch { /* already gone with its window */ }
            }
            _windowHooks.Clear();
            _window = null;
        }

        /// <summary>Re-runs the gate on any observed window change; the value itself is never read.</summary>
        private sealed class Ping : IObserver<bool>, IObserver<WindowState>
        {
            private readonly AmbientFxCanvas _owner;
            public Ping(AmbientFxCanvas owner) => _owner = owner;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(bool value) => _owner.Evaluate();
            public void OnNext(WindowState value) => _owner.Evaluate();
        }

        /// <summary>The single gate: start the clock only when everything says it may run.</summary>
        private void Evaluate()
        {
            try
            {
                if (ShouldRun()) StartClock();
                else StopClock();
            }
            catch (Exception ex) { Log.Debug("AmbientFxCanvas.Evaluate: {E}", ex.Message); }
        }

        private bool ShouldRun()
        {
            // Live one-shot work outruns the ambient gates: a burst or a token flight is an event
            // moment, already budget-checked at emit time, and it may run on a canvas composing no
            // ambient layers at all. A token flight counts for the extra reason that its landings
            // drive somebody else's counter - stopping the clock under it would strand the display.
            bool oneShotLive = (_burst != null && _burstN > 0) || _tokN > 0;
            if (_paused || _faults >= FaultLimit) return false;
            if (!_running && !oneShotLive) return false;
            if (!IsLoaded || !IsVisible) return false;
            if (!oneShotLive)
            {
                if (_targetFps <= 0) return false;
                if (!Env.AllowAmbientLoops) return false;
            }
            var w = _window;
            if (w != null)
            {
                if (w.WindowState == WindowState.Minimized) return false;
                if (!w.IsActive && !oneShotLive) return false;
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
                StepTokens(dt);

                InvalidateVisual();
            }
            catch (Exception ex)
            {
                _faults++;
                Log.Warning("AmbientFxCanvas tick failed ({N}/{Max}): {E}", _faults, FaultLimit, ex.Message);
                if (_faults >= FaultLimit)
                {
                    Log.Warning("AmbientFxCanvas: stopping after repeated faults");
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
                if (fogOnly) Log.Information("[FXPERF] ambient canvas dropped to fog-only (hitch score {S:F1})", _hitchScore);
                else Log.Information("[FXPERF] ambient canvas recovered");
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
            // The surface's own density share of whatever the governor is currently allowing. Never
            // above the live budget: this can only ever spend less than the tier permits.
            var density = Math.Clamp(_config.DustDensity, 0.0, 1.0);
            var target = Math.Min((int)Math.Round(_liveBudget * density), _dust.Length);

            // Shrink immediately when the density drops (a program cooling off between days), so the
            // field thins out on the next tick instead of waiting for natural expiry.
            if (_dustN > target) _dustN = Math.Max(0, target);

            // Refill slowly so the field breathes rather than blinking back in all at once.
            while (_dustN < target && _dustT > 0.12f)
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
                // Full teardown: the buffer and its brush go away until the next event moment.
                _burst = null;
                _burstDot = null;
                Evaluate();
            }
        }

        /// <summary>
        /// Advance the token flight. Nothing here integrates: each token's position is evaluated
        /// straight off its bezier at the eased fraction of its own elapsed time, so a hitch costs
        /// smoothness and never accuracy - a token that misses ten frames is simply further along.
        ///
        /// <para>Landings are collected and dispatched AFTER the loop, never inside it. The
        /// callback is the shell's counter step and may do arbitrary work, up to and including
        /// launching the next flight; letting it run mid-walk would mutate the array under the
        /// iterator.</para>
        /// </summary>
        private void StepTokens(float dt)
        {
            if (_tok == null || _tokN == 0) return;

            float dtMs = dt * 1000f;
            int landedNow = 0;

            for (int i = _tokN - 1; i >= 0; i--)
            {
                var t = _tok[i];
                t.Elapsed += dtMs;

                float local = t.Elapsed - t.Delay;
                if (local <= 0f) { _tok[i] = t; continue; }   // still waiting out its stagger

                float p = Math.Clamp(local / t.Dur, 0f, 1f);
                float e = p * p;                              // ease-in: accelerate INTO the counter
                float inv = 1f - e;
                t.X = inv * inv * t.X0 + 2f * inv * e * t.CX + e * e * t.X2;
                t.Y = inv * inv * t.Y0 + 2f * inv * e * t.CY + e * e * t.Y2;

                if (p >= 1f)
                {
                    _tok[i] = _tok[--_tokN];
                    if (landedNow < _tokLandBuf.Length) _tokLandBuf[landedNow++] = _tokLanded;
                    _tokLanded++;
                }
                else
                {
                    _tok[i] = t;
                }
            }

            if (landedNow == 0) return;

            var cb = _tokOnLand;
            int total = _tokTotal;
            bool done = _tokN == 0 && _tokLanded >= _tokTotal;

            if (done)
            {
                // Teardown before dispatch, for the same reason ForceLandTokens clears first.
                _tokOnLand = null;
                _tokLanded = 0;
                _tokTotal = 0;
                _tokDot = null;
            }

            for (int k = 0; k < landedNow; k++)
                InvokeLand(cb, _tokLandBuf[k], _tokLandBuf[k] == total - 1);

            if (done) Evaluate();
        }

        // ================================ paint ================================

        /// <summary>
        /// The Avalonia twin of the WPF original's <c>OnPaintSurface</c>. Layer order is identical,
        /// and so are the gates: the governor's fog-only mode cuts everything but the fog, and the
        /// two one-shot layers always draw because they are event moments, not ambience.
        /// </summary>
        public override void Render(DrawingContext context)
        {
            base.Render(context);

            double bw = Bounds.Width, bh = Bounds.Height;
            if (bw <= 0 || bh <= 0) return;

            try
            {
                float w = (float)bw, h = (float)bh;
                float min = Math.Min(w, h);
                float intensity = (float)Math.Clamp(_config.Intensity, 0.0, 1.5);
                var layers = _config.Layers;

                if (!_fogOnly && (layers & AmbientFxLayers.AuroraWash) != 0) DrawAurora(context, w, h, intensity);
                if ((layers & AmbientFxLayers.FogDrift) != 0) DrawFog(context, w, h, min, intensity);
                if (!_fogOnly && (layers & AmbientFxLayers.GlowBreath) != 0) DrawGlowBreath(context, w, h, min, intensity);
                if (!_fogOnly && (layers & AmbientFxLayers.DustField) != 0) DrawDust(context, w, h, min, intensity);
                if (!_fogOnly && (layers & AmbientFxLayers.SheenSweep) != 0) DrawSheen(context, w, h, intensity);
                DrawBurst(context, w, h, min);
                DrawTokens(context, w, h, min);
            }
            catch (Exception ex)
            {
                _faults++;
                Log.Debug("AmbientFxCanvas.Render: {E}", ex.Message);
            }
        }

        private void DrawAurora(DrawingContext ctx, float w, float h, float intensity)
        {
            // One long, slow diagonal wash: two mod colours sliding across the surface. Drawn as
            // the tinted sprite stretched way past the bounds so nothing needs a per-frame brush.
            float phase = (float)((Math.Sin(_auroraT * 0.06) + 1) * 0.5);
            float bw = w * 2.4f, bh = h * 2.4f;
            float cx = -w * 0.7f + phase * w * 1.4f;
            float cy = -h * 0.7f + (1f - phase) * h * 1.4f;

            DrawSprite(ctx, _mistDot, cx, cy, bw, bh, 0.13f * intensity * _mistAlpha);
            DrawSprite(ctx, _glowDot, w - cx, h - cy, bw * 0.8f, bh * 0.8f, 0.10f * intensity * _mistAlpha);
        }

        private void DrawFog(DrawingContext ctx, float w, float h, float min, float intensity)
        {
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
                DrawSprite(ctx, _mistDot, px * w, py * h, d, d, a);
            }
        }

        private void DrawGlowBreath(DrawingContext ctx, float w, float h, float min, float intensity)
        {
            float breath = 0.60f + 0.40f * (float)((Math.Sin(_breathT * 0.62) + 1) * 0.5);
            float d = (float)Math.Clamp(_config.GlowRadius, 0.05, 1.5) * min * 2f;
            DrawSprite(ctx, _glowSoft,
                       (float)_config.GlowCenter.X * w, (float)_config.GlowCenter.Y * h,
                       d, d, 0.30f * breath * intensity);
        }

        private void DrawDust(DrawingContext ctx, float w, float h, float min, float intensity)
        {
            if (_dustN == 0) return;
            for (int i = 0; i < _dustN; i++)
            {
                var d = _dust[i];
                float env = (float)Math.Sin(Math.PI * Math.Clamp(1.0 - d.Life / d.Max, 0.0, 1.0));
                float a = 0.55f * env * intensity;
                if (a <= 0.004f) continue;
                float size = d.Size * min * 2f;
                DrawSprite(ctx, _particleDot, d.X * w, d.Y * h, size, size, a);
            }
        }

        private void DrawSheen(DrawingContext ctx, float w, float h, float intensity)
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

            // Rotate THEN translate, matching the WPF canvas.Translate + RotateDegrees pair: an
            // Avalonia Matrix product applies its left operand first.
            var m = Matrix.CreateRotation(18.0 * Math.PI / 180.0) * Matrix.CreateTranslation(cx, h * 0.5f);
            using (ctx.PushTransform(m))
                DrawSprite(ctx, _flashDot, 0, 0, band, h * 2.4f, a);
        }

        private void DrawBurst(DrawingContext ctx, float w, float h, float min)
        {
            if (_burst == null || _burstN == 0) return;
            for (int i = 0; i < _burstN; i++)
            {
                var s = _burst[i];
                float env = Math.Clamp(s.Life / s.Max, 0f, 1f);
                float a = 0.95f * env;
                float size = s.Size * min * 2f * (0.6f + 0.4f * env);
                DrawSprite(ctx, _burstDot, s.X * w, s.Y * h, size, size, a);
            }
        }

        /// <summary>
        /// A token is a bright core sitting in a soft halo - two draws of the same dot at different
        /// scales, which is how everything else on this canvas gets a glow without a second brush.
        /// Drawn last, over the bursts: THE BANK is the thing being read.
        /// </summary>
        private void DrawTokens(DrawingContext ctx, float w, float h, float min)
        {
            if (_tok == null || _tokN == 0) return;

            for (int i = 0; i < _tokN; i++)
            {
                var t = _tok[i];
                float local = t.Elapsed - t.Delay;
                if (local <= 0f) continue;   // still staggered: it does not exist yet

                float p = Math.Clamp(local / t.Dur, 0f, 1f);
                float a = Math.Clamp(local / BankTokenFadeInMs, 0f, 1f);
                if (a <= 0.004f) continue;

                float core = t.Size * min * 2f;
                float x = t.X * w, y = t.Y * h;

                DrawSprite(ctx, _tokDot, x, y, core * BankTokenGlowScale, core * BankTokenGlowScale,
                           BankTokenGlowAlpha * a);

                // The core brightens as it closes, so the last thing the eye tracks is the arrival.
                DrawSprite(ctx, _tokDot, x, y, core, core, (0.75f + 0.25f * p) * a);
            }
        }

        /// <summary>
        /// One soft dot. The WPF twin drew a white <c>SKImage</c> under a Modulate colour filter at
        /// a per-draw alpha; here the brush already carries the colour, so only the alpha varies and
        /// it rides <c>PushOpacity</c> - which keeps the "nothing allocated per frame" rule the
        /// class comment promises.
        /// </summary>
        private static void DrawSprite(DrawingContext ctx, IBrush? brush, float cx, float cy, float w, float h, float alpha)
        {
            if (brush == null) return;
            double a = Math.Clamp(alpha, 0f, 1f);
            if (a <= 0.0015) return;
            using (ctx.PushOpacity(a))
                ctx.DrawEllipse(brush, null, new Point(cx, cy), w / 2.0, h / 2.0);
        }

        // ============================== helpers ==============================

        private static float Frac(float v) { v -= (float)Math.Floor(v); return v; }

        /// <summary>Wrap into a -0.3..1.3 band so puffs drift off one edge and back on the other.</summary>
        private static float Frac2(float v) => Frac((v + 0.3f) / 1.6f) * 1.6f - 0.3f;

        /// <summary>
        /// The port's twin of <c>BakeRadial</c>: a radial gradient that is opaque out to
        /// <paramref name="coreStop"/> and fades to nothing at the edge. Frozen with
        /// <c>ToImmutable</c> so the render thread never walks a mutable brush's property store.
        /// </summary>
        private static IBrush MakeDot(Color color, float coreStop)
        {
            var brush = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            };
            brush.GradientStops.Add(new GradientStop(color, 0));
            brush.GradientStops.Add(new GradientStop(color, Math.Clamp(coreStop, 0f, 0.9f)));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
            return brush.ToImmutable();
        }

        /// <summary>
        /// Local copy of ConditioningControlPanel/Models/AppSettings.cs:PerformanceTier. The WPF
        /// enum lives beside <c>AppSettings</c>, which is pinned to the head by <c>App.</c>; this
        /// canvas only needs the three names to keep its tier switches a line-for-line diff against
        /// the original. Nested and private on purpose: every FX control in this port hits the same
        /// missing enum, and a second top-level copy in this namespace would be a CS0101 the moment
        /// the stack is chained.
        /// ponytail: local copy of Models/AppSettings.cs:PerformanceTier, delete when AppSettings
        /// moves to Core.
        /// </summary>
        private enum PerformanceTier
        {
            Quality,
            Balanced,
            Performance,
        }

        /// <summary>
        /// Everything this canvas asks the app about. In the WPF head these are
        /// <c>PerformanceProfile</c>, <c>MotionFx</c> and <c>FxTheme</c>, all three of which read
        /// <c>App.Settings</c>, <c>App.Mods</c> and <c>Application.Current.Resources</c> and so
        /// cannot come along yet.
        ///
        /// <para>ponytail: needs PerformanceProfile, MotionFx and FxTheme, wired when they move to
        /// Core. The values below are the WPF Quality tier and FxTheme's own #FF69B4 fallback -
        /// placeholder data, chosen so the canvas draws its full composition rather than silently
        /// rendering nothing while the services are missing.</para>
        /// </summary>
        private static class Env
        {
            /// <summary>FxTheme.Fallback, the colour every slot resolves to with no mod loaded.</summary>
            private static readonly Color Fallback = Color.FromRgb(0xFF, 0x69, 0xB4);

            public static PerformanceTier CurrentTier => PerformanceTier.Quality;

            /// <summary>PerformanceProfile.MaxAmbientParticles, verbatim.</summary>
            public static int MaxAmbientParticles(PerformanceTier tier) => tier switch
            {
                PerformanceTier.Performance => 0,
                PerformanceTier.Balanced => 24,
                _ => 60,
            };

            /// <summary>PerformanceProfile.FxTargetFps, verbatim.</summary>
            public static int FxTargetFps(PerformanceTier tier) => tier switch
            {
                PerformanceTier.Performance => 0,
                PerformanceTier.Balanced => 24,
                _ => 30,
            };

            /// <summary>PerformanceProfile.AllowAmbientMotion, verbatim.</summary>
            public static bool AllowAmbientMotion(PerformanceTier tier) => tier != PerformanceTier.Performance;

            /// <summary>MotionFx.AllowAmbientLoops - the reduced-motion half is the missing piece.</summary>
            public static bool AllowAmbientLoops => AllowAmbientMotion(CurrentTier);

            /// <summary>MotionFx.AllowParticles.</summary>
            public static bool AllowParticles => AllowAmbientLoops && MaxAmbientParticles(CurrentTier) > 0;

            public static Color MistColor => Fallback;
            public static Color ParticleColor => Fallback;
            public static Color GlowColor => Fallback;
            public static Color FlashTintColor => Fallback;
            public static double MistOpacity => 1.0;
        }
    }
}
