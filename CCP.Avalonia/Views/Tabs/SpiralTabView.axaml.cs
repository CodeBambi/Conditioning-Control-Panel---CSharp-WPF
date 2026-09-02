using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// THE SPIRAL ROOM — the tab that replaced <c>SpiralMapWindow</c> (CONTRACT-FUSE-0816 §2.4,
    /// owner ruling 2026-08-16). Three states, one surface.
    ///
    /// <para>PORTED from ConditioningControlPanel/Views/Tabs/SpiralTabView.xaml.cs. Everything the
    /// original does to dodge the airspace problem is done here too, and for the same reason: a
    /// native web view is a child surface that paints over every framework element in the window,
    /// so the embed is CONSTRUCTED on entering the spiral state and dropped on the way out rather
    /// than merely hidden.</para>
    ///
    /// <para><b>What is real on this head:</b> the three states and the selection between them, the
    /// fog era's copy and its FX (the hero's pulse, the hairline's breath, the embers), the splash
    /// (its spiral geometry, spin, breath and marquee), the waiting room's ambience, the embed
    /// slab, the help chip and the 32px inset that keeps it alive over a browser.</para>
    ///
    /// <para><b>What is stubbed, and why each one is a stub:</b></para>
    /// <list type="bullet">
    ///   <item><c>SpiralRoom.StateFor</c> and the four gates it reads (<c>App.Settings</c>,
    ///     <c>App.DescentCountdown</c>, <c>App.DescentMigration</c>, <c>App.Descent</c>) are all in
    ///     the WPF head, so <see cref="Refresh"/> cannot ask the world anything — see the
    ///     placeholder there.</item>
    ///   <item><c>SpiralFirstLightVisual</c> is a WPF <c>DrawingVisual</c>, so <c>FogHost</c> stays
    ///     empty and FIRST LIGHT hands straight back rather than holding a black room for its three
    ///     and a half seconds. The reveal's frame loop, its block wait and its timeout go with it;
    ///     all three exist only to drive that canvas.</item>
    ///   <item><c>SpiralEmbedView</c> (WebView2, Windows-only) becomes
    ///     <see cref="WebHost"/>, whose <c>Navigated</c>/<c>Failed</c>/<c>Ready</c>/<c>PostState</c>
    ///     have no twin yet — see <see cref="EnsureEmbed"/>.</item>
    ///   <item><c>MotionFx</c>, <c>DescentRoomSfx</c>, <c>DescentBarkWatcher</c> and
    ///     <c>HelpPopover</c> are WPF-head services; each is a stub at its call site.</item>
    ///   <item><c>DescentFuseCopy</c> is likewise still in the WPF head; its six lines for this
    ///     room are inlined verbatim below.</item>
    /// </list>
    ///
    /// <para><b>Escape is never handled here.</b> The global panic key must reach its hook
    /// untouched; nothing in this tab looks at the keyboard at all.</para>
    /// </summary>
    public partial class SpiralTabView : UserControl
    {
        /// <summary>ponytail: mirrors <c>SpiralRoomState</c> (WPF head, Services/Descent/SpiralRoom.cs).
        /// Delete this and use the real enum when that file moves to Core — the values are its.</summary>
        private enum RoomState
        {
            /// <summary>A ceremony is owed or scheduled: weather and a countdown, no spiral.</summary>
            Fog = 0,

            /// <summary>The gate is open but there is nothing to draw yet. A held promise.</summary>
            Waiting = 1,

            /// <summary>The spiral itself: the /embed/spiral canvas at ?mode=map.</summary>
            Spiral = 2,
        }

        // ============================== the copy ==============================
        //
        // ponytail: needs DescentFuseCopy (WPF head, Services/Descent), inlined verbatim until it
        // moves to Core. Hardcoded English by contract (CONTRACT-FUSE-0816 §4) in both heads, so
        // there is no loc key to reference and inventing one would be worse than the copy.

        private const string FogEyebrowCopy = "a door you haven't opened yet";

        private const string FogLineCopy =
            "The fog isn't hiding something from you. It's keeping something for you.";

        private const string FogTailCopy =
            "When the clock runs out, you'll be shown in. Until then, keep your devotion where it belongs.";

        /// <summary>What stands where the digits were once the instant has passed. NOT a duration
        /// and NOT an apology: the server re-offers on every sync.</summary>
        private const string FogImminentCopy = "any moment now.";

        /// <summary>The waiting room's one line. It must never grow a button.</summary>
        private const string WaitingLineCopy = "the spiral is finding you.";

        /// <summary>The splash's one line, lower case to match the waiting room's register — the
        /// same voice saying the same kind of thing, one step earlier.</summary>
        private const string SplashCopy = "opening the spiral";

        /// <summary>The map route, verbatim from <c>SpiralEmbedView.BuildUrl("map")</c>. Navigation
        /// is locked to this origin over there; the WebHost has no such lock yet.</summary>
        private const string EmbedUrl = "https://app.cclabs.app/embed/spiral?mode=map";

        // ============================== the fog era's FX ==============================
        //
        // Owner verdict on the live demo, 2026-08-16: "could use some FX, a bolder text, maybe some
        // little animation and flair." Everything below is opacity and transform ONLY — the two
        // BlurEffects in the XAML are set once and never touched by a clock, which is the app's
        // standing rule (an animated Effect property re-runs the shader graph every frame).

        /// <summary>The hero digits, at their own size. The "any moment now" phrase shares the
        /// TextBlock and gets <see cref="ImminentFontSize"/> instead: it is a sentence, not a
        /// readout, and at digit size it would wrap and stop being a hero at all.</summary>
        private const double HeroFontSize = 56;
        private const double ImminentFontSize = 30;

        /// <summary>The pulse's whole amplitude. Two percent is the ceiling the owner named.</summary>
        private const double PulseScale = 1.022;

        /// <summary>
        /// The pulse's HALF cycle. ponytail: needs <c>SpiralRoom.FogPulseSecondsFor(phase)</c>, whose
        /// tempo ladder runs 2.8s → 0.52s as the phase closes in. <c>DescentFusePhase</c> is a WPF
        /// head type, so this head cannot know the phase and takes that function's own default rung
        /// (the <c>_ =&gt; 3.0</c> arm, which is where a dark fuse lands anyway).
        /// </summary>
        private const double FogPulseSeconds = 3.0;

        /// <summary>The glow's resting opacity, and the top of its one-shot flare.</summary>
        private const double GlowRest = 0.42;
        private const double GlowFlarePeak = 1.0;
        private const double GlowFlareSeconds = 0.95;

        private const double HairlineLo = 0.14;
        private const double HairlineHi = 0.46;
        private const double HairlineSeconds = 3.6;

        /// <summary>
        /// The embers, as fractions of the fog's own rectangle so they reflow with the window
        /// instead of clustering in a corner on a wide monitor. Curated rather than random: a seeded
        /// RNG would still be one more thing that can differ between two machines watching the same
        /// night, and nine specks is few enough to place by hand.
        ///
        /// <para>Tuple order: x fraction, y fraction, radius, the drift's period in seconds, and the
        /// peak opacity at the middle of that drift.</para>
        /// </summary>
        private static readonly (double Fx, double Fy, double R, double Seconds, double Peak)[] EmberSeeds =
        {
            (0.14, 0.86, 1.6, 19.0, 0.42),
            (0.27, 0.94, 2.4, 24.0, 0.30),
            (0.39, 0.80, 1.3, 16.5, 0.50),
            (0.52, 0.97, 2.0, 27.0, 0.26),
            (0.63, 0.84, 1.5, 21.0, 0.44),
            (0.74, 0.92, 2.6, 25.5, 0.24),
            (0.83, 0.78, 1.4, 17.5, 0.48),
            (0.91, 0.95, 1.9, 22.5, 0.32),
            (0.06, 0.90, 2.1, 28.0, 0.22),
        };

        /// <summary>The waiting panel's motes, in the little canvas's own coordinates rather than
        /// in fractions — that canvas has a fixed size, so there is nothing to reflow.</summary>
        private static readonly (double Fx, double Fy, double R, double Seconds, double Peak)[] MoteSeeds =
        {
            (0.12, 1.0, 1.5, 5.6, 0.50),
            (0.31, 1.0, 1.1, 7.4, 0.62),
            (0.50, 1.0, 1.8, 6.3, 0.38),
            (0.69, 1.0, 1.2, 8.1, 0.55),
            (0.88, 1.0, 1.5, 6.9, 0.44),
        };

        // ============================== the splash ==============================

        /// <summary>
        /// How long the splash will wait for an embed that has neither navigated nor failed before
        /// giving the browser its airspace anyway.
        ///
        /// <para><b>It exists so the splash cannot become the hang it was built to hide.</b> The
        /// embed is held invisible while it loads, and an engine that somehow never finishes (a
        /// wedged renderer, a runtime mid-update) would strand the spiral behind a spinning glyph
        /// with nothing on screen ever changing. On this deadline the room shows whatever the
        /// browser actually has, which is either the canvas or its own dark slab — and the slab at
        /// least tells the truth.</para>
        /// </summary>
        private static readonly TimeSpan SplashRevealDeadline = TimeSpan.FromSeconds(10);

        private const double SplashFadeSeconds = 0.4;

        // ============================== motion ==============================

        /// <summary>
        /// ponytail: needs MotionFx (WPF head, Services/MotionFx.cs), wired when it moves to Core.
        /// Full motion is the honest default — a stub that claimed "reduced" would silently drop
        /// every loop in this file on every machine. The two gates are kept apart because the
        /// original distinguishes them: loops are ambient, the flare and the splash fade are
        /// transitions.
        /// </summary>
        private static readonly bool AllowAmbientLoops = true;
        private static readonly bool AllowTransitions = true;

        // ---- the parts ----
        private readonly Canvas _emberHost;
        private readonly StackPanel _fogCopy;
        private readonly Grid _fogHost;
        private readonly Grid _fogDigitsHost;
        private readonly TextBlock _fogDigits;
        private readonly TextBlock _fogDigitsGlow;
        private readonly Rectangle _fogHairline;
        private readonly Grid _embedHost;
        private readonly Grid _spiralSplash;
        private readonly Ellipse _splashHalo;
        private readonly Path _splashGlyph;
        private readonly TextBlock _splashDot1, _splashDot2, _splashDot3;
        private readonly Border _waitingPanel;
        private readonly TextBlock _waitingGlow;
        private readonly Canvas _waitingMotes;
        private readonly Button _btnSpiralHelp;

        private readonly DriftField _embers;
        private readonly DriftField _motes;

        // ---- clocks ----
        private CancellationTokenSource? _fogFx;
        private CancellationTokenSource? _splashFx;
        private CancellationTokenSource? _waitFx;
        private DispatcherTimer? _splashWatchdog;

        /// <summary>True while the splash owns the surface — set the instant the spiral era is
        /// painted, cleared when the embed is revealed, when it fails, or on leaving the tab.</summary>
        private bool _splashUp;

        /// <summary>The embed, for as long as this tab is the one on screen in the spiral state.</summary>
        private WebHost? _embed;

        private RoomState _state = RoomState.Waiting;

        public SpiralTabView()
        {
            AvaloniaXamlLoader.Load(this);

            _fogHost = this.FindControl<Grid>("FogHost")!;
            _emberHost = this.FindControl<Canvas>("EmberHost")!;
            _fogCopy = this.FindControl<StackPanel>("FogCopy")!;
            _fogDigits = this.FindControl<TextBlock>("FogDigits")!;
            _fogDigitsGlow = this.FindControl<TextBlock>("FogDigitsGlow")!;
            _fogHairline = this.FindControl<Rectangle>("FogHairline")!;
            _embedHost = this.FindControl<Grid>("EmbedHost")!;
            _spiralSplash = this.FindControl<Grid>("SpiralSplash")!;
            _splashHalo = this.FindControl<Ellipse>("SplashHalo")!;
            _splashGlyph = this.FindControl<Path>("SplashGlyph")!;
            _splashDot1 = this.FindControl<TextBlock>("SplashDot1")!;
            _splashDot2 = this.FindControl<TextBlock>("SplashDot2")!;
            _splashDot3 = this.FindControl<TextBlock>("SplashDot3")!;
            _waitingPanel = this.FindControl<Border>("WaitingPanel")!;
            _waitingGlow = this.FindControl<TextBlock>("WaitingGlow")!;
            _waitingMotes = this.FindControl<Canvas>("WaitingMotes")!;
            _btnSpiralHelp = this.FindControl<Button>("BtnSpiralHelp")!;

            _fogDigitsHost = this.FindControl<Grid>("FogDigitsHost")!;

            this.FindControl<TextBlock>("FogEyebrow")!.Text = FogEyebrowCopy;
            this.FindControl<TextBlock>("FogLine")!.Text = FogLineCopy;
            this.FindControl<TextBlock>("FogTail")!.Text = FogTailCopy;
            this.FindControl<TextBlock>("WaitingLine")!.Text = WaitingLineCopy;
            this.FindControl<TextBlock>("SplashLine")!.Text = SplashCopy;

            _splashGlyph.Data = BuildSpiralGeometry();

            _embers = new DriftField(_emberHost, EmberSeeds);
            _motes = new DriftField(_waitingMotes, MoteSeeds);

            // The embers are laid out in fractions of the fog's rectangle, so they follow the
            // window. WPF's SizeChanged is a Bounds change here.
            _emberHost.PropertyChanged += (_, e) =>
            {
                if (e.Property == BoundsProperty) _embers.Reflow();
            };

            // WPF's Loaded/Unloaded pair. Detach is the window going away or this view being
            // re-parented, and it is also how --render-all disposes a view: every clock in this
            // file has to stop there or the next render inherits it.
            AttachedToVisualTree += (_, _) => Refresh();
            DetachedFromVisualTree += (_, _) => Suspend();

            // The tab system shows and hides rather than rebuilding, so this is the real entry and
            // exit hook — same shape as the WPF IsVisibleChanged. Leaving takes the browser with it.
            PropertyChanged += (_, e) =>
            {
                if (e.Property != IsVisibleProperty) return;
                if (IsVisible) Refresh();
                else Suspend();
            };
        }

        /// <summary>
        /// The tab was navigated to. Called from <c>ShowTab("spiral")</c>, which can happen at any
        /// time and from anywhere (the rail entry, the fuse chip, the profile plate, the account
        /// menu, a first light), so the room is re-read from scratch rather than trusted from
        /// whenever this view was last painted.
        ///
        /// <para>ponytail: needs <c>DescentBarkWatcher.NotifySpiralOpened()</c> (WPF head), which the
        /// original calls here and on every entry to let the companion say hello to the room once
        /// ever. It is decoration and it never blocks the entry it rides on.</para>
        /// </summary>
        internal void OnTabShown() => Refresh();

        /// <summary>Park everything: no clocks, no browser, nothing holding a frame.</summary>
        private void Suspend()
        {
            StopFogFx();
            HideSplash(fade: false);
            StopWaitingAmbience();
            TeardownEmbed();
        }

        // ============================== the state ==============================

        /// <summary>
        /// Read the world, ask <c>SpiralRoom</c>, paint the answer. The whole surface is computed
        /// from scratch every time so that arriving in any state lands on a correct, complete room
        /// rather than on the accumulated result of the transitions taken to get here.
        /// </summary>
        private void Refresh()
        {
            // ponytail: needs SpiralRoom.StateFor(settings, phase, fuseArmed, spiralWithheld,
            // hasBlock) plus App.Settings / App.DescentCountdown / App.DescentMigration /
            // App.Descent — all WPF head, wired when Services/Descent moves to Core. WPF's own
            // "the predicate threw" fallback is Waiting; this head has no world to read at all, so
            // the state below is a PLACEHOLDER FOR THE RENDER PROOF and Spiral is the choice that
            // exercises the embed seam. Delete this line and call StateFor the moment it lands.
            ApplyState(RoomState.Spiral);
        }

        private void ApplyState(RoomState state)
        {
            _state = state;

            // THE "?" FOLLOWS THE STATE. It belongs to the two states that are a room you are being
            // asked to make sense of, and to neither of the two that are a ceremony: the fog says
            // nothing is clickable and means it, and the reveal is a one-shot the user should not be
            // able to interrupt with a help card.
            ApplyHelpChip(state != RoomState.Fog);

            switch (state)
            {
                case RoomState.Fog:
                    // AIRSPACE: the browser must not exist while the fog is up.
                    TeardownEmbed();
                    _embedHost.IsVisible = false;
                    _waitingPanel.IsVisible = false;
                    StopWaitingAmbience();
                    HideSplash(fade: false);

                    _fogHost.IsVisible = true;
                    _fogCopy.IsVisible = true;
                    ApplyFogReadout();
                    StartFogFx();
                    // ponytail: needs DescentRoomSfx.PlayFogEntry() (WPF head), one door sound per
                    // entry. Audio is decoration; the room paints without it.
                    break;

                case RoomState.Spiral:
                    StopFogFx();
                    _fogHost.IsVisible = false;
                    _fogCopy.IsVisible = false;

                    // A REPAINT IS NOT A RE-ENTRY. This state is re-applied on every block change,
                    // and the spiral era is exactly where those keep arriving — so once this entry
                    // has a browser, whatever state it reached (loading behind the splash, revealed,
                    // or handed to the waiting panel because it failed) is the state a repaint must
                    // leave alone. Without this, a routine sync would drop the splash back over a
                    // canvas that had already been revealed, with no watchdog left to lift it.
                    if (_embed != null) break;

                    _waitingPanel.IsVisible = false;
                    StopWaitingAmbience();

                    if (WebHost.IsAvailable)
                    {
                        // AIRSPACE, one layer down. The slab is arranged either way, so the engine
                        // is handed a real rectangle and loads exactly as it would otherwise, but
                        // it cannot paint over the splash while it does.
                        // ponytail: WPF parked this at Visibility.Hidden — arranged but with no
                        // airspace — which has no clean twin over a NativeControlHost. IsVisible
                        // false is the nearest thing; a host with an engine should check that the
                        // page still loads while the splash is up.
                        _embedHost.IsVisible = false;
                        ShowSplash();
                        EnsureEmbed();
                        // Only if there is something to wait for: a build that threw has already
                        // handed the room to the waiting panel and stopped this watchdog once.
                        if (_embed != null) StartSplashWatchdog();
                    }
                    else
                    {
                        // NO ENGINE, NOTHING TO HIDE. The splash exists to cover a native surface
                        // painting its own dark slab; WebHost with no engine paints a legible panel
                        // naming the missing library instead, so raising a splash over it would be
                        // covering the one honest thing on screen. Reveal at once.
                        HideSplash(fade: false);
                        EnsureEmbed();
                        _embedHost.IsVisible = _embed != null;
                    }
                    break;

                default:
                    StopFogFx();
                    TeardownEmbed();
                    HideSplash(fade: false);
                    _fogHost.IsVisible = false;
                    _fogCopy.IsVisible = false;
                    _embedHost.IsVisible = false;
                    _waitingPanel.IsVisible = true;
                    StartWaitingAmbience();
                    break;
            }
        }

        /// <summary>
        /// Is the room actually SHOWING the spiral right now? The gate the first-open intro card
        /// stands on, and it is deliberately the painted state rather than "does a block exist":
        /// a card that explained banked days over a fog layer would be describing a map the user
        /// has not been given yet.
        /// </summary>
        internal bool IsShowingSpiral => _state == RoomState.Spiral;

        /// <summary>
        /// THE FIRST LIGHT — the one-shot reveal the original plays inside this tab the moment the
        /// user's own ceremony commits (owner ruling 2026-08-16). Called by
        /// <c>DescentShowDirector</c> after it has brought the window forward and navigated here.
        ///
        /// <para>ponytail: needs Controls/SpiralFirstLightVisual (a WPF DrawingVisual) — the reveal
        /// IS that canvas, and without it there is nothing to play. It fails SOFT the way every
        /// other first-light failure does in the original (a thrown frame, a withdrawn block, a
        /// twelve-second timeout): hand the room straight back to the ordinary selection rather
        /// than hold a black rectangle for three and a half seconds. Nothing is lost — the withhold
        /// is already open by the time this is called, so every ordinary door into the spiral
        /// works.</para>
        /// </summary>
        internal void BeginFirstLight()
        {
            Log.Information("[Spiral] first light: no canvas on this head - handing the room back quietly.");
            Refresh();
        }

        // ============================== the "?" ==============================

        /// <summary>
        /// Show or hide the help chip.
        ///
        /// <para>ponytail: needs Controls/HelpPopover + HelpContent (WPF head), wired when the
        /// popover moves to Core. The original attaches it lazily the first time the chip is
        /// wanted, with a topic built at attach time out of Loc: icon "◌", title
        /// <c>help_descent_title</c>, body <c>help_descent_what</c>, three tips
        /// <c>help_descent_tip_1..3</c> and <c>help_descent_how</c> — so the topic is translated on
        /// its first day. No Loc call is made here because nothing on this head would display the
        /// result. The chip itself, its skin and its state gating are real.</para>
        /// </summary>
        private void ApplyHelpChip(bool show) => _btnSpiralHelp.IsVisible = show;

        // ============================== the fog readout ==============================

        /// <summary>
        /// The fog's readout: T-minus while the fuse is still burning, and a phrase once the instant
        /// has gone by without this account's ceremony having reached them. A countdown that ran out
        /// and kept showing 00:00 would read as broken; "any moment now" is what is actually true,
        /// because the server re-offers on every sync until a choice is taken.
        ///
        /// <para>ponytail: needs App.DescentCountdown (WPF head) for the remaining time and
        /// <c>DescentFuseCopy.TMinus</c> to format it. With no fuse to ask, this lands on exactly
        /// the branch the original takes for a fuse that is null or spent — which is the honest one
        /// for a head that cannot see the clock.</para>
        /// </summary>
        private void ApplyFogReadout() => ApplyReadout(FogImminentCopy, hero: false);

        /// <summary>
        /// Type the readout and size it for what it actually is.
        ///
        /// <para><b>The size is passed in, never sniffed off the string.</b> Both callers know which
        /// of the two things they are showing, and a "does it start with a digit" test would be one
        /// copy edit away from rendering a sentence at 56px bold — where it wraps, breaks the
        /// StackPanel's rhythm and stops looking like anything the app meant to draw.</para>
        ///
        /// <para>The blurred glow behind it follows both the text and the size by binding, so there
        /// is nothing to keep in step here.</para>
        /// </summary>
        private void ApplyReadout(string text, bool hero)
        {
            _fogDigits.Text = text;
            _fogDigits.FontSize = hero ? HeroFontSize : ImminentFontSize;
        }

        // ============================== the embed ==============================

        /// <summary>
        /// Build the browser, once, for as long as this tab is the one on screen in the spiral
        /// state.
        ///
        /// <para>ponytail: <c>SpiralEmbedView</c> (WebView2, Windows-only) becomes
        /// <see cref="WebHost"/>, and four of its five members have no twin yet — <c>Start()</c>
        /// (the runtime/environment handshake), <c>Navigated</c> (the splash's cue),
        /// <c>Failed</c> (which handed the room to the waiting panel and latched
        /// <c>_embedGaveUp</c> for the rest of the entry) and <c>PostState(block)</c> (which pushes
        /// the descent block into the canvas). The URL is the real one and the lifecycle — built on
        /// entering the state, dropped on leaving it — is the real one.</para>
        /// </summary>
        private void EnsureEmbed()
        {
            if (_embed != null) return;
            if (!IsVisible) return;

            try
            {
                var embed = new WebHost { Source = new Uri(EmbedUrl) };
                _embed = embed;
                _embedHost.Children.Add(embed);
            }
            catch (Exception ex)
            {
                // Every failure ends on the waiting room — which is TODAY'S LIVE PATH, because the
                // canvas's ?mode=map route has not deployed yet. That is why the fallback is a room
                // rather than an apology.
                Log.Debug("[Spiral] room embed could not start: {E}", ex.Message);
                TeardownEmbed();
                ShowWaitingUnderSpiral();
            }
        }

        /// <summary>The spiral state with no canvas in it. Deliberately NOT a state change: the
        /// gates still say "spiral", and the next entry retries.</summary>
        private void ShowWaitingUnderSpiral()
        {
            if (_state != RoomState.Spiral) return;
            StopSplashWatchdog();
            // No fade here, unlike the successful path: the splash is handing over to another
            // held-promise panel rather than to a finished spiral, and cross-fading one apology
            // into another just draws the eye to the swap.
            HideSplash(fade: false);
            _embedHost.IsVisible = false;
            _waitingPanel.IsVisible = true;
            StartWaitingAmbience();
        }

        private void TeardownEmbed()
        {
            StopSplashWatchdog();
            if (_embed == null) return;
            try
            {
                _embedHost.Children.Remove(_embed);
            }
            catch (Exception ex) { Log.Debug("[Spiral] room embed teardown: {E}", ex.Message); }
            _embed = null;
        }

        private void StartSplashWatchdog()
        {
            StopSplashWatchdog();
            _splashWatchdog = new DispatcherTimer(DispatcherPriority.Normal)
            { Interval = SplashRevealDeadline };
            _splashWatchdog.Tick += OnSplashDeadline;
            _splashWatchdog.Start();
        }

        private void StopSplashWatchdog()
        {
            if (_splashWatchdog == null) return;
            try
            {
                _splashWatchdog.Stop();
                _splashWatchdog.Tick -= OnSplashDeadline;
            }
            catch (Exception ex) { Log.Debug("[Spiral] splash watchdog stop: {E}", ex.Message); }
            _splashWatchdog = null;
        }

        /// <summary>
        /// The embed said nothing inside the deadline. Reveal it anyway: whatever the browser has is
        /// more honest than a splash that has stopped meaning "loading" and started meaning "hung".
        ///
        /// <para>With no <c>Navigated</c> event to promote the embed early, this deadline is
        /// currently the ONLY thing that lifts the splash on a machine that has an engine.</para>
        /// </summary>
        private void OnSplashDeadline(object? sender, EventArgs e)
        {
            StopSplashWatchdog();
            if (_state != RoomState.Spiral || _embed is null) return;

            Log.Information(
                "[Spiral] the embed said nothing within {Seconds}s - revealing it behind the splash anyway.",
                (int)SplashRevealDeadline.TotalSeconds);

            try
            {
                _embedHost.IsVisible = true;
                HideSplash(fade: true);
            }
            catch (Exception ex) { Log.Debug("[Spiral] splash deadline: {E}", ex.Message); }
        }

        // ============================== the fog era's FX ==============================

        /// <summary>
        /// Light the fog's own clocks: the hero's heartbeat, the hairline's breath, the embers.
        ///
        /// <para>Unlike the original this is NOT idempotent-by-phase, because there is no phase on
        /// this head to compare against (see <see cref="FogPulseSeconds"/>). It restarts the loops,
        /// so it must only be called on entering the fog rather than on every repaint — which is
        /// what <see cref="ApplyState"/> does.</para>
        /// </summary>
        private void StartFogFx()
        {
            StopFogFx();

            if (!AllowAmbientLoops)
            {
                // Reduced motion gets the LOOK and none of the clocks: the layered glow, the
                // hairline and the bold digits are all still there, simply held still. The hero is
                // information; only its heartbeat is decoration.
                _fogDigitsGlow.Opacity = GlowRest;
                _fogHairline.Opacity = HairlineHi;
                return;
            }

            _emberHost.IsVisible = true;
            _fogFx = new CancellationTokenSource();
            var token = _fogFx.Token;

            _embers.Start(token);
            Breathe(_fogHairline, OpacityProperty, HairlineLo, HairlineHi, HairlineSeconds, token);

            // Half a breath in, half a breath out, on the host's whole RenderTransform.
            //
            // Both scales ride one animation so they cannot drift apart. It targets the HOST, not
            // a transform - see Pulse for why, and for why the XAML declares no transform at all.
            Pulse(_fogDigitsHost, FogPulseSeconds, PulseScale, token);
        }

        /// <summary>
        /// Kill every fog clock and park the parts at rest. The explicit resting values are what the
        /// original's <c>BeginAnimation(…, null)</c> calls were for: without them the last animated
        /// value sticks, and a tab left mid-breath comes back very slightly too large forever.
        /// </summary>
        private void StopFogFx()
        {
            try
            {
                Cancel(ref _fogFx);

                _fogDigitsHost.RenderTransform = null;
                _fogDigitsGlow.Opacity = GlowRest;
                _fogHairline.Opacity = HairlineHi;

                _embers.Stop();
                _emberHost.IsVisible = false;
            }
            catch (Exception ex) { Log.Debug("[Spiral] fog fx stop: {E}", ex.Message); }
        }

        /// <summary>
        /// THE PHASE FLARE — one glow swell when the countdown crosses into a nearer phase, and the
        /// only moment in the fog era that is an event rather than a loop.
        ///
        /// <para>It is the blurred duplicate's OPACITY that swells, not the blur radius and not the
        /// digits' size: the effect underneath it is set once and never animated, and the letters
        /// themselves never move, so a glance away and back does not find the readout a different
        /// shape.</para>
        ///
        /// <para>ponytail: its one caller is the fuse's <c>PhaseChanged</c> handler, which needs
        /// App.DescentCountdown (WPF head). The flare itself is ported and ready for it.</para>
        /// </summary>
        private void FlarePhaseChange()
        {
            try
            {
                if (!AllowTransitions) return;

                // The end value is the XAML's resting 0.42, so the property is handed back at rest
                // whether or not the ramp is allowed to fill — the original used FillBehavior.Stop
                // for exactly this.
                var flare = new Animation
                {
                    Duration = TimeSpan.FromSeconds(GlowFlareSeconds),
                    Easing = new SineEaseInOut(),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, GlowRest) } },
                        new KeyFrame { Cue = new Cue(0.22d), Setters = { new Setter(OpacityProperty, GlowFlarePeak) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, GlowRest) } },
                    },
                };
                _ = flare.RunAsync(_fogDigitsGlow, _fogFx?.Token ?? CancellationToken.None);
            }
            catch (Exception ex) { Log.Debug("[Spiral] phase flare: {E}", ex.Message); }
        }

        // ============================== the splash ==============================

        /// <summary>
        /// Raise the splash over the spiral era and start its clocks. Called every time the spiral
        /// state is painted with an engine behind it, so leaving the tab and coming back gets a
        /// fresh splash rather than the faded-out remains of the last one — which is why the opacity
        /// is reset here and not only on the way down.
        /// </summary>
        private void ShowSplash()
        {
            try
            {
                _splashUp = true;
                Cancel(ref _splashFx);
                _spiralSplash.Opacity = 1;
                _spiralSplash.IsVisible = true;

                // ponytail: needs DescentRoomSfx.PlaySplashOpen() (WPF head), one chime per ENTRY
                // and never per retry: a cue that repeated while a browser struggled would sound
                // exactly like the thing being stuck.

                if (!AllowAmbientLoops)
                {
                    // Held still, not hidden: somebody who turned motion off still has to be able to
                    // tell that something is happening, and the line plus the glyph say so.
                    _splashGlyph.RenderTransform = null;
                    _splashHalo.Opacity = 0.16;
                    _splashGlyph.Opacity = 0.92;
                    _splashDot1.Opacity = _splashDot2.Opacity = _splashDot3.Opacity = 0.6;
                    return;
                }

                _splashFx = new CancellationTokenSource();
                var token = _splashFx.Token;

                // One turn every 4.6s, about the glyph's own centre (Avalonia's default origin).
                var spin = new Animation
                {
                    Duration = TimeSpan.FromSeconds(4.6),
                    IterationCount = IterationCount.Infinite,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(RotateTransform.AngleProperty, 0d) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(RotateTransform.AngleProperty, 360d) } },
                    },
                };
                _ = spin.RunAsync(_splashGlyph, token);

                Breathe(_splashHalo, OpacityProperty, 0.08, 0.30, 2.2, token);
                Breathe(_splashGlyph, OpacityProperty, 0.58, 0.96, 1.7, token);

                // The ellipsis, one dot at a time. Same clock, three offsets - a marquee rather than
                // three independent loops that would drift out of order within a minute.
                BeginDot(_splashDot1, 0.00, token);
                BeginDot(_splashDot2, 0.42, token);
                BeginDot(_splashDot3, 0.84, token);
            }
            catch (Exception ex) { Log.Debug("[Spiral] splash up: {E}", ex.Message); }
        }

        private static void BeginDot(Visual dot, double offsetSeconds, CancellationToken token)
        {
            const double cycle = 1.45;
            var anim = new Animation
            {
                Duration = TimeSpan.FromSeconds(cycle),
                IterationCount = IterationCount.Infinite,
                Delay = TimeSpan.FromSeconds(offsetSeconds),
                Easing = new SineEaseInOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 0.15) } },
                    new KeyFrame { Cue = new Cue(0.34d), Setters = { new Setter(OpacityProperty, 0.95) } },
                    new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 0.15) } },
                },
            };
            _ = anim.RunAsync(dot, token);
        }

        /// <summary>
        /// Take the splash down — faded when it is handing over to a finished spiral, instantly when
        /// it is handing over to the waiting panel or leaving the tab. Idempotent, and safe to call
        /// on a splash that was never up.
        /// </summary>
        private void HideSplash(bool fade)
        {
            try
            {
                if (!_splashUp && !_spiralSplash.IsVisible) return;
                _splashUp = false;

                Cancel(ref _splashFx);
                _splashGlyph.RenderTransform = null;

                if (!fade || !AllowTransitions)
                {
                    _spiralSplash.Opacity = 1;   // the resting value, for the next time it is raised
                    _spiralSplash.IsVisible = false;
                    return;
                }

                _splashFx = new CancellationTokenSource();
                var token = _splashFx.Token;
                var fadeOut = new Animation
                {
                    Duration = TimeSpan.FromSeconds(SplashFadeSeconds),
                    Easing = new QuadraticEaseOut(),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 1d) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 0d) } },
                    },
                };
                CollapseAfterFade(fadeOut, token);
            }
            catch (Exception ex) { Log.Debug("[Spiral] splash down: {E}", ex.Message); }
        }

        /// <summary>
        /// WPF's <c>Completed</c> handler on the fade. Awaited rather than continued, so the tail
        /// resumes on the UI thread that started it — every line of it touches the tree.
        /// </summary>
        private async void CollapseAfterFade(Animation fadeOut, CancellationToken token)
        {
            try { await fadeOut.RunAsync(_spiralSplash, token); }
            catch (Exception ex) { Log.Debug("[Spiral] splash fade: {E}", ex.Message); return; }

            // A re-entry can raise the splash again inside these 400ms; if it did, _splashUp is
            // true again and this stale completion must not collapse the new one.
            if (_splashUp || token.IsCancellationRequested) return;
            try
            {
                _spiralSplash.Opacity = 1;
                _spiralSplash.IsVisible = false;
            }
            catch { /* the window went away under a 400ms fade */ }
        }

        /// <summary>
        /// The splash's glyph: an archimedean spiral, three turns, drawn as one open polyline. A
        /// StreamGeometry rather than a path string for the same reason the rail chip's clock is —
        /// the shape is arithmetic, and arithmetic is easier to re-tune than a mini-language. It is
        /// immutable once closed, which is what WPF's Freeze() bought.
        /// </summary>
        private static Geometry BuildSpiralGeometry()
        {
            const double turns = 3.0;
            const int steps = 200;
            const double maxRadius = 38;
            var centre = new Point(40, 40);

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(centre, isFilled: false);
                for (int i = 1; i <= steps; i++)
                {
                    double t = i / (double)steps;
                    double angle = t * turns * 2 * Math.PI;
                    double radius = t * maxRadius;
                    ctx.LineTo(new Point(centre.X + radius * Math.Cos(angle),
                                         centre.Y + radius * Math.Sin(angle)));
                }
                ctx.EndFigure(false);
            }
            return geo;
        }

        // ============================== the waiting room's ambience ==============================

        /// <summary>
        /// Give the held promise a pulse. The line breathes and a few motes drift up behind it,
        /// because a panel that says "the spiral is finding you" and then sits perfectly still reads
        /// as a panel that stopped looking — which is the exact impression this whole pass exists to
        /// remove.
        /// </summary>
        private void StartWaitingAmbience()
        {
            try
            {
                StopWaitingAmbience();

                if (!AllowAmbientLoops)
                {
                    _waitingGlow.Opacity = 0.5;
                    return;
                }

                _waitFx = new CancellationTokenSource();
                var token = _waitFx.Token;
                Breathe(_waitingGlow, OpacityProperty, 0.18, 0.70, 3.1, token);
                _motes.Start(token);
            }
            catch (Exception ex) { Log.Debug("[Spiral] waiting ambience: {E}", ex.Message); }
        }

        private void StopWaitingAmbience()
        {
            try
            {
                Cancel(ref _waitFx);
                _waitingGlow.Opacity = 0.5;
                _motes.Stop();
            }
            catch (Exception ex) { Log.Debug("[Spiral] waiting ambience stop: {E}", ex.Message); }
        }

        // ============================== the loop helpers ==============================

        /// <summary>
        /// <c>MotionFx.GlowBreath</c> in Avalonia's terms: <paramref name="halfCycleSeconds"/> is
        /// the time from <paramref name="min"/> to <paramref name="max"/>, and Alternate does what
        /// WPF's AutoReverse did — which is why the number is HALF the breath, exactly as it is in
        /// the original's call sites.
        /// </summary>
        private static void Breathe(Animatable target, AvaloniaProperty property,
                                    double min, double max, double halfCycleSeconds,
                                    CancellationToken token)
        {
            var anim = new Animation
            {
                Duration = TimeSpan.FromSeconds(halfCycleSeconds),
                IterationCount = IterationCount.Infinite,
                PlaybackDirection = PlaybackDirection.Alternate,
                Easing = new SineEaseInOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(property, min) } },
                    new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(property, max) } },
                },
            };
            _ = anim.RunAsync(target, token);
        }

        /// <summary>
        /// THE ONE TRANSFORM RULE ON THIS HEAD, learned the hard way and worth writing down.
        ///
        /// <para>WPF called <c>BeginAnimation</c> ON the named transform. Avalonia's animator for a
        /// transform property does the finding itself: it takes the CONTROL, and if that control has
        /// no <c>RenderTransform</c> it installs a group holding one of each and animates the child
        /// whose type owns the property. So every transform animation here targets the control and
        /// leaves <c>RenderTransform</c> unset in the XAML - handing it the transform object throws
        /// (a Transform is not a Visual), and animating <c>RenderTransform</c> itself needs an
        /// animator nothing registers by default.</para>
        ///
        /// <para>The resting state is <c>RenderTransform = null</c>, which is what the stop paths
        /// set: the animator's own group goes with it.</para>
        /// </summary>
        private static void Pulse(Animatable target, double halfCycleSeconds, double scale,
                                  CancellationToken token)
        {
            var anim = new Animation
            {
                Duration = TimeSpan.FromSeconds(halfCycleSeconds),
                IterationCount = IterationCount.Infinite,
                PlaybackDirection = PlaybackDirection.Alternate,
                Easing = new SineEaseInOut(),
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0d),
                        Setters =
                        {
                            new Setter(ScaleTransform.ScaleXProperty, 1.0),
                            new Setter(ScaleTransform.ScaleYProperty, 1.0),
                        },
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1d),
                        Setters =
                        {
                            new Setter(ScaleTransform.ScaleXProperty, scale),
                            new Setter(ScaleTransform.ScaleYProperty, scale),
                        },
                    },
                },
            };
            _ = anim.RunAsync(target, token);
        }

        /// <summary>Cancel and clear one clock group. The resting values are the caller's job.</summary>
        private static void Cancel(ref CancellationTokenSource? cts)
        {
            var live = cts;
            cts = null;
            if (live == null) return;
            try { live.Cancel(); live.Dispose(); }
            catch { /* a token source disposed under a teardown race */ }
        }

        // ============================== the drift field ==============================

        /// <summary>
        /// A handful of specks drifting up through a Canvas, built once and started/stopped with the
        /// state that owns them. Used twice: the fog's embers (which reflow with the window) and the
        /// waiting panel's motes (which do not, because that canvas has a fixed size).
        ///
        /// <para><b>Transform and opacity only, and one clock per speck.</b> Nothing here touches
        /// Canvas.Top with an animation — that is a layout property, and animating layout is how the
        /// app has burned a frame budget before. The specks are POSITIONED by Canvas.Left/Top once
        /// per size change and MOVED by a TranslateTransform.</para>
        ///
        /// <para><b>It parks empty.</b> Stop() cancels every clock and drops the specks to zero
        /// opacity, so a collapsed field costs a few brushes and nothing else.</para>
        /// </summary>
        private sealed class DriftField
        {
            /// <summary>The fuse's gold, and never the mod accent — same law as every other fuse
            /// surface. Immutable, which is what WPF's Freeze() bought.</summary>
            private static readonly IBrush Gold = new ImmutableSolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x52));

            /// <summary>How far a speck travels in one cycle. Fixed device-independent pixels rather
            /// than a fraction of the host, so a resize repositions the field without having to
            /// rebuild every clock in it.</summary>
            private const double RiseDistance = 190;

            private readonly Canvas _host;
            private readonly (double Fx, double Fy, double R, double Seconds, double Peak)[] _seeds;
            private readonly List<Ellipse> _dots = new();
            private bool _running;

            /// <param name="seeds">Positions are FRACTIONS of the host's size, which is what lets
            /// the same field serve a full-bleed canvas that follows the window and a fixed 216px
            /// one inside a Border.</param>
            internal DriftField(Canvas host,
                                (double Fx, double Fy, double R, double Seconds, double Peak)[] seeds)
            {
                _host = host;
                _seeds = seeds;

                foreach (var seed in _seeds)
                {
                    var dot = new Ellipse
                    {
                        Width = seed.R * 2,
                        Height = seed.R * 2,
                        Fill = Gold,
                        Opacity = 0,
                        IsHitTestVisible = false,
                    };
                    _dots.Add(dot);
                    _host.Children.Add(dot);
                }

                Reflow();
            }

            /// <summary>Re-seat every speck for the host's current size. Cheap: property sets only,
            /// and the running clocks are on the transforms, so nothing is interrupted.</summary>
            internal void Reflow()
            {
                try
                {
                    double w = _host.Bounds.Width;
                    double h = _host.Bounds.Height;
                    if (w <= 0 || h <= 0)
                    {
                        // Before the first layout pass. An authored size is the fallback (the
                        // waiting panel's canvas has one); the fog's does not, and it gets a real
                        // size from the Bounds hook the moment it is arranged.
                        w = double.IsNaN(_host.Width) ? 0 : _host.Width;
                        h = double.IsNaN(_host.Height) ? 0 : _host.Height;
                        if (w <= 0 || h <= 0) return;
                    }

                    for (int i = 0; i < _dots.Count; i++)
                    {
                        Canvas.SetLeft(_dots[i], _seeds[i].Fx * w - _seeds[i].R);
                        Canvas.SetTop(_dots[i], _seeds[i].Fy * h - _seeds[i].R);
                    }
                }
                catch { /* a canvas mid-teardown is not worth a log line */ }
            }

            /// <summary>Start every speck's drift. Idempotent — a second call while running leaves
            /// the existing clocks alone rather than restarting the whole field in lockstep, which
            /// is the one thing that would make nine hand-placed specks look like a machine.</summary>
            internal void Start(CancellationToken token)
            {
                if (_running) return;
                _running = true;

                Reflow();

                for (int i = 0; i < _dots.Count; i++)
                {
                    var seed = _seeds[i];
                    var dot = _dots[i];
                    var duration = TimeSpan.FromSeconds(seed.Seconds);

                    // Each speck starts a fraction of its own cycle later, which is what keeps the
                    // field from breathing as one animal.
                    var offset = TimeSpan.FromSeconds(seed.Seconds * (i / (double)Math.Max(1, _dots.Count)));

                    var rise = new Animation
                    {
                        Duration = duration,
                        IterationCount = IterationCount.Infinite,
                        Delay = offset,
                        Children =
                        {
                            new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(TranslateTransform.YProperty, 0d) } },
                            new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(TranslateTransform.YProperty, -RiseDistance) } },
                        },
                    };

                    var fade = new Animation
                    {
                        Duration = duration,
                        IterationCount = IterationCount.Infinite,
                        Delay = offset,
                        Easing = new SineEaseInOut(),
                        Children =
                        {
                            new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 0d) } },
                            new KeyFrame { Cue = new Cue(0.42d), Setters = { new Setter(OpacityProperty, seed.Peak) } },
                            new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 0d) } },
                        },
                    };

                    _ = rise.RunAsync(dot, token);
                    _ = fade.RunAsync(dot, token);
                }
            }

            /// <summary>Park the field invisible. The clocks themselves are cancelled by the token
            /// the owner passed to <see cref="Start"/>; this is the resting state behind them.</summary>
            internal void Stop()
            {
                _running = false;
                foreach (var dot in _dots)
                {
                    try
                    {
                        dot.RenderTransform = null;
                        dot.Opacity = 0;
                    }
                    catch { /* teardown race */ }
                }
            }
        }
    }
}
