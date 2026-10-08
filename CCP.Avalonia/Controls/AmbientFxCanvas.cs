using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Motion;
using ConditioningControlPanel.Services;
using Serilog;
using SkiaSharp;
using ModPackage = ConditioningControlPanel.Models.ModPackage;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;
using PerformanceTier = ConditioningControlPanel.Models.PerformanceTier;

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
        /// <summary>
        /// Warm motes rising from the bottom edge on a slow sine sway, budgeted by the tier and
        /// thinned by the governor like the dust. Additive: surfaces that never ask for it pay
        /// nothing.
        /// </summary>
        Embers = 1 << 5,
        /// <summary>
        /// Section-hued motes drifting clockwise along a thin window-edge strip (nav polish 9).
        /// The strip's side comes from <see cref="AmbientFxConfig.EdgeSide"/>; the maths is
        /// <see cref="EdgeDriftMath"/>. Additive like the rest: no other surface pays for it.
        /// </summary>
        EdgeDrift = 1 << 6,
        /// <summary>
        /// Soft section-hued puffs drifting and breathing along a window-edge strip (nav polish 11):
        /// the section edge's fog. Side from <see cref="AmbientFxConfig.EdgeSide"/>, maths in
        /// <see cref="EdgeFogMath"/>, sim and paint in AmbientFxCanvas.EdgeFog.cs. The one layer that
        /// may also run at Reduced motion, and only when <see cref="AmbientFxConfig.EdgeFogReduced"/>
        /// asks for it (half the puffs at half the speed).
        /// </summary>
        EdgeFog = 1 << 7,
        /// <summary>
        /// The Premium page's motes (polish 12 round 2): gold glitter rising off the Basic cards,
        /// cyan diamonds off the Prime ones, a few loose sparkles anywhere. Zones from
        /// <see cref="AmbientFxCanvas.SetVaultZones"/>, numbers in <see cref="VaultMoteMath"/>, sim and
        /// paint in AmbientFxCanvas.Vault.cs. May tick at Reduced (a few slow motes) like EdgeFog.
        /// </summary>
        VaultMotes = 1 << 8,
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

        /// <summary>The window edge an <see cref="AmbientFxLayers.EdgeDrift"/> strip lines.</summary>
        public EdgeSide EdgeSide { get; set; } = EdgeSide.Top;

        /// <summary>
        /// The depth, in DIPs, the <see cref="AmbientFxLayers.EdgeDrift"/> motes keep to on a strip
        /// thicker than their band (the fog's strip). 0, the default, spreads them over the whole
        /// strip exactly as before.
        /// </summary>
        public double EdgeDriftBandPx { get; set; }

        /// <summary><see cref="AmbientFxLayers.EdgeFog"/> at Reduced: half the puffs at half the
        /// speed, and the canvas may tick at Reduced motion for this layer.</summary>
        public bool EdgeFogReduced { get; set; }

        /// <summary>Alpha gain on the fog (the section edge balances light and dark hues), 0-1.5.</summary>
        public double EdgeFogGain { get; set; } = 1.0;

        /// <summary>
        /// Keep ticking while the host window is NOT the active window (polish wave 13). Only for a
        /// host that already repaints every frame on its own, so the clock adds no wake-ups: the
        /// companion tube. Minimised, hidden, Motion below Full and the tier budget still stop it.
        /// Never set it on a surface inside the main window (#550 idle parking).
        /// </summary>
        public bool RunWhileInactive { get; set; }
    }

    /// <summary>
    /// PORTED from ConditioningControlPanel/Controls/AmbientFxCanvas.cs (7.1.5).
    ///
    /// <para>The one reusable in-window FX surface: a hit-test-invisible Skia surface
    /// (<see cref="FxSurface"/>, half resolution, layers drawn with <see cref="SKBlendMode.Plus"/> so
    /// overlapping glows bloom) running a self-stopping ~30fps frame-locked <see cref="FrameClock"/>,
    /// composed from the layer vocabulary in <see cref="AmbientFxLayers"/>. Deliberately NOT the
    /// fullscreen compositor - that is per-monitor topmost overlay windows, and keeping its shared
    /// tick alive for ambient loops would undo the idle-parking that fixed #550. This control spawns
    /// no window of any kind.</para>
    ///
    /// Rules it enforces for every caller:
    ///   • the clock only runs while the control is loaded, effectively visible, and its window is
    ///     active and not minimized - the clock stops itself the moment any of that stops holding;
    ///   • colours and the performance budget are read ONCE at (re)start and cached, never per tick;
    ///     a mod switch re-reads them;
    ///   • nothing is allocated per frame - one shared soft-dot <see cref="SKImage"/>, one glow
    ///     image, one <see cref="SKPaint"/> and colour filters rebuilt only when the palette moves;
    ///   • every tick is wrapped, and repeated faults stop the clock instead of spamming the log.
    ///
    /// <para>All simulation state is in element-normalized (0-1) coordinates, so it is DPI- and
    /// Viewbox-agnostic, while the two one-shot entry points (<see cref="Burst"/> and
    /// <see cref="BankTokens"/>) take plain element-local coordinates.</para>
    ///
    /// <para>Port history: the first port drew the layers with Avalonia's DrawingContext
    /// (source-over, no bloom) on a DispatcherTimer. Parity wave 1 put it back on the WPF engine:
    /// Skia sprites under Plus on a reduced-resolution <see cref="FxSurface"/>, ticked by the
    /// frame-locked <see cref="FrameClock"/>, plus the Embers, EdgeDrift, EdgeFog and VaultMotes
    /// layers the first port had not carried.</para>
    /// </summary>
    public partial class AmbientFxCanvas : Decorator
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

        private readonly FxSurface _sk;
        private readonly FrameClock _timer;

        /// <summary>
        /// Share of the screen's pixels the ambient layers raster at (perf pass, 2026-10-07). Fog,
        /// aurora, dust and embers are soft sprites and read the same at half resolution for a
        /// quarter of the CPU fill and of the per-frame upload. A live burst or token flight paints
        /// at full resolution, because sparks and coins are crisp.
        /// </summary>
        public const double AmbientResolution = 0.5;
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

        /// <summary>Cap on the ember pool; the tier budget can only lower it.</summary>
        private const int EmberMax = 40;
        /// <summary>The share of the live particle budget embers may spend (dust keeps its own).</summary>
        private const float EmberBudgetShare = 0.66f;
        private struct Ember { public float X0, Y, VY, Amp, Phase, PhaseSpd, Life, Max, Size; }
        private Ember[] _embers = Array.Empty<Ember>();
        private int _emberN;
        private float _emberT;
        private SKColorFilter? _emberTint;

        // Edge drift (nav polish 9): along/depth are strip-normalized (see EdgeDriftMath), size is px.
        private struct EdgeMote { public float Along, Depth, Speed, Life, Max, SizePx, Phase, PhaseSpd; }
        private EdgeMote[] _edge = Array.Empty<EdgeMote>();
        private int _edgeN;
        private float _edgeT;

        private struct Spark { public float X, Y, VX, VY, Life, Max, Size; }
        private Spark[]? _burst;
        private int _burstN;
        private SKColorFilter? _burstTint;

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
        private SKColorFilter? _tokTint;

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
        /// Subscriptions to the host window's IsActive / WindowState, plus the effective-visibility
        /// watch. WPF hooked Activated, Deactivated and StateChanged; Avalonia exposes IsActive as a
        /// DirectProperty and WindowState as a StyledProperty, and observing the properties covers
        /// both directions of the activation flip in one subscription instead of two half-events.
        /// </summary>
        private readonly List<IDisposable> _windowHooks = new();
        private Window? _window;
        private bool _modHooked;

        private readonly SKPaint _paint = FxSprites.AdditivePaint();

        public AmbientFxCanvas()
        {
            IsHitTestVisible = false;

            // Fog puffs are drawn well past the bounds; the surface clips them, and so does this.
            ClipToBounds = true;

            _sk = new FxSurface { IsHitTestVisible = false, ResolutionScale = AmbientResolution };
            _sk.PaintSurface += OnPaintSurface;
            Child = _sk;

            // Frame-locked (perf pass): the tick lands right before a frame is composed and holds
            // the tier's rate on whole frames, so motion steps evenly. Late frames still show up
            // as gaps, which is what the governor reads to degrade itself.
            _timer = new FrameClock(this) { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => Tick();

            // IsLoaded is a plain getter in Avalonia, not a StyledProperty, so the gate cannot be
            // re-run from OnPropertyChanged the way IsVisible is - the event is the only hook.
            Loaded += (_, _) => Evaluate();

            ReadEnvironment();
        }

        /// <summary>True while the frame clock runs. Tests read it; nothing else needs to.</summary>
        internal bool IsTicking => _timer.IsEnabled;

        /// <summary>The surface the layers paint on (tests, bench).</summary>
        internal FxSurface Surface => _sk;

        /// <summary>The layers this canvas was last asked to run.</summary>
        public AmbientFxLayers Layers => _config.Layers;

        /// <summary>True while the clock is actually ticking.</summary>
        public bool IsRunning => _timer.IsEnabled;

        /// <summary>The tint override this canvas is painting with, if any.</summary>
        public Color? Tint => _config.Tint;

        /// <summary>The side an edge drift strip was started on.</summary>
        public EdgeSide EdgeSide => _config.EdgeSide;

        /// <summary>Edge motes alive right now (tests).</summary>
        internal int EdgeMoteCount => _edgeN;

        /// <summary>Embers alive right now (tests).</summary>
        internal int EmberCount => _emberN;

        /// <summary>
        /// Swap the tint override without reseeding: live particles take the new colour on the
        /// next frame. The section edge calls it on every section change, under its own fade.
        /// </summary>
        public void Retint(Color tint)
        {
            try
            {
                _config.Tint = tint;
                ApplyAccent(new SKColor(tint.R, tint.G, tint.B));
                _sk.Redraw();
            }
            catch (Exception ex)
            {
                Log.Debug("AmbientFxCanvas.Retint: {E}", ex.Message);
            }
        }

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
            // WPF leaned on the first tick to paint the freshly composed surface. That is a hole
            // whenever the clock is gated off - reduced motion, the Performance tier, an inactive
            // window - which would leave the surface blank instead of showing its static first
            // frame. Paint it here and the tick only ever moves it.
            _sk.Redraw();
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
            _emberN = 0;
            _edgeN = 0;
            _fogN = 0;
            _grainN = 0;
            _vaultN = 0;
            _sk.Redraw();
        }

        /// <summary>
        /// Re-read the mod palette and repaint. Wired to <see cref="CoreMods.ModChanged"/> while
        /// attached, exactly as the WPF twin subscribes to <c>App.Mods.ModChanged</c>; still public
        /// because a host that repaints for its own reasons (a live event tint) pushes it too.
        /// </summary>
        public void RefreshPalette()
        {
            try
            {
                ReadEnvironment();
                _sk.Redraw();
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

                var c = color is { } wc ? new SKColor(wc.R, wc.G, wc.B) : _particle;
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

                var c = color is { } wc ? new SKColor(wc.R, wc.G, wc.B) : _particle;
                _tokTint?.Dispose();
                _tokTint = SKColorFilter.CreateBlendMode(c, SKBlendMode.Modulate);

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
            _tokTint?.Dispose();
            _tokTint = null;

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

                _mist = ToSk(Env.MistColor);
                _particle = ToSk(Env.ParticleColor);
                _glow = ToSk(Env.GlowColor);
                _flash = ToSk(Env.FlashTintColor);
                _mistAlpha = (float)Math.Clamp(Env.MistOpacity, 0.0, 1.0);

                // A surface with its own authored accent overrides the mod palette for the two
                // layers that read as "this thing's colour" - the particles and the glow. Mist and
                // flash stay the mod's, so the surface still sits inside the app's theme rather
                // than becoming a coloured hole in it.
                _mistTint?.Dispose(); _mistTint = SKColorFilter.CreateBlendMode(_mist, SKBlendMode.Modulate);
                _flashTint?.Dispose(); _flashTint = SKColorFilter.CreateBlendMode(_flash, SKBlendMode.Modulate);
                ApplyAccent(_config.Tint is { } tint ? new SKColor(tint.R, tint.G, tint.B) : null);

                _liveBudget = _particleBudget;
                _fogOnly = false;
                _hitchScore = 0;
            }
            catch (Exception ex)
            {
                Log.Debug("AmbientFxCanvas.ReadEnvironment: {E}", ex.Message);
            }
        }

        /// <summary>
        /// Rebuild the particle, glow and ember filters, optionally over an accent first. Shared by
        /// <see cref="ReadEnvironment"/> and <see cref="Retint"/>, so a retint can never disagree
        /// with a restart about what a tint does.
        /// </summary>
        private void ApplyAccent(SKColor? accent)
        {
            if (accent is { } a)
            {
                _particle = a;
                _glow = a;
            }
            _particleTint?.Dispose(); _particleTint = SKColorFilter.CreateBlendMode(_particle, SKBlendMode.Modulate);
            _glowTint?.Dispose(); _glowTint = SKColorFilter.CreateBlendMode(_glow, SKBlendMode.Modulate);
            // Embers sit halfway between the mod's particle colour and a candle gold, so they
            // read warm on every palette without leaving the theme.
            var ember = new SKColor((byte)((_particle.Red + 255) / 2), (byte)((_particle.Green + 196) / 2), (byte)((_particle.Blue + 110) / 2));
            _emberTint?.Dispose(); _emberTint = SKColorFilter.CreateBlendMode(ember, SKBlendMode.Modulate);
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
            _embers = _particleBudget > 0 && (_config.Layers & AmbientFxLayers.Embers) != 0
                ? new Ember[Math.Min(EmberMax, _particleBudget)]
                : Array.Empty<Ember>();
            _emberN = 0;
            _emberT = 0f;
            _edge = _particleBudget > 0 && (_config.Layers & AmbientFxLayers.EdgeDrift) != 0
                ? new EdgeMote[Math.Min(EdgeDriftMath.MaxPerStrip, _particleBudget)]
                : Array.Empty<EdgeMote>();
            _edgeN = 0;
            _edgeT = 0f;
            ReseedFog();
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
                if (!_modHooked) { CoreMods.ModChanged += OnModChanged; Env.MotionGateChanged += Evaluate; _modHooked = true; }
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
                if (_modHooked) { CoreMods.ModChanged -= OnModChanged; Env.MotionGateChanged -= Evaluate; _modHooked = false; }
            }
            catch (Exception ex) { Log.Debug("AmbientFxCanvas.OnDetachedFromVisualTree: {E}", ex.Message); }
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>
        /// A mod switch re-reads the palette and repaints; it never reseeds, so the fog keeps its
        /// drift and only its colour moves. The head raises this from whatever thread its mod
        /// service switched on, so it is marshalled before touching the filters.
        /// </summary>
        private void OnModChanged(object? sender, ModPackage mod)
        {
            if (Dispatcher.UIThread.CheckAccess()) RefreshPalette();
            else Dispatcher.UIThread.Post(RefreshPalette);
        }

        /// <summary>The WPF twin's IsVisibleChanged handler; Avalonia routes it through the property system.</summary>
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty) Evaluate();
        }

        private void HookWindow(Window? window)
        {
            if (window != null && ReferenceEquals(_window, window)) return;
            UnhookWindow();
            _windowHooks.Add(EffectiveVisibility.Watch(this, Evaluate));
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
            if (FxBisect.Off("canvas:" + (string.IsNullOrEmpty(Name) ? _config.Layers.ToString() : Name))) return false;
            if (!_running && !oneShotLive) return false;
            if (!IsLoaded || !IsEffectivelyVisible) return false;
            if (!oneShotLive)
            {
                if (_targetFps <= 0) return false;
                if (!Env.AllowAmbientLoops && !ReducedFogMayRun() && !ReducedVaultMayRun()) return false;
            }
            var w = _window;
            if (w != null)
            {
                if (w.WindowState == WindowState.Minimized) return false;
                if (!w.IsActive && !oneShotLive && !_config.RunWhileInactive
                    && !FxBisect.Off("forceactive")) return false;
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
                StepAll(gapMs);
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

        /// <summary>One frame of sim plus the repaint, given the gap since the last one (ms).</summary>
        private void StepAll(double gapMs)
        {
            float dt = (float)Math.Clamp(gapMs / 1000.0, 0.001, 0.100);

            Governor(gapMs);

            _fogT += dt;
            _auroraT += dt;
            _breathT += dt;
            if (!_sheenDone) _sheenT += dt;
            StepDust(dt);
            StepEmbers(dt);
            StepEdge(dt);
            StepFog(dt);
            StepVault(dt);
            StepBurst(dt);
            StepTokens(dt);

            bool crisp = (_burst != null && _burstN > 0) || _tokN > 0;
            _sk.ResolutionScale = crisp ? 1.0 : AmbientResolution;
            _sk.Redraw();
        }

        /// <summary>Test and bench seam: advance <paramref name="frames"/> frames of <paramref name="frameMs"/> each, gates aside.</summary>
        internal void StepForTests(int frames, double frameMs = 33)
        {
            for (int i = 0; i < frames; i++) StepAll(frameMs);
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

        /// <summary>How many embers the governor currently allows: a share of the live budget.</summary>
        private int EmberTarget() =>
            _fogOnly ? 0 : Math.Min(_embers.Length, (int)Math.Round(_liveBudget * EmberBudgetShare));

        private void StepEmbers(float dt)
        {
            if (_embers.Length == 0) return;
            for (int i = _emberN - 1; i >= 0; i--)
            {
                var m = _embers[i];
                m.Y += m.VY * dt;
                m.Phase += m.PhaseSpd * dt;
                m.Life -= dt;
                if (m.Life <= 0f || m.Y < -0.06f)
                    _embers[i] = _embers[--_emberN];
                else
                    _embers[i] = m;
            }

            if ((_config.Layers & AmbientFxLayers.Embers) == 0 || _fogOnly) return;

            int target = EmberTarget();
            if (_emberN > target) _emberN = Math.Max(0, target);

            // One every quarter second at most, so the field fills over ten seconds rather than
            // appearing as a curtain.
            _emberT += dt;
            while (_emberN < target && _emberT > 0.25f)
            {
                _emberT -= 0.25f;
                float life = 10f + (float)_rng.NextDouble() * 8f;
                _embers[_emberN++] = new Ember
                {
                    X0 = (float)_rng.NextDouble(),
                    Y = 1.02f + (float)_rng.NextDouble() * 0.05f,
                    VY = -(0.035f + (float)_rng.NextDouble() * 0.030f),
                    Amp = 0.010f + (float)_rng.NextDouble() * 0.022f,
                    Phase = (float)(_rng.NextDouble() * Math.PI * 2),
                    PhaseSpd = 0.8f + (float)_rng.NextDouble() * 1.2f,
                    Life = life, Max = life,
                    Size = 0.0040f + (float)_rng.NextDouble() * 0.0045f,
                };
            }
        }

        /// <summary>
        /// Edge drift: every mote slides clockwise along its strip at its own speed, holds its
        /// depth, and is retired at the strip end or when its life runs out. Refills one mote per
        /// <see cref="EdgeDriftMath.SpawnEverySeconds"/> up to the governed target.
        /// </summary>
        internal void StepEdge(float dt)
        {
            if (_edge.Length == 0) return;
            for (int i = _edgeN - 1; i >= 0; i--)
            {
                var m = _edge[i];
                m.Along = (float)EdgeDriftMath.Advance(m.Along, m.Speed, dt);
                m.Phase += m.PhaseSpd * dt;
                m.Life -= dt;
                if (EdgeDriftMath.IsSpent(m.Along, m.Life))
                    _edge[i] = _edge[--_edgeN];
                else
                    _edge[i] = m;
            }

            if ((_config.Layers & AmbientFxLayers.EdgeDrift) == 0 || _fogOnly) return;

            int target = Math.Min(_edge.Length, EdgeDriftMath.Target(_liveBudget));
            if (_edgeN > target) _edgeN = Math.Max(0, target);

            // The spawn clock only banks while a strip is short; a full strip holds one spawn's
            // worth, so a mote retiring after a long full spell is replaced alone, not in a burst.
            _edgeT = _edgeN >= target
                ? Math.Min(_edgeT + dt, (float)EdgeDriftMath.SpawnEverySeconds)
                : _edgeT + dt;
            while (_edgeN < target && _edgeT > EdgeDriftMath.SpawnEverySeconds)
            {
                _edgeT -= (float)EdgeDriftMath.SpawnEverySeconds;
                float life = (float)(EdgeDriftMath.LifeMin + _rng.NextDouble() * (EdgeDriftMath.LifeMax - EdgeDriftMath.LifeMin));
                _edge[_edgeN++] = new EdgeMote
                {
                    Along = (float)_rng.NextDouble() * 0.9f,
                    Depth = (float)(EdgeDriftMath.DepthMin + _rng.NextDouble() * (EdgeDriftMath.DepthMax - EdgeDriftMath.DepthMin)),
                    Speed = (float)(EdgeDriftMath.SpeedMin + _rng.NextDouble() * (EdgeDriftMath.SpeedMax - EdgeDriftMath.SpeedMin)),
                    Life = life, Max = life,
                    SizePx = (float)(EdgeDriftMath.SizeMinPx + _rng.NextDouble() * (EdgeDriftMath.SizeMaxPx - EdgeDriftMath.SizeMinPx)),
                    Phase = (float)(_rng.NextDouble() * Math.PI * 2),
                    PhaseSpd = 1.5f + (float)_rng.NextDouble() * 1.5f,
                };
            }
        }

        /// <summary>
        /// Test seam: the (along, depth) of every live edge mote, so a test can step the sim and
        /// check the band and the direction without a paint.
        /// </summary>
        internal (float Along, float Depth)[] EdgeMotesForTests()
        {
            var r = new (float, float)[_edgeN];
            for (int i = 0; i < _edgeN; i++) r[i] = (_edge[i].Along, _edge[i].Depth);
            return r;
        }

        /// <summary>Test seam: seed the sim as if the tier allowed <paramref name="budget"/> particles.</summary>
        internal void PrimeEdgeForTests(int budget)
        {
            _particleBudget = budget;
            _liveBudget = budget;
            _fogOnly = false;
            Reseed();
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
                _tokTint?.Dispose();
                _tokTint = null;
            }

            for (int k = 0; k < landedNow; k++)
                InvokeLand(cb, _tokLandBuf[k], _tokLandBuf[k] == total - 1);

            if (done) Evaluate();
        }

        // ================================ paint ================================

        private void OnPaintSurface(object? sender, FxPaintEventArgs e)
        {
            var canvas = e.Canvas;
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
                if (!_fogOnly && (layers & AmbientFxLayers.Embers) != 0) DrawEmbers(canvas, w, h, min, intensity);
                if ((layers & AmbientFxLayers.EdgeFog) != 0) DrawEdgeFog(canvas, w, h);
                if (!_fogOnly && (layers & AmbientFxLayers.EdgeDrift) != 0) DrawEdge(canvas, w, h, intensity);
                if (!_fogOnly && (layers & AmbientFxLayers.SheenSweep) != 0) DrawSheen(canvas, w, h, intensity);
                if ((layers & AmbientFxLayers.VaultMotes) != 0) DrawVault(canvas, w, h);
                DrawBurst(canvas, w, h, min);
                DrawTokens(canvas, w, h, min);
            }
            catch (Exception ex)
            {
                _faults++;
                Log.Debug("AmbientFxCanvas.OnPaintSurface: {E}", ex.Message);
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
            DrawSprite(canvas, FxSprites.Dot, cx, cy, bw, bh);

            _paint.ColorFilter = _glowTint;
            _paint.Color = SKColors.White.WithAlpha(Alpha(0.10f * intensity * _mistAlpha));
            DrawSprite(canvas, FxSprites.Dot, w - cx, h - cy, bw * 0.8f, bh * 0.8f);
            _paint.ColorFilter = null;
        }

        private void DrawFog(SKCanvas canvas, float w, float h, float min, float intensity)
        {
            _paint.ColorFilter = _mistTint;
            for (int i = 0; i < _puffs.Length; i++)
            {
                var p = _puffs[i];
                // Position is a pure function of the clock - no integration state to drift, and a
                // paused/resumed canvas picks up exactly where the elapsed time says it should.
                float px = Frac2(p.X + p.VX * _fogT);
                float py = Frac2(p.Y + p.VY * _fogT);
                float a = p.BaseA * (0.70f + 0.30f * (float)Math.Sin(p.Phase + _fogT * p.PhaseSpd))
                          * intensity * _mistAlpha;
                if (a <= 0.004f) continue;
                float d = p.R * min * 2f;
                _paint.Color = SKColors.White.WithAlpha(Alpha(a));
                DrawSprite(canvas, FxSprites.Dot, px * w, py * h, d, d);
            }
            _paint.ColorFilter = null;
        }

        private void DrawGlowBreath(SKCanvas canvas, float w, float h, float min, float intensity)
        {
            float breath = 0.60f + 0.40f * (float)((Math.Sin(_breathT * 0.62) + 1) * 0.5);
            float d = (float)Math.Clamp(_config.GlowRadius, 0.05, 1.5) * min * 2f;
            _paint.ColorFilter = _glowTint;
            _paint.Color = SKColors.White.WithAlpha(Alpha(0.30f * breath * intensity));
            DrawSprite(canvas, FxSprites.GlowSprite, (float)_config.GlowCenter.X * w, (float)_config.GlowCenter.Y * h, d, d);
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
                DrawSprite(canvas, FxSprites.Dot, d.X * w, d.Y * h, size, size);
            }
            _paint.ColorFilter = null;
        }

        private void DrawEmbers(SKCanvas canvas, float w, float h, float min, float intensity)
        {
            if (_emberN == 0) return;
            _paint.ColorFilter = _emberTint;
            for (int i = 0; i < _emberN; i++)
            {
                var m = _embers[i];
                // Fade in over the first stretch of the climb, fade out toward the top edge, and
                // flicker a little on the way like a spark that is still deciding.
                float rise = Math.Clamp((1.02f - m.Y) / 0.08f, 0f, 1f);
                float high = Math.Clamp(m.Y / 0.30f, 0f, 1f);
                float flicker = 0.78f + 0.22f * (float)Math.Sin(m.Phase * 2.7f);
                float a = 0.72f * rise * high * flicker * intensity;
                if (a <= 0.004f) continue;
                float x = m.X0 + m.Amp * (float)Math.Sin(m.Phase);
                float size = m.Size * min * 2f;
                _paint.Color = SKColors.White.WithAlpha(Alpha(a));
                DrawSprite(canvas, FxSprites.Dot, x * w, m.Y * h, size, size);
            }
            _paint.ColorFilter = null;
        }

        private void DrawEdge(SKCanvas canvas, float w, float h, float intensity)
        {
            if (_edgeN == 0) return;
            // Sizes are authored in native px: scale element units to the surface's device pixels once.
            double aw = Bounds.Width;
            float px = aw > 1 ? (float)(w / aw) : 1f;
            var side = _config.EdgeSide;
            // A band narrower than the strip (the fog's 56 px strip) keeps the motes in their
            // authored 30 px; the default 0 spreads them over the whole strip as before.
            double thick = side is EdgeSide.Top or EdgeSide.Bottom ? Bounds.Height : aw;
            float band = _config.EdgeDriftBandPx > 0 && thick > _config.EdgeDriftBandPx
                ? (float)(_config.EdgeDriftBandPx / thick) : 1f;
            _paint.ColorFilter = _particleTint;
            for (int i = 0; i < _edgeN; i++)
            {
                var m = _edge[i];
                float a = (float)EdgeDriftMath.Alpha(m.Along, m.Life, m.Max, m.Phase, intensity);
                if (a <= 0.004f) continue;
                var (x, y) = EdgeDriftMath.Position(side, m.Along, m.Depth * band);
                float size = m.SizePx * px;
                _paint.Color = SKColors.White.WithAlpha(Alpha(a));
                DrawSprite(canvas, FxSprites.Dot, (float)x * w, (float)y * h, size, size);
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
            DrawSprite(canvas, FxSprites.Dot, 0, 0, band, h * 2.4f);
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
                DrawSprite(canvas, FxSprites.Dot, s.X * w, s.Y * h, size, size);
            }
            _paint.ColorFilter = null;
        }

        /// <summary>
        /// A token is a bright core sitting in a soft halo - two draws of the shared dot at
        /// different scales, which is how everything else on this canvas gets a glow without
        /// allocating a shader. Drawn last, over the bursts: THE BANK is the thing being read.
        /// </summary>
        private void DrawTokens(SKCanvas canvas, float w, float h, float min)
        {
            if (_tok == null || _tokN == 0) return;

            _paint.ColorFilter = _tokTint;
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

                _paint.Color = SKColors.White.WithAlpha(Alpha(BankTokenGlowAlpha * a));
                DrawSprite(canvas, FxSprites.Dot, x, y, core * BankTokenGlowScale, core * BankTokenGlowScale);

                // The core brightens as it closes, so the last thing the eye tracks is the arrival.
                _paint.Color = SKColors.White.WithAlpha(Alpha((0.75f + 0.25f * p) * a));
                DrawSprite(canvas, FxSprites.Dot, x, y, core, core);
            }
            _paint.ColorFilter = null;
        }

        private void DrawSprite(SKCanvas canvas, SKImage? img, float cx, float cy, float w, float h)
            => FxSprites.DrawSprite(canvas, img, _paint, cx, cy, w, h);

        // ============================== helpers ==============================

        private static byte Alpha(float a) => FxSprites.Alpha(a);

        private static float Frac(float v) { v -= (float)Math.Floor(v); return v; }

        /// <summary>Wrap into a -0.3..1.3 band so puffs drift off one edge and back on the other.</summary>
        private static float Frac2(float v) => Frac((v + 0.3f) / 1.6f) * 1.6f - 0.3f;

        private static SKColor ToSk(Color c) => new(c.R, c.G, c.B);

        /// <summary>
        /// Everything the FX controls in this head ask the app about, in one place. In the WPF head
        /// these are three services - <c>PerformanceProfile</c>, <c>MotionFx</c> and <c>FxTheme</c>.
        /// The first two read <c>App.Settings</c>, which is <see cref="CoreSettings"/> now, so the
        /// tier and the motion gate below are the real decision rather than a placeholder.
        ///
        /// <para>ponytail: internal and nested rather than a file of its own, because
        /// <see cref="TakeoverOrb"/> and <see cref="VatGlassCanvas"/> read it and the per-tier
        /// tables must exist exactly once. Those tables are pure and belong in Core beside
        /// <c>PerformanceTier</c>; when PerformanceProfile moves, this becomes a forwarder.</para>
        /// </summary>
        internal static class Env
        {
            /// <summary>FxTheme.Fallback, the colour every slot resolves to with no palette set.</summary>
            private static readonly Color Fallback = Color.FromRgb(0xFF, 0x69, 0xB4);

            static Env()
            {
                // The desktop's reduced-motion flag flips on a background probe; every loop that
                // listens to the gate re-reads it on the UI thread, as a motion-level change does.
                OsReducedMotion.Changed += () =>
                {
                    try { Dispatcher.UIThread.Post(RaiseMotionGateChanged); }
                    catch { /* no dispatcher (tests) */ }
                };
            }

            /// <summary>
            /// WPF MainWindow.UiUpdates.CmbMotionLevel_SelectionChanged stops/re-arms every running
            /// ambient loop when the level changes; here the loops subscribe while loaded and
            /// re-read <see cref="AllowAmbientLoops"/>. Raised on the UI thread by Settings ▸ Performance
            /// and when the desktop's reduced-motion preference flips.
            /// </summary>
            internal static event Action? MotionGateChanged;

            internal static void RaiseMotionGateChanged() => MotionGateChanged?.Invoke();

            /// <summary>
            /// PerformanceProfile.CurrentTier. Its automatic escalation counts live flash windows
            /// and ambient bubbles; this head has neither, so HeavyElementCount is 0,
            /// AutoPerformanceMode can never escalate, and the explicit toggle IS the whole
            /// decision. Do not "fix" the missing Balanced branch - it is unreachable here.
            /// </summary>
            public static PerformanceTier CurrentTier =>
                CoreSettings.Current.PerformanceMode ? PerformanceTier.Performance : PerformanceTier.Quality;

            /// <summary>PerformanceProfile.MaxAmbientParticles: Core MotionGate.</summary>
            public static int MaxAmbientParticles(PerformanceTier tier) => MotionGate.MaxAmbientParticles(tier);

            /// <summary>PerformanceProfile.FxTargetFps: Core MotionGate.</summary>
            public static int FxTargetFps(PerformanceTier tier) => MotionGate.FxTargetFps(tier);

            /// <summary>PerformanceProfile.AllowAmbientMotion: Core MotionGate.</summary>
            public static bool AllowAmbientMotion(PerformanceTier tier) => MotionGate.AllowAmbientMotion(tier);

            /// <summary>MotionFx.ResolveLevel: Core MotionGate (the OS preference only ever removes motion).</summary>
            public static MotionLevel ResolveLevel(MotionLevel setting, bool osAnimationsEnabled) =>
                MotionGate.ResolveLevel(setting, osAnimationsEnabled);

            /// <summary>
            /// MotionFx.Level: the user's setting capped by the desktop's reduced-motion flag
            /// (<see cref="OsReducedMotion"/>: Windows SPI_GETCLIENTAREAANIMATION, GNOME
            /// enable-animations, KDE AnimationDurationFactor).
            /// </summary>
            public static MotionLevel Level =>
                ResolveLevel(CoreSettings.Current.MotionLevel, OsReducedMotion.AnimationsEnabled);

            /// <summary>MotionFx.AllowAmbientLoops, now the real gate.</summary>
            public static bool AllowAmbientLoops => MotionGate.AllowAmbientLoops(Level, CurrentTier);

            /// <summary>MotionFx.AllowParticles.</summary>
            public static bool AllowParticles => MotionGate.AllowParticles(Level, CurrentTier);

            /// <summary>
            /// MotionFx.AllowTransitions. Interaction motion - hover, press, entrances, a stamp
            /// landing - is 80-400ms and runs at every level except Off, which is a different
            /// question from <see cref="AllowAmbientLoops"/> and must not be answered with it.
            /// </summary>
            public static bool AllowTransitions => MotionGate.AllowTransitions(Level);

            /// <summary>PerformanceProfile.AllowGlow, verbatim.</summary>
            public static bool AllowGlow(PerformanceTier tier) => tier != PerformanceTier.Performance;

            /// <summary>PerformanceProfile.MaxGlowBlurRadius, verbatim. Only consulted when
            /// <see cref="AllowGlow"/> already said yes, hence no Performance arm.</summary>
            public static double MaxGlowBlurRadius(PerformanceTier tier) => tier switch
            {
                PerformanceTier.Balanced => 18,
                _ => 24,
            };

            // FxTheme's colours are read, not computed: it writes four Color keys into the app
            // resources from the mod palette and every consumer reads them back. Theme/Colors.xaml
            // already carries the keys, so this is that same read path - and nothing on this head
            // writes them yet, which means the CCP default accent until an FxTheme twin lands.
            public static Color MistColor => ThemeColor("FxMistColor");
            public static Color ParticleColor => ThemeColor("FxParticleColor");
            public static Color GlowColor => ThemeColor("FxGlowColor");
            public static Color FlashTintColor => ThemeColor("FxFlashTintColor");

            /// <summary>
            /// FxTheme.MistOpacity. ponytail: the WPF original reads
            /// <c>App.Mods.GetMistOpacity()</c>; <see cref="CoreMods"/> exposes no mist-opacity
            /// provider, so there is nothing to call here yet - adding one is a Core change.
            /// 1.0 is what the WPF call answers with no mod loaded, so this is the no-mod value
            /// rather than an invented default.
            /// </summary>
            public static double MistOpacity => 1.0;

            /// <summary>FxTheme.Read's Avalonia twin: the same key out of the app resources.</summary>
            private static Color ThemeColor(string key)
            {
                try
                {
                    var app = Application.Current;
                    if (app != null && app.TryGetResource(key, app.ActualThemeVariant, out var v) && v is Color c)
                        return c;
                }
                catch { /* decoration; the fallback is FxTheme's own */ }
                return Fallback;
            }
        }
    }
}
